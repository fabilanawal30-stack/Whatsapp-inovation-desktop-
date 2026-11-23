using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TALKORA.Core;

/// <summary>Résultat du test d'un contact avant l'envoi.</summary>
public enum EtatContact
{
    AppPresente,   // la machine répond ET Talkora est ouvert
    AppAbsente,    // la machine répond, mais Talkora n'est pas installé ou pas lancé
    Injoignable    // la machine ne répond pas (éteinte, hors réseau, pare-feu)
}

/// <summary>
/// Réseau pair-à-pair : chaque poste écoute sur un port et peut écrire aux autres.
/// Protocole (chaque en-tête est une ligne de texte) :
///   "TALKORA_HELLO"        -> "TALKORA_OK"
///   "MSG:" + JSON          -> "TALKORA_RECU"
///   "FILE:" + JSON + octets du fichier (TailleFichier octets) -> "TALKORA_RECU"
/// </summary>
public class ReseauService : IDisposable
{
    public const long TailleMaxFichier = 100L * 1024 * 1024;   // 100 Mo

    private const string Salutation = "TALKORA_HELLO";
    private const string ReponseSalutation = "TALKORA_OK";
    private const string PrefixeMessage = "MSG:";
    private const string PrefixeFichier = "FILE:";
    private const string AccuseReception = "TALKORA_RECU";
    private const int LongueurMaxLigne = 1_000_000;

    private readonly int _port;
    private readonly FichierService _fichiers;
    private TcpListener? _ecouteur;
    private CancellationTokenSource? _annulation;

    // Déclenché à chaque message reçu, sur un thread d'arrière-plan :
    // dans l'interface, passer par BeginInvoke avant de toucher aux contrôles.
    public event Action<MessageChat>? MessageRecu;

    // Permet de refuser un expéditeur (contact bloqué) AVANT de lire son message ou son fichier.
    // Appelé sur un thread d'arrière-plan : ne rien y faire qui ne soit pas thread-safe.
    public Func<string, bool>? AccepteExpediteur { get; set; }

    // Chiffrement TLS (étape 13). Null = connexions en clair (tests uniquement).
    public CanalSecurise? Securite { get; set; }

    // Ouvre le flux d'une connexion : chiffré si Securite est défini
    private async Task<Stream> OuvrirFluxAsync(TcpClient client, string ip, bool entrant, CancellationToken jeton)
    {
        Stream brut = client.GetStream();
        if (Securite is null)
            return brut;
        return entrant ? await Securite.ServeurAsync(brut, ip, jeton)
                       : await Securite.ClientAsync(brut, ip, jeton);
    }

    public ReseauService(int port = 5000, FichierService? fichiers = null)
    {
        _port = port;
        _fichiers = fichiers ?? new FichierService();
    }

    // ===================== RÉCEPTION =====================

    public void Demarrer()
    {
        if (_ecouteur is not null)
            return;

        _annulation = new CancellationTokenSource();
        _ecouteur = new TcpListener(IPAddress.Any, _port);
        _ecouteur.Start();   // lève SocketException si le port est déjà utilisé

        _ = BoucleEcouteAsync(_ecouteur, _annulation.Token);
    }

    public void Arreter()
    {
        _annulation?.Cancel();
        _ecouteur?.Stop();
        _ecouteur = null;
    }

    private async Task BoucleEcouteAsync(TcpListener ecouteur, CancellationToken jeton)
    {
        try
        {
            while (!jeton.IsCancellationRequested)
            {
                TcpClient client = await ecouteur.AcceptTcpClientAsync(jeton);
                _ = TraiterClientAsync(client);   // chaque connexion est traitée à part
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task TraiterClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                string ipDistante = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                Stream fluxSecurise;
                using (var delaiTls = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    fluxSecurise = await OuvrirFluxAsync(client, ipDistante, true, delaiTls.Token);
                using var flux = new BufferedStream(fluxSecurise);

                string? ligne;
                using (var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    ligne = await LireLigneAsync(flux, delai.Token);

                if (ligne is null)
                    return;

                if (ligne == Salutation)
                {
                    await EcrireLigneAsync(flux, ReponseSalutation);
                    return;
                }

                bool estMessage = ligne.StartsWith(PrefixeMessage);
                bool estFichier = ligne.StartsWith(PrefixeFichier);
                if (!estMessage && !estFichier)
                    return;

                // Contact bloqué : on ferme sans rien lire de plus
                if (AccepteExpediteur is not null && !AccepteExpediteur(ipDistante))
                    return;

                string json = ligne.Substring(estMessage ? PrefixeMessage.Length : PrefixeFichier.Length);
                var message = JsonSerializer.Deserialize<MessageChat>(json);
                if (message is null)
                    return;

                // On ne fait pas confiance à ce que l'expéditeur écrit sur lui-même :
                // l'IP vient de la vraie connexion, et le chemin local est toujours remis à zéro
                // (sinon quelqu'un pourrait désigner un fichier de NOTRE ordinateur).
                message.AdresseIpExpediteur = ipDistante;
                message.CheminLocal = "";
                message.IntegriteOk = null;
                message.Etat = EtatMessage.Recu;
                MessageActions.AssainirRecu(message);

                if (estMessage)
                {
                    if (message.Type is TypeMessage.Fichier or TypeMessage.Vocal or TypeMessage.StatutImage)
                        return;     // un fichier doit arriver avec ses octets
                }
                else
                {
                    if (message.Type is not (TypeMessage.Fichier or TypeMessage.Vocal or TypeMessage.StatutImage))
                        return;
                    if (!await RecevoirFichierAsync(flux, message))
                        return;
                }

                MessageRecu?.Invoke(message);
                await EcrireLigneAsync(flux, AccuseReception);
            }
            catch (Exception ex) when (ex is IOException or JsonException or AuthenticationException
                                          or SocketException or OperationCanceledException)
            {
                // Connexion coupée ou données invalides : on ignore ce client
            }
        }
    }

    // Lit exactement TailleFichier octets et les range dans le dossier de Talkora
    private async Task<bool> RecevoirFichierAsync(Stream flux, MessageChat message)
    {
        long taille = message.TailleFichier;
        if (taille < 0 || taille > TailleMaxFichier)
            return false;

        message.NomFichier = FichierService.NomSecurise(message.NomFichier);
        string chemin = _fichiers.CheminPour(message.Id, message.NomFichier);
        string partiel = chemin + ".part";
        bool complet = false;

        try
        {
            await using (var sortie = new FileStream(partiel, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var tampon = new byte[64 * 1024];
                long restant = taille;

                while (restant > 0)
                {
                    // Si plus rien n'arrive pendant 30 s, on abandonne
                    using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    int lus = await flux.ReadAsync(tampon.AsMemory(0, (int)Math.Min(tampon.Length, restant)), delai.Token);
                    if (lus == 0)
                        return false;    // connexion coupée : transfert interrompu

                    await sortie.WriteAsync(tampon.AsMemory(0, lus));
                    restant -= lus;
                }
            }

            File.Move(partiel, chemin, overwrite: true);
            message.CheminLocal = chemin;
            message.IntegriteOk = IntegriteFichier.Verifier(chemin, message.Sha256);
            complet = true;
            return true;
        }
        finally
        {
            if (!complet && File.Exists(partiel))
            {
                try { File.Delete(partiel); } catch (IOException) { }
            }
        }
    }

    // ===================== ENVOI =====================

    // Teste un contact : machine joignable ? Talkora présent ?
    public async Task<EtatContact> VerifierContactAsync(Contact contact)
    {
        bool connecte = false;
        try
        {
            using var client = new TcpClient();
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(contact.AdresseIp, contact.Port, delai.Token);
            connecte = true;

            // Si l'autre machine n'a pas Talkora (ou une ancienne version sans TLS), la négociation échoue : AppAbsente
            using var flux = await OuvrirFluxAsync(client, contact.AdresseIp, false, delai.Token);
            await EcrireLigneAsync(flux, Salutation);
            string? reponse = await LireLigneAsync(flux, delai.Token);

            return reponse == ReponseSalutation ? EtatContact.AppPresente : EtatContact.AppAbsente;
        }
        catch (OperationCanceledException)
        {
            return connecte ? EtatContact.AppAbsente : EtatContact.Injoignable;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return EtatContact.AppAbsente;
        }
        catch (SocketException)
        {
            return EtatContact.Injoignable;
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
            return EtatContact.AppAbsente;
        }
    }

    // Envoie un message texte (ou une action). Vrai si le contact a confirmé la réception.
    public async Task<bool> EnvoyerMessageAsync(Contact contact, MessageChat message)
    {
        try
        {
            using var client = new TcpClient();
            using (var delaiConnexion = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await client.ConnectAsync(contact.AdresseIp, contact.Port, delaiConnexion.Token);

            Stream fluxTls;
            using (var delaiTls = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                fluxTls = await OuvrirFluxAsync(client, contact.AdresseIp, false, delaiTls.Token);
            using var flux = new BufferedStream(fluxTls);
            await EcrireLigneAsync(flux, PrefixeMessage + Serialiser(message));

            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await LireLigneAsync(flux, delai.Token) == AccuseReception;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or AuthenticationException)
        {
            return false;
        }
    }

    // Envoie un fichier ou un message vocal : l'en-tête JSON, puis les octets.
    public async Task<bool> EnvoyerFichierAsync(Contact contact, MessageChat message, string cheminFichier)
    {
        try
        {
            var info = new FileInfo(cheminFichier);
            if (!info.Exists || info.Length > TailleMaxFichier)
                return false;

            message.TailleFichier = info.Length;
            message.IntegriteOk = null;
            message.Sha256 = IntegriteFichier.Calculer(cheminFichier);

            using var client = new TcpClient();
            using (var delaiConnexion = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await client.ConnectAsync(contact.AdresseIp, contact.Port, delaiConnexion.Token);

            Stream fluxTls;
            using (var delaiTls = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                fluxTls = await OuvrirFluxAsync(client, contact.AdresseIp, false, delaiTls.Token);
            using var flux = new BufferedStream(fluxTls);
            await EcrireLigneAsync(flux, PrefixeFichier + Serialiser(message));

            await using (var entree = new FileStream(cheminFichier, FileMode.Open, FileAccess.Read, FileShare.Read))
                await entree.CopyToAsync(flux, 64 * 1024);
            await flux.FlushAsync();

            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await LireLigneAsync(flux, delai.Token) == AccuseReception;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or AuthenticationException)
        {
            return false;
        }
    }

    // Diffuse un message (ou un fichier, si cheminFichier est donné) à tous les membres d'un groupe,
    // sauf moi et les contacts bloqués. Renvoie : IP du membre -> bien reçu ?
    public async Task<Dictionary<string, bool>> EnvoyerAuGroupeAsync(
        Groupe groupe, MessageChat message, string monAdresseIp, string? cheminFichier = null)
    {
        var envois = new List<(string Ip, Task<bool> Tache)>();

        foreach (var membre in groupe.Membres)
        {
            if (membre.AdresseIp == monAdresseIp || membre.EstBloque)
                continue;

            envois.Add((membre.AdresseIp, cheminFichier is null
                ? EnvoyerMessageAsync(membre, message)
                : EnvoyerFichierAsync(membre, message, cheminFichier)));
        }

        var resultats = new Dictionary<string, bool>();
        foreach (var (ip, tache) in envois)
            resultats[ip] = await tache;

        return resultats;
    }

    public void Dispose()
    {
        Arreter();
        _annulation?.Dispose();
    }

    // ===================== OUTILS =====================

    // Le chemin local est propre à notre ordinateur : on ne l'envoie jamais
    // (on travaille sur une copie JSON : le message d'origine n'est jamais modifié,
    //  donc plusieurs envois en parallèle sont sans danger)
    private static string Serialiser(MessageChat message)
    {
        var noeud = JsonSerializer.SerializeToNode(message)!.AsObject();
        noeud["CheminLocal"] = "";
        return noeud.ToJsonString();
    }

    // On lit octet par octet (et non avec un StreamReader) pour ne pas "avaler"
    // le début du fichier qui suit l'en-tête.
    private static async Task<string?> LireLigneAsync(Stream flux, CancellationToken jeton)
    {
        var octets = new List<byte>(256);
        var un = new byte[1];

        while (true)
        {
            int lus = await flux.ReadAsync(un.AsMemory(0, 1), jeton);
            if (lus == 0)
                return octets.Count == 0 ? null : Encoding.UTF8.GetString(octets.ToArray());

            if (un[0] == (byte)'\n')
                break;
            if (un[0] != (byte)'\r')
                octets.Add(un[0]);
            if (octets.Count > LongueurMaxLigne)
                throw new IOException("Ligne trop longue.");
        }

        return Encoding.UTF8.GetString(octets.ToArray());
    }

    private static async Task EcrireLigneAsync(Stream flux, string texte)
    {
        byte[] octets = new UTF8Encoding(false).GetBytes(texte + "\n");
        await flux.WriteAsync(octets);
        await flux.FlushAsync();
    }
}

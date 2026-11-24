using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TALKORA.Core;


public class Brouillon
{
    public string Cle { get; set; } = "";           // conversation : IP du contact ou Id du groupe
    public string Texte { get; set; } = "";         // vide = brouillon supprimé (« pierre tombale » : la suppression se propage aussi)
    public DateTime ModifieUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Les brouillons de toutes mes conversations (fichier brouillons.json). Le plus récent gagne.</summary>
public class StockBrouillons
{
    public const int TexteMax = 4000;
    public const int Max = 300;
    public const int JoursPierres = 30;

    private readonly string _chemin;
    private readonly object _verrou = new();
    private readonly Dictionary<string, Brouillon> _d = new();

    public StockBrouillons(string dossier = "DataTalkora")
    {
        _chemin = Path.Combine(dossier, "brouillons.json");
        try
        {
            if (File.Exists(_chemin))
                foreach (var b in JsonSerializer.Deserialize<List<Brouillon>>(File.ReadAllText(_chemin)) ?? new())
                    if (CleValide(b.Cle)) _d[b.Cle] = b;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public static bool CleValide(string? c) => !string.IsNullOrEmpty(c) && c.Length <= 64 && Regex.IsMatch(c, @"^[A-Za-z0-9.:_-]+$");

    public string? Obtenir(string? cle)
    {
        if (!CleValide(cle)) return null;
        lock (_verrou) return _d.TryGetValue(cle!, out var b) && b.Texte.Length > 0 ? b.Texte : null;
    }

    public DateTime DateDe(string cle) { lock (_verrou) return _d.TryGetValue(cle, out var b) ? b.ModifieUtc : DateTime.MinValue; }

    /// <summary>Enregistre le brouillon. Renvoie l'élément modifié, ou null si rien n'a changé.</summary>
    public Brouillon? Definir(string? cle, string? texte, DateTime maintenantUtc)
    {
        if (!CleValide(cle)) return null;
        texte = (texte ?? "");
        if (texte.Trim().Length == 0) texte = "";
        if (texte.Length > TexteMax) texte = texte[..TexteMax];
        lock (_verrou)
        {
            string avant = _d.TryGetValue(cle!, out var b0) ? b0.Texte : "";
            if (avant == texte) return null;
            var b = new Brouillon { Cle = cle!, Texte = texte, ModifieUtc = maintenantUtc };
            _d[cle!] = b;
            Sauvegarder();
            return b;
        }
    }

    public List<Brouillon> Depuis(DateTime utc)
    {
        lock (_verrou) return _d.Values.Where(b => b.ModifieUtc > utc).Select(b => new Brouillon { Cle = b.Cle, Texte = b.Texte, ModifieUtc = b.ModifieUtc }).ToList();
    }

    /// <summary>Fusionne des brouillons reçus : le plus récent gagne. Renvoie ceux qui ont été appliqués.</summary>
    public List<Brouillon> Fusionner(IEnumerable<Brouillon> recus, DateTime maintenantUtc)
    {
        var appliques = new List<Brouillon>();
        lock (_verrou)
        {
            foreach (var r in recus.Take(Max))
            {
                if (!CleValide(r.Cle) || (r.Texte ?? "").Length > TexteMax) continue;
                var date = r.ModifieUtc > maintenantUtc.AddMinutes(5) ? maintenantUtc : r.ModifieUtc;     // une horloge en avance ne doit pas « geler » un brouillon
                if (_d.TryGetValue(r.Cle, out var local) && local.ModifieUtc >= date) continue;
                if (!_d.ContainsKey(r.Cle) && _d.Count >= Max) continue;
                var b = new Brouillon { Cle = r.Cle, Texte = r.Texte ?? "", ModifieUtc = date };
                _d[r.Cle] = b;
                appliques.Add(b);
            }
            if (appliques.Count > 0) Sauvegarder();
        }
        return appliques;
    }

    private void Sauvegarder()
    {
        try
        {
            foreach (var k in _d.Where(kv => kv.Value.Texte.Length == 0 && kv.Value.ModifieUtc < DateTime.UtcNow.AddDays(-JoursPierres)).Select(kv => kv.Key).ToList()) _d.Remove(k);
            Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
            string tmp = _chemin + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_d.Values.ToList()));
            File.Move(tmp, _chemin, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

// ---------------------------------------------------------------------
//  Appareils appairés
// ---------------------------------------------------------------------

public class AppareilLie
{
    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string CleProtegee { get; set; } = "";       // jamais la clé en clair (DPAPI)
    public DateTime DerniereSyncUtc { get; set; } = DateTime.MinValue;
}

public class RegistreAppareils
{
    public const int Max = 5;
    public string MonId { get; set; } = Guid.NewGuid().ToString("N");
    public List<AppareilLie> Appareils { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] public string Dossier { get; set; } = "DataTalkora";

    public static RegistreAppareils Charger(string dossier = "DataTalkora")
    {
        RegistreAppareils? r = null;
        try
        {
            string c = Path.Combine(dossier, "appareils.json");
            if (File.Exists(c)) r = JsonSerializer.Deserialize<RegistreAppareils>(File.ReadAllText(c));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        r ??= new RegistreAppareils();
        if (!Regex.IsMatch(r.MonId ?? "", "^[a-f0-9]{32}$")) r.MonId = Guid.NewGuid().ToString("N");
        r.Dossier = dossier;
        if (!File.Exists(Path.Combine(dossier, "appareils.json"))) r.Sauvegarder();
        return r;
    }

    public void Sauvegarder()
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            string tmp = Path.Combine(Dossier, "appareils.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Path.Combine(Dossier, "appareils.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public AppareilLie? Trouver(string? id) => string.IsNullOrEmpty(id) ? null : Appareils.Find(a => a.Id == id);
    public AppareilLie? ParIp(string? ip) => string.IsNullOrEmpty(ip) ? null : Appareils.Find(a => a.Ip == ip);

    public AppareilLie Lier(string id, string nom, string ip, int port, byte[] cle, IProtecteurSecret prot)
    {
        var a = Trouver(id);
        if (a is null)
        {
            if (Appareils.Count >= Max) throw new InvalidOperationException($"Au plus {Max} appareils.");
            Appareils.Add(a = new AppareilLie { Id = id });
        }
        a.Nom = Contact.NettoyerNom(nom) ?? "Mon appareil";
        a.Ip = ip; a.Port = port is >= 1 and <= 65535 ? port : 5000;
        a.CleProtegee = prot.Proteger(Convert.ToBase64String(cle));
        Sauvegarder();
        return a;
    }

    public byte[]? Cle(AppareilLie a, IProtecteurSecret prot)
    {
        string? b64 = string.IsNullOrEmpty(a.CleProtegee) ? null : prot.Reveler(a.CleProtegee);
        try { return b64 is null ? null : Convert.FromBase64String(b64); } catch (FormatException) { return null; }
    }

    public void Retirer(string id) { Appareils.RemoveAll(a => a.Id == id); Sauvegarder(); }
}

// ---------------------------------------------------------------------
//  Appairage par code à 6 chiffres
//  A (qui garde la discussion) affiche un code ; B tape ce code. Le code ne circule JAMAIS : seulement des preuves HMAC.
// ---------------------------------------------------------------------

public class PaquetAppairage
{
    public string Action { get; set; } = "demande";     // "demande" (B -> A) puis "ok" (A -> B)
    public string IdAppareil { get; set; } = "";
    public string Nom { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string Nonce { get; set; } = "";
    public string Preuve { get; set; } = "";
}

public class Appairage
{
    public const int MinutesValidite = 5;
    public const int EssaisMax = 5;

    public string Code { get; }
    public DateTime Expire { get; }
    public int Essais { get; private set; }
    public bool Utilise { get; private set; }

    public Appairage(DateTime maintenant, string? code = null)
    {
        Code = code ?? GenererCode();
        Expire = maintenant.AddMinutes(MinutesValidite);
    }

    public static string GenererCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    public bool Valide(DateTime now) => !Utilise && Essais < EssaisMax && now < Expire;

    static byte[] Hmac(byte[] cle, string donnees) => HMACSHA256.HashData(cle, Encoding.UTF8.GetBytes(donnees));
    static byte[] CleCode(string code) => Encoding.UTF8.GetBytes("talkora-sync|" + code);

    /// <summary>B calcule sa preuve à partir du code tapé.</summary>
    public static PaquetAppairage Demande(string code, string idB, string nomB, int portB, string idA)
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new PaquetAppairage
        {
            Action = "demande", IdAppareil = idB, Nom = nomB, Port = portB, Nonce = nonce,
            Preuve = Convert.ToHexString(Hmac(CleCode(code), $"pair|{idB}|{idA}|{nonce}")).ToLowerInvariant()
        };
    }

    public static byte[] CleCommune(string code, string idA, string idB, string nonce)
        => Hmac(CleCode(code), $"key|{idA}|{idB}|{nonce}");

    /// <summary>A vérifie la demande de B. Renvoie la clé commune (et la confirmation à renvoyer), ou null (code faux, expiré, trop d'essais).</summary>
    public (byte[] Cle, PaquetAppairage Reponse)? Recevoir(PaquetAppairage p, string idA, DateTime now)
    {
        if (!Valide(now)) return null;
        if (p.Action != "demande" || !Regex.IsMatch(p.IdAppareil ?? "", "^[a-f0-9]{32}$") || !Regex.IsMatch(p.Nonce ?? "", "^[a-f0-9]{32}$")) return null;
        Essais++;
        byte[] attendu = Hmac(CleCode(Code), $"pair|{p.IdAppareil}|{idA}|{p.Nonce}");
        byte[] recu;
        try { recu = Convert.FromHexString(p.Preuve ?? ""); } catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(attendu, recu)) return null;
        Utilise = true;
        byte[] cle = CleCommune(Code, idA, p.IdAppareil, p.Nonce);
        return (cle, new PaquetAppairage
        {
            Action = "ok", IdAppareil = idA, Nonce = p.Nonce,
            Preuve = Convert.ToHexString(Hmac(cle, $"ok|{idA}|{p.IdAppareil}|{p.Nonce}")).ToLowerInvariant()
        });
    }

    /// <summary>B vérifie que A connaissait bien le code (confirmation) et obtient la clé commune.</summary>
    public static byte[]? Confirmer(PaquetAppairage reponse, string code, string idA, string idB, string nonce)
    {
        if (reponse.Action != "ok" || reponse.IdAppareil != idA || reponse.Nonce != nonce) return null;
        byte[] cle = CleCommune(code, idA, idB, nonce);
        byte[] attendu = Hmac(cle, $"ok|{idA}|{idB}|{nonce}");
        try { return CryptographicOperations.FixedTimeEquals(attendu, Convert.FromHexString(reponse.Preuve ?? "")) ? cle : null; }
        catch (FormatException) { return null; }
    }
}

// ---------------------------------------------------------------------
//  Signature et contenu des échanges
// ---------------------------------------------------------------------

public class ContactSync { public string Nom { get; set; } = ""; public string Ip { get; set; } = ""; public int Port { get; set; } = 5000; public string Langue { get; set; } = ""; }
public class EspaceSync { public string Espace { get; set; } = ""; public List<ItemTogether> Items { get; set; } = new(); }

public class PaquetSync
{
    public string Action { get; set; } = "donnees";
    public string DeIp { get; set; } = "";
    public List<Brouillon> Brouillons { get; set; } = new();
    public List<MessageChat> Messages { get; set; } = new();
    public List<EspaceSync> Espaces { get; set; } = new();
    public List<ContactSync> Contacts { get; set; } = new();
}

public static class ServiceSync
{
    public const int MessagesParPaquet = 150;
    public const int JsonMax = 400_000;
    public const int JoursMessages = 90;
    public const int MessagesMax = 2000;
    public const long FichierMax = 20L * 1024 * 1024;
    public const int JoursFichiers = 30;

    // ---- signature ----
    public static string Empreinte(MessageChat m)
    {
        string h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Contenu ?? ""))).ToLowerInvariant();
        return $"{m.Id:N}|{(int)m.Type}|{m.SyncDe}|{m.SyncIp}|{m.SyncOrigine}|{m.Destinataire}|{m.DateEnvoi.Ticks}|{h}|{m.NomFichier}|{m.TailleFichier}";
    }

    public static void Signer(MessageChat m, byte[] cle)
        => m.PreuveSync = Convert.ToHexString(HMACSHA256.HashData(cle, Encoding.UTF8.GetBytes(Empreinte(m)))).ToLowerInvariant();

    public static bool Verifier(MessageChat m, byte[] cle)
    {
        try
        {
            byte[] attendu = HMACSHA256.HashData(cle, Encoding.UTF8.GetBytes(Empreinte(m)));
            return CryptographicOperations.FixedTimeEquals(attendu, Convert.FromHexString(m.PreuveSync ?? ""));
        }
        catch (FormatException) { return false; }
    }

    // ---- paquets ----
    public static string Encoder(PaquetSync p) => JsonSerializer.Serialize(p);

    public static PaquetSync? Decoder(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 1_000_000) return null;
        try { return JsonSerializer.Deserialize<PaquetSync>(json); } catch (JsonException) { return null; }
    }

    public static MessageChat Cadre(string deId, string moiIp, string versIp, PaquetSync p, byte[] cle)
    {
        var m = new MessageChat
        {
            Type = TypeMessage.Sync, SyncDe = deId, SyncIp = moiIp, AdresseIpExpediteur = moiIp, Destinataire = versIp,
            Contenu = Encoder(p), Expediteur = "", Etat = EtatMessage.Envoye
        };
        Signer(m, cle);
        return m;
    }

    /// <summary>Messages à envoyer à l'appareil : récents, non-actions, depuis la dernière synchro (marge de 10 min pour les horloges).</summary>
    public static List<MessageChat> AEnvoyer(IEnumerable<MessageChat> tous, DateTime derniereSyncLocale, DateTime maintenant)
    {
        var depuis = derniereSyncLocale == DateTime.MinValue ? maintenant.AddDays(-JoursMessages) : derniereSyncLocale.AddMinutes(-10);
        return tous.Where(m => !m.EstAction && m.DateEnvoi >= depuis && m.DateEnvoi >= maintenant.AddDays(-JoursMessages) && m.SyncDe.Length == 0)
                   .OrderBy(m => m.DateEnvoi).TakeLast(MessagesMax).ToList();
    }

    /// <summary>Découpe en paquets assez petits pour une ligne du protocole.</summary>
    public static List<List<MessageChat>> Lots(IEnumerable<MessageChat> messages)
    {
        var lots = new List<List<MessageChat>>(); var cur = new List<MessageChat>(); int taille = 0;
        foreach (var m in messages)
        {
            int t = (m.Contenu?.Length ?? 0) + 600;
            if (cur.Count >= MessagesParPaquet || (taille + t > JsonMax && cur.Count > 0)) { lots.Add(cur); cur = new(); taille = 0; }
            cur.Add(m); taille += t;
        }
        if (cur.Count > 0) lots.Add(cur);
        return lots;
    }

    // ---- ce qui arrive de mon autre appareil ----

    private static readonly HashSet<TypeMessage> TypesAcceptes = new() { TypeMessage.Texte, TypeMessage.Fichier, TypeMessage.Vocal, TypeMessage.Sondage };

    /// <summary>
    /// Le message d'un autre de MES appareils : ses adresses « moi » deviennent MON adresse, pour qu'il apparaisse comme
    /// envoyé par moi (ou reçu par moi) dans ma conversation. Null s'il est invalide ou dangereux (action, taille...).
    /// </summary>
    public static MessageChat? Importer(MessageChat? recu, string ipAppareil, string monIp)
        => Importer(recu, new[] { ipAppareil }, monIp);

    public static MessageChat? Importer(MessageChat? recu, IEnumerable<string> ipsAppareil, string monIp)
    {
        var ips = ipsAppareil.Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
        if (recu is not null && (recu.SyncIp ?? "").Length is > 0 and <= 64) ips.Add(recu.SyncIp);
        if (recu is null || recu.Id == Guid.Empty || !TypesAcceptes.Contains(recu.Type)) return null;
        if ((recu.Contenu?.Length ?? 0) > 20_000 || (recu.AdresseIpExpediteur?.Length ?? 0) > 64 || (recu.Destinataire?.Length ?? 0) > 64) return null;
        var m = JsonSerializer.Deserialize<MessageChat>(JsonSerializer.Serialize(recu))!;
        if ((recu.SyncOrigine ?? "").Length is > 0 and <= 64) m.AdresseIpExpediteur = recu.SyncOrigine;   // fichier : le réseau a remplacé l'expéditeur
        if (ips.Contains(m.AdresseIpExpediteur)) m.AdresseIpExpediteur = monIp;
        if (ips.Contains(m.Destinataire)) m.Destinataire = monIp;
        if (m.DateEnvoi > DateTime.Now.AddHours(1)) m.DateEnvoi = DateTime.Now;
        m.SyncDe = ""; m.PreuveSync = ""; m.SyncOrigine = ""; m.SyncIp = ""; m.Auto = recu.Auto;
        m.CheminLocal = "";                      // le chemin d'un AUTRE ordinateur ne vaut rien ici ; le fichier arrive à part
        if (m.Type is TypeMessage.Fichier or TypeMessage.Vocal) m.NomFichier = (m.NomFichier ?? "").Split('/', '\\').Last();
        return m;
    }

    public static List<ContactSync> ContactsValides(IEnumerable<ContactSync> l)
        => l.Take(200).Where(c => IPAddress.TryParse(c.Ip, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && c.Port is >= 1 and <= 65535)
            .Select(c => new ContactSync { Nom = Contact.NettoyerNom(c.Nom) ?? c.Ip, Ip = c.Ip, Port = c.Port, Langue = (c.Langue ?? "").Length <= 8 ? c.Langue ?? "" : "" }).ToList();
}

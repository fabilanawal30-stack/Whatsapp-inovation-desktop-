using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TALKORA.Core;

/// <summary>« Je suis là » : ce que chaque Talkora annonce toutes les 5 secondes sur le réseau local.</summary>
public class PaquetPresence
{
    public string Pseudo { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string Id { get; set; } = "";          // identifiant de CETTE installation : une machine à plusieurs cartes réseau n'apparaît qu'une fois
}

public record Voisin(string Pseudo, string Ip, int Port, DateTime VuLe, string Id = "");

public static class CodecPresence
{
    public const string Marque = "TALKORA_PRESENCE|";
    public const int TailleMax = 512;

    public static byte[] Encoder(string pseudo, int port, string id = "")
        => Encoding.UTF8.GetBytes(Marque + JsonSerializer.Serialize(new PaquetPresence { Pseudo = pseudo, Port = port, Id = id }));

    /// <summary>Lit un paquet reçu ; null s'il est invalide (trop gros, pas de marque, JSON cassé, pseudo vide, port absurde).</summary>
    public static PaquetPresence? Decoder(byte[] donnees)
    {
        if (donnees.Length == 0 || donnees.Length > TailleMax) return null;
        string texte;
        try { texte = new UTF8Encoding(false, true).GetString(donnees); } catch (ArgumentException) { return null; }
        if (!texte.StartsWith(Marque, StringComparison.Ordinal)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<PaquetPresence>(texte[Marque.Length..]);
            if (p is null || p.Port is < 1 or > 65535) return null;
            string? nom = Contact.NettoyerNom(p.Pseudo);
            if (nom is null) return null;
            p.Pseudo = nom;
            p.Id = (p.Id ?? "").Length <= 40 && System.Text.RegularExpressions.Regex.IsMatch(p.Id ?? "", "^[A-Za-z0-9-]*$") ? (p.Id ?? "") : "";
            return p;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Les personnes vues récemment sur le réseau. L'adresse IP est celle de l'EXPÉDITEUR du paquet (impossible à usurper par le contenu).</summary>
public class TableVoisins
{
    public const int DureeVieSecondes = 20;
    private readonly object _verrou = new();
    private readonly Dictionary<string, Voisin> _parIp = new();

    /// <summary>Enregistre une présence. Renvoie vrai si c'est une NOUVELLE personne (ou si son nom/port a changé).</summary>
    public bool Mettre(string ip, PaquetPresence p, DateTime maintenant)
    {
        lock (_verrou)
        {
            bool nouveau = !_parIp.TryGetValue(ip, out var avant) || maintenant - avant.VuLe > TimeSpan.FromSeconds(DureeVieSecondes)
                           || avant.Pseudo != p.Pseudo || avant.Port != p.Port;
            _parIp[ip] = new Voisin(p.Pseudo, ip, p.Port, maintenant, p.Id);
            return nouveau;
        }
    }

    public List<Voisin> Actuels(DateTime maintenant)
    {
        lock (_verrou)
        {
            foreach (var cle in _parIp.Where(kv => maintenant - kv.Value.VuLe > TimeSpan.FromSeconds(DureeVieSecondes)).Select(kv => kv.Key).ToList())
                _parIp.Remove(cle);
            return _parIp.Values.OrderBy(v => v.Pseudo, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}

/// <summary>
/// Quand une même machine (même Id) nous écrit depuis plusieurs adresses (cartes Wi-Fi, VirtualBox, VMware, VPN...),
/// on n'en garde qu'une : de préférence celle qui est sur le MÊME réseau (/24) que ma meilleure adresse.
/// </summary>
public class ChoixAdresse
{
    private readonly Dictionary<string, (string Ip, DateTime Vu)> _choisie = new();

    public static bool MemeReseau(string a, string b)
    {
        var x = a.Split('.'); var y = b.Split('.');
        return x.Length == 4 && y.Length == 4 && x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
    }

    /// <summary>Vrai si on doit prendre en compte ce paquet (sinon : c'est une adresse « en double » de la même machine).</summary>
    public bool Retenir(string id, string ip, string monIp, DateTime now)
    {
        if (id.Length == 0) return true;                         // ancienne version sans Id : comportement d'avant
        if (!_choisie.TryGetValue(id, out var c) || now - c.Vu > TimeSpan.FromSeconds(TableVoisins.DureeVieSecondes) || c.Ip == ip)
        { _choisie[id] = (ip, now); return true; }
        if (MemeReseau(ip, monIp) && !MemeReseau(c.Ip, monIp))   // une meilleure adresse : on bascule
        { _choisie[id] = (ip, now); return true; }
        return false;
    }
}

/// <summary>
/// Découverte automatique sur le réseau local, sans serveur ni saisie d'adresse (comme « Appareils à proximité »).
/// Envoie sa présence en diffusion UDP (port 5001) et écoute celle des autres. Fonctionne 100 % hors ligne.
/// </summary>
public sealed class ServiceDecouverte : IDisposable
{
    public const int PortDecouverte = 5001;
    public const int PeriodeMs = 5000;

    private readonly string _pseudo;
    private readonly int _portTcp;
    private readonly Func<string, bool> _estMoi;
    private readonly int _portUdp;
    private readonly IPEndPoint[] _ciblesSupplementaires;
    private readonly bool _accepterBoucle;
    private readonly string _id;
    private readonly Func<string> _monIp;
    private readonly ChoixAdresse _choix = new();
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _udp;

    public TableVoisins Table { get; } = new();

    /// <summary>Levé (sur un thread d'arrière-plan) quand une personne apparaît pour la première fois.</summary>
    public event Action<Voisin>? VoisinApparu;

    public ServiceDecouverte(string pseudo, int portTcp, Func<string, bool> estMoi,
                             int portUdp = PortDecouverte, IEnumerable<IPEndPoint>? ciblesSupplementaires = null, bool accepterBoucle = false,
                             string? id = null, Func<string>? monIp = null)
    {
        _id = id ?? Guid.NewGuid().ToString("N");
        _monIp = monIp ?? (() => "");
        _pseudo = pseudo; _portTcp = portTcp; _estMoi = estMoi; _portUdp = portUdp;
        _ciblesSupplementaires = ciblesSupplementaires?.ToArray() ?? Array.Empty<IPEndPoint>();
        _accepterBoucle = accepterBoucle;
    }

    /// <summary>Démarre. Renvoie faux (sans planter) si le port est déjà pris : on se contentera de l'ajout manuel.</summary>
    public bool Demarrer()
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _portUdp));
        }
        catch (SocketException) { _udp?.Dispose(); _udp = null; return false; }

        _ = Task.Run(() => EcouterAsync(_stop.Token));
        _ = Task.Run(() => AnnoncerAsync(_stop.Token));
        return true;
    }

    private async Task EcouterAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udp is not null)
        {
            try
            {
                var r = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                if (r.RemoteEndPoint.AddressFamily != AddressFamily.InterNetwork) continue;
                string ip = r.RemoteEndPoint.Address.ToString();
                if (_estMoi(ip) || EstAdresseDeCetOrdinateur(r.RemoteEndPoint.Address) || (!_accepterBoucle && IPAddress.IsLoopback(r.RemoteEndPoint.Address))) continue;
                var p = CodecPresence.Decoder(r.Buffer);
                if (p is null || (p.Id.Length > 0 && p.Id == _id)) continue;          // mon propre paquet
                if (!_choix.Retenir(p.Id, ip, _monIp(), DateTime.Now)) continue;       // même machine, autre carte réseau
                if (Table.Mettre(ip, p, DateTime.Now))
                    VoisinApparu?.Invoke(new Voisin(p.Pseudo, ip, p.Port, DateTime.Now, p.Id));
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { await Task.Delay(500, CancellationToken.None).ConfigureAwait(false); }   // ex. ICMP « port injoignable » : on continue
        }
    }

    /// <summary>Mes propres cartes réseau (Wi-Fi, Ethernet, VirtualBox, VMware...) : mon diffusion m'arrive aussi par elles, ce n'est pas un voisin.</summary>
    internal static bool EstAdresseDeCetOrdinateur(IPAddress a)
    {
        try
        {
            if (IPAddress.IsLoopback(a)) return false;      // la boucle locale reste gérée par _accepterBoucle
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                    if (u.Address.Equals(a)) return true;
        }
        catch (NetworkInformationException) { }
        return false;
    }

    private async Task AnnoncerAsync(CancellationToken ct)
    {
        byte[] paquet = CodecPresence.Encoder(_pseudo, _portTcp, _id);
        while (!ct.IsCancellationRequested && _udp is not null)
        {
            foreach (var cible in Cibles())
            {
                try { await _udp.SendAsync(paquet, cible, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { }       // une carte sans réseau : on essaie les autres
            }
            try { await Task.Delay(PeriodeMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Diffusion générale + diffusion propre à CHAQUE réseau (plus fiable quand il y a plusieurs cartes, dont des virtuelles).</summary>
    private IEnumerable<IPEndPoint> Cibles()
    {
        var liste = new List<IPEndPoint> { new(IPAddress.Broadcast, _portUdp) };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask is null) continue;
                    var b = a.Address.GetAddressBytes(); var m = a.IPv4Mask.GetAddressBytes();
                    var diff = new byte[4];
                    for (int i = 0; i < 4; i++) diff[i] = (byte)(b[i] | ~m[i]);
                    liste.Add(new IPEndPoint(new IPAddress(diff), _portUdp));
                }
            }
        }
        catch (NetworkInformationException) { }
        liste.AddRange(_ciblesSupplementaires);
        return liste.DistinctBy(e => e.ToString());
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _udp?.Dispose(); } catch (SocketException) { }
        _udp = null;
    }
}

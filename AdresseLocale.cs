using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TALKORA.Core;

/// <summary>
/// Trouve la VRAIE adresse IP de l'ordinateur sur le réseau (Wi-Fi / câble), pas celle d'une carte virtuelle
/// (VirtualBox, VMware, WSL, Hyper-V, Docker créent des cartes « fantômes » : 192.168.56.1, 172.x…).
/// </summary>
public static class AdresseLocale
{
    public record Candidate(string Ip, string Carte, int Score, bool Virtuelle, bool PasserelleTrouvee);

    private static readonly string[] MotsVirtuels =
        { "virtualbox", "vmware", "hyper-v", "vethernet", "wsl", "docker", "vpn", "tap", "tun", "loopback", "pseudo", "bluetooth", "host-only", "vbox" };

    /// <summary>Toutes les adresses IPv4 possibles, la meilleure en premier.</summary>
    public static List<Candidate> Lister()
    {
        var res = new List<Candidate>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                bool passerelle = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                 && !g.Address.Equals(IPAddress.Any));
                string nom = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool virtuelle = MotsVirtuels.Any(nom.Contains);
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = a.Address.ToString();
                    res.Add(new Candidate(ip, ni.Name, Noter(ip, ni.NetworkInterfaceType, passerelle, virtuelle), virtuelle, passerelle));
                }
            }
        }
        catch (NetworkInformationException) { }
        return res.OrderByDescending(c => c.Score).ThenBy(c => c.Ip, StringComparer.Ordinal).ToList();
    }

    /// <summary>La meilleure adresse, ou 127.0.0.1 si aucun réseau.</summary>
    public static string Meilleure() => Lister().FirstOrDefault(c => c.Score > 0)?.Ip ?? "127.0.0.1";

    /// <summary>
    /// Remet l'adresse du profil à jour AUTOMATIQUEMENT (à chaque démarrage) : si l'IP enregistrée n'existe plus sur cet ordinateur
    /// (box/DHCP/autre réseau) ou si c'est une carte virtuelle alors qu'une vraie carte existe, on prend la meilleure.
    /// L'ancienne IP est gardée dans AnciennesAdresses. Renvoie vrai si le profil a changé.
    /// </summary>
    public static bool MettreProfilAJour(ProfilUtilisateur profil, IReadOnlyList<Candidate> candidats)
    {
        var meilleur = candidats.FirstOrDefault(c => c.Score > 0);
        if (meilleur is null) return false;                                   // pas de réseau : on ne touche à rien
        var actuel = candidats.FirstOrDefault(c => c.Ip == profil.AdresseIp);
        bool aGarder = actuel is not null && actuel.Score > 0 && !(actuel.Virtuelle && !meilleur.Virtuelle);
        if (aGarder) return false;
        profil.ChangerAdresse(meilleur.Ip);
        return true;
    }

    /// <summary>Note d'une adresse : plus c'est haut, plus c'est probablement la bonne.</summary>
    public static int Noter(string ip, NetworkInterfaceType type, bool passerelle, bool virtuelle)
    {
        if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) return -100;      // « pas de réseau » (APIPA) ou boucle locale
        int s = 0;
        if (passerelle) s += 100;                                                 // une vraie carte connectée à un routeur/box/point d'accès a une passerelle
        if (virtuelle) s -= 80;
        if (type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet) s += 20;
        if (ip.StartsWith("192.168.56.")) s -= 80;                                // réseau « host-only » de VirtualBox
        if (ip.StartsWith("192.168.") || ip.StartsWith("10.")) s += 10;
        else if (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out int b) && b is >= 16 and <= 31) s += 5;
        return s;
    }
}

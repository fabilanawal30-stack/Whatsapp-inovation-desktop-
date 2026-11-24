using System.Security.Cryptography;

namespace TALKORA.Core;

/// <summary>
/// Talkora Avatar : ma photo de profil, envoyée à mes contacts (petite image JPEG/PNG dans un message-action),
/// et celles que mes contacts m'envoient. Tout est fichier local : DataTalkora/Avatars/moi.jpg et {ip}.jpg.
/// Le Core ne manipule que des octets ; le redimensionnement (System.Drawing) est fait côté WinForms.
/// </summary>
public static class ServiceAvatar
{
    public const int OctetsMax = 60_000;        // une photo de 160 px fait ~10 Ko : 60 Ko est déjà très large
    public const int Cote = 160;                // côté (px) de l'image réduite

    public static string Empreinte(byte[] octets) => Convert.ToHexString(SHA256.HashData(octets)).ToLowerInvariant();

    /// <summary>JPEG (FF D8 FF) ou PNG (89 50 4E 47), pas trop gros. Une vérification de forme, pas de sécurité : l'image est ensuite ré-encodée.</summary>
    public static bool EstImageValide(byte[]? o)
    {
        if (o is null || o.Length < 16 || o.Length > OctetsMax) return false;
        bool jpeg = o[0] == 0xFF && o[1] == 0xD8 && o[2] == 0xFF;
        bool png = o[0] == 0x89 && o[1] == 0x50 && o[2] == 0x4E && o[3] == 0x47;
        return jpeg || png;
    }

    /// <summary>Le message qui annonce mon avatar. « octets » null = je retire ma photo. Renvoie null si l'image est invalide.</summary>
    public static MessageChat? CreerMessage(ProfilUtilisateur moi, string destinataire, byte[]? octets)
    {
        if (octets is not null && !EstImageValide(octets)) return null;
        var m = MessageActions.Nouveau(moi, destinataire, octets is null ? "" : Empreinte(octets) + "|" + Convert.ToBase64String(octets));
        m.Type = TypeMessage.Avatar;
        return m;
    }

    public record AvatarRecu(bool Retire, string Empreinte, byte[]? Octets);

    /// <summary>Lit un message Avatar reçu. Null si ce n'est pas un avatar valide (mauvaise empreinte, trop gros, pas une image…).</summary>
    public static AvatarRecu? Lire(MessageChat m)
    {
        if (m.Type != TypeMessage.Avatar) return null;
        string c = m.Contenu ?? "";
        if (c.Length == 0) return new(true, "", null);
        if (c.Length > OctetsMax * 2) return null;                 // base64 : ~4/3 de la taille
        int i = c.IndexOf('|');
        if (i != 64) return null;
        try
        {
            byte[] octets = Convert.FromBase64String(c[(i + 1)..]);
            if (!EstImageValide(octets)) return null;
            string h = Empreinte(octets);
            if (!string.Equals(h, c[..i], StringComparison.OrdinalIgnoreCase)) return null;
            return new(false, h, octets);
        }
        catch (FormatException) { return null; }
    }

    // ===================== FICHIERS =====================

    /// <summary>Nom de fichier sûr pour une adresse IP : jamais de chemin (« ../ »).</summary>
    public static string? NomFichier(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip.Length > 45) return null;
        foreach (char c in ip)
            if (!(char.IsAsciiHexDigit(c) || c == '.' || c == ':')) return null;
        return ip.Replace(':', '_') + ".img";
    }

    public static string Dossier(string dataTalkora) => Path.Combine(dataTalkora, "Avatars");

    public static string CheminMoi(string dataTalkora) => Path.Combine(Dossier(dataTalkora), "moi.img");

    public static string? Chemin(string dataTalkora, string ip)
        => NomFichier(ip) is string n ? Path.Combine(Dossier(dataTalkora), n) : null;

    public static bool Enregistrer(string dataTalkora, string ip, byte[] octets)
    {
        string? chemin = Chemin(dataTalkora, ip);
        if (chemin is null || !EstImageValide(octets)) return false;
        Directory.CreateDirectory(Dossier(dataTalkora));
        File.WriteAllBytes(chemin, octets);
        return true;
    }

    public static void EnregistrerMoi(string dataTalkora, byte[] octets)
    {
        if (!EstImageValide(octets)) throw new ArgumentException("Image invalide ou trop grosse.");
        Directory.CreateDirectory(Dossier(dataTalkora));
        File.WriteAllBytes(CheminMoi(dataTalkora), octets);
    }

    public static byte[]? Charger(string? chemin)
    {
        try { return chemin is not null && File.Exists(chemin) ? File.ReadAllBytes(chemin) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void Supprimer(string? chemin)
    {
        try { if (chemin is not null && File.Exists(chemin)) File.Delete(chemin); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Évite de renvoyer mon avatar à chaque vérification : une fois par heure et par contact, sauf si on force.</summary>
public class SuiviEnvoisAvatar
{
    private readonly Dictionary<string, DateTime> _derniers = new();
    public TimeSpan Delai { get; set; } = TimeSpan.FromHours(1);

    public bool DoitEnvoyer(string ip, DateTime maintenant, bool forcer = false)
        => forcer || !_derniers.TryGetValue(ip, out var d) || maintenant - d >= Delai;

    public void Noter(string ip, DateTime maintenant) => _derniers[ip] = maintenant;

    public void Oublier() => _derniers.Clear();
}

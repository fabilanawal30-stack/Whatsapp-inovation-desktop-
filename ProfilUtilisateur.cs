using System.Security.Cryptography;

namespace TALKORA.Core;

/// <summary>
/// Le profil de la personne qui utilise ce poste.
/// Le PIN n'est jamais stocké en clair : on garde un hash + un sel.
/// </summary>
public class ProfilUtilisateur
{
    public string Pseudo { get; set; } = "";
    public string AdresseIp { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string Langue { get; set; } = "Français (fr)";
    public bool BlindageActif { get; set; }

    // Mes anciennes adresses IP : pour reconnaître mes anciens messages comme MIENS après un changement d'IP
    public List<string> AnciennesAdresses { get; set; } = new();

    /// <summary>Cette adresse est-elle (ou a-t-elle été) la mienne ?</summary>
    public bool EstAdresseMoi(string? ip)
        => !string.IsNullOrEmpty(ip) && (ip == AdresseIp || AnciennesAdresses.Contains(ip));

    /// <summary>Change mon adresse IP en gardant la trace de l'ancienne.</summary>
    public void ChangerAdresse(string nouvelle)
    {
        if (string.IsNullOrWhiteSpace(nouvelle) || nouvelle == AdresseIp) return;
        if (!string.IsNullOrEmpty(AdresseIp) && !AnciennesAdresses.Contains(AdresseIp))
            AnciennesAdresses.Add(AdresseIp);
        AnciennesAdresses.Remove(nouvelle);          // revenir à une ancienne adresse : elle redevient l'actuelle
        AdresseIp = nouvelle;
        if (AnciennesAdresses.Count > 10) AnciennesAdresses.RemoveAt(0);
    }

    // Empreinte du PIN (hash) et sel aléatoire, enregistrés en Base64
    public string PinHash { get; set; } = "";
    public string PinSel { get; set; } = "";

    private const int Iterations = 100_000;

    // Enregistre un nouveau PIN : un sel aléatoire, puis le hash du PIN avec ce sel
    public void DefinirPin(string pin)
    {
        byte[] sel = RandomNumberGenerator.GetBytes(16);
        PinSel = Convert.ToBase64String(sel);
        PinHash = Convert.ToBase64String(Calculer(pin, sel));
    }

    // Vérifie un PIN saisi : on refait le même calcul et on compare
    public bool VerifierPin(string pin)
    {
        if (string.IsNullOrEmpty(PinHash) || string.IsNullOrEmpty(PinSel))
            return false;

        try
        {
            byte[] attendu = Convert.FromBase64String(PinHash);
            byte[] calcule = Calculer(pin ?? "", Convert.FromBase64String(PinSel));

            // Comparaison à temps constant (évite de révéler le PIN par le temps de calcul)
            return CryptographicOperations.FixedTimeEquals(attendu, calcule);
        }
        catch (FormatException)
        {
            return false;   // fichier abîmé
        }
    }

    // "Français (fr)" -> "fr" (utile pour la traduction IA)
    public string ObtenirCodeLangue()
    {
        int debut = Langue.IndexOf('(');
        int fin = Langue.IndexOf(')');
        return debut >= 0 && fin > debut
            ? Langue.Substring(debut + 1, fin - debut - 1)
            : "fr";
    }

    private static byte[] Calculer(string pin, byte[] sel)
        => Rfc2898DeriveBytes.Pbkdf2(pin, sel, Iterations, HashAlgorithmName.SHA256, 32);
}

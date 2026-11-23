namespace TALKORA.Core;

public class Contact
{
    public string Nom { get; set; } = "";
    public string AdresseIp { get; set; } = "";
    public int Port { get; set; } = 5000;
    public string LanguePreferee { get; set; } = "fr";
    public bool EstBloque { get; set; } = false;

    // Notifications coupées pour ce contact (pas de son quand il écrit)
    public bool Muet { get; set; } = false;

    // Mode En ligne (étape 23) : l'IA répond automatiquement à ce contact (toujours signalé 🤖). Réglage PERSONNEL, jamais envoyé.
    public bool ReponseIA { get; set; } = false;

    // Organisation de la liste (étape 14) : réglages PERSONNELS, jamais envoyés aux autres
    public bool Epingle { get; set; } = false;
    public bool Archive { get; set; } = false;

    public override string ToString() => $"{Nom} ({AdresseIp}:{Port})";

    /// <summary>Nom saisi pour renommer un contact : 1 à 40 caractères, sans retour à la ligne. null si invalide.</summary>
    public static string? NettoyerNom(string? saisie)
    {
        string nom = (saisie ?? "").Trim();
        if (nom.Length == 0 || nom.Length > 40 || nom.Any(char.IsControl))
            return null;
        return nom;
    }
}

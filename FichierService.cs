namespace TALKORA.Core;

public enum CategorieFichier { Photo, Video, Audio, Document }

/// <summary>
/// Range les fichiers envoyés et reçus dans DataTalkora/Fichiers,
/// et fournit des outils (type de fichier, taille lisible, nom sécurisé).
/// Chaque fichier est stocké sous le nom "IdDuMessage.extension" : un nom envoyé par
/// quelqu'un d'autre ne peut donc jamais écraser un fichier de l'ordinateur.
/// </summary>
public class FichierService
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm" };
    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".wma" };

    // Fichiers qui peuvent lancer un programme : on demande confirmation avant de les ouvrir
    private static readonly HashSet<string> Risques = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".msi", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".scr",
          ".com", ".lnk", ".jar", ".reg", ".dll", ".hta", ".cpl", ".pif" };

    public string Dossier { get; }

    public FichierService(string? dossier = null)
        => Dossier = Path.GetFullPath(dossier ?? Path.Combine("DataTalkora", "Fichiers"));

    /// <summary>Chemin où ranger le fichier d'un message (crée le dossier si besoin).</summary>
    public string CheminPour(Guid id, string nomFichier)
    {
        Directory.CreateDirectory(Dossier);
        return Path.Combine(Dossier, id.ToString("N") + Path.GetExtension(NomSecurise(nomFichier)));
    }

    /// <summary>Copie un fichier dans le dossier de Talkora et renvoie son nouveau chemin.</summary>
    public string Importer(string source, Guid id, string nomFichier)
    {
        string destination = CheminPour(id, nomFichier);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    /// <summary>Supprime un fichier, mais seulement s'il est bien dans le dossier de Talkora.</summary>
    public bool Supprimer(string chemin)
    {
        try
        {
            string complet = Path.GetFullPath(chemin);
            string racine = Dossier.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!complet.StartsWith(racine, StringComparison.OrdinalIgnoreCase) || !File.Exists(complet))
                return false;

            File.Delete(complet);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // ===================== OUTILS =====================

    /// <summary>Nettoie un nom de fichier reçu : plus de chemin, plus de caractères interdits.</summary>
    public static string NomSecurise(string? nomBrut)
    {
        string nom = Path.GetFileName((nomBrut ?? "").Replace('\\', '/'));

        foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }))
            nom = nom.Replace(c, '_');

        nom = nom.Trim().TrimEnd('.', ' ');

        if (nom.Length > 100)
        {
            string extension = Path.GetExtension(nom);
            if (extension.Length > 20) extension = "";
            nom = nom[..(100 - extension.Length)] + extension;
        }

        return nom.Length == 0 ? "fichier" : nom;
    }

    public static CategorieFichier Categorie(string nomFichier)
    {
        string extension = Path.GetExtension(nomFichier);
        if (Images.Contains(extension)) return CategorieFichier.Photo;
        if (Videos.Contains(extension)) return CategorieFichier.Video;
        if (Audios.Contains(extension)) return CategorieFichier.Audio;
        return CategorieFichier.Document;
    }

    public static bool EstRisque(string nomFichier) => Risques.Contains(Path.GetExtension(nomFichier));

    public static string FormaterTaille(long octets)
    {
        if (octets < 1024) return $"{octets} o";
        if (octets < 1024 * 1024) return $"{octets / 1024.0:0.#} Ko";
        if (octets < 1024L * 1024 * 1024) return $"{octets / (1024.0 * 1024):0.#} Mo";
        return $"{octets / (1024.0 * 1024 * 1024):0.##} Go";
    }
}
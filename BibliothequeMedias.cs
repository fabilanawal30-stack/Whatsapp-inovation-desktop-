namespace TALKORA.Core;

public enum TypeMedia { Sticker, Gif }


public class BibliothequeMedias
{
    public const long TailleMaxSticker = 2L * 1024 * 1024;
    public const long TailleMaxGif = 8L * 1024 * 1024;

    private static readonly string[] ExtensionsSticker = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private static readonly string[] ExtensionsGif = { ".gif" };

    private readonly string _racine;

    public BibliothequeMedias(string dossier = "DataTalkora")
    {
        _racine = dossier;
        Directory.CreateDirectory(Dossier(TypeMedia.Sticker));
        Directory.CreateDirectory(Dossier(TypeMedia.Gif));
    }

    public string Dossier(TypeMedia type) => Path.Combine(_racine, type == TypeMedia.Sticker ? "Stickers" : "Gifs");

    private static string[] Extensions(TypeMedia type) => type == TypeMedia.Sticker ? ExtensionsSticker : ExtensionsGif;
    private static long TailleMax(TypeMedia type) => type == TypeMedia.Sticker ? TailleMaxSticker : TailleMaxGif;

    // Les fichiers du dossier, les plus récents d'abord
    public List<string> Lister(TypeMedia type)
    {
        try
        {
            return new DirectoryInfo(Dossier(type)).EnumerateFiles()
                .Where(f => Extensions(type).Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>Copie une image dans la bibliothèque. Renvoie le nouveau chemin, ou lève InvalidDataException (message en français).</summary>
    public string Importer(string source, TypeMedia type)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (!Extensions(type).Contains(ext))
            throw new InvalidDataException(type == TypeMedia.Gif
                ? "Seuls les fichiers .gif sont acceptés."
                : "Formats acceptés : PNG, JPG, BMP ou GIF.");

        var info = new FileInfo(source);
        if (!info.Exists)
            throw new InvalidDataException("Fichier introuvable.");
        if (info.Length > TailleMax(type))
            throw new InvalidDataException($"Fichier trop lourd (maximum {FichierService.FormaterTaille(TailleMax(type))}).");

        string base_ = FichierService.NomSecurise(Path.GetFileNameWithoutExtension(source));
        if (base_.Length > 40) base_ = base_[..40];
        if (base_.Length == 0) base_ = "image";

        // Un identifiant court évite d'écraser un fichier de même nom
        string destination = Path.Combine(Dossier(type), $"{base_}-{Guid.NewGuid():N}"[..(base_.Length + 7)] + ext);
        File.Copy(source, destination, overwrite: false);
        return destination;
    }

    /// <summary>Supprime un fichier de la bibliothèque (refuse tout fichier situé en dehors du dossier).</summary>
    public bool Supprimer(string chemin, TypeMedia type)
    {
        try
        {
            string dossier = Path.GetFullPath(Dossier(type)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string complet = Path.GetFullPath(chemin);
            if (!complet.StartsWith(dossier, StringComparison.OrdinalIgnoreCase) || !File.Exists(complet))
                return false;
            File.Delete(complet);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}

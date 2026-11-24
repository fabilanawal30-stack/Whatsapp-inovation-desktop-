namespace TALKORA.Core;

/// <summary>
/// Une image JPEG est coupée en morceaux (UDP) puis recollée ici. Si un morceau se perd,
/// l'image est simplement abandonnée : on préfère sauter une image que figer la vidéo.
/// </summary>
public class ReassembleurVideo
{
    public const int FragmentsMax = 512;        // ≈ 560 Ko par image au maximum
    private const int ImagesEnCoursMax = 4;

    private sealed class ImageEnCours
    {
        public byte[]?[] Parties = Array.Empty<byte[]?>();
        public int Recus;
    }

    private readonly Dictionary<uint, ImageEnCours> _enCours = new();
    private uint _derniereAffichee;
    private bool _aDejaAffiche;

    /// <summary>Ajoute un morceau. Renvoie l'image complète dès que tous ses morceaux sont là.</summary>
    public byte[]? Ajouter(uint idImage, int fragment, int nombreFragments, byte[] donnees)
    {
        if (nombreFragments < 1 || nombreFragments > FragmentsMax
            || fragment < 0 || fragment >= nombreFragments
            || donnees.Length > PaquetMedia.DonneesMax)
            return null;

        // Image plus ancienne (ou égale) à celle déjà affichée : trop tard
        if (_aDejaAffiche && (int)(idImage - _derniereAffichee) <= 0)
            return null;

        if (!_enCours.TryGetValue(idImage, out var image))
        {
            // Trop d'images en attente : on abandonne la plus ancienne
            if (_enCours.Count >= ImagesEnCoursMax)
                _enCours.Remove(_enCours.Keys.OrderBy(k => (int)(k - idImage)).First());

            image = new ImageEnCours { Parties = new byte[]?[nombreFragments] };
            _enCours[idImage] = image;
        }

        if (image.Parties.Length != nombreFragments || image.Parties[fragment] is not null)
            return null;                       // incohérent, ou doublon

        image.Parties[fragment] = donnees;
        image.Recus++;
        if (image.Recus < nombreFragments)
            return null;

        // Complète : on recolle, et on oublie les images plus anciennes
        int total = image.Parties.Sum(p => p!.Length);
        var resultat = new byte[total];
        int position = 0;
        foreach (var partie in image.Parties)
        {
            partie!.CopyTo(resultat, position);
            position += partie.Length;
        }

        _enCours.Remove(idImage);
        foreach (var ancien in _enCours.Keys.Where(k => (int)(k - idImage) < 0).ToList())
            _enCours.Remove(ancien);

        _derniereAffichee = idImage;
        _aDejaAffiche = true;
        return resultat;
    }

    /// <summary>Coupe une image en morceaux de DonneesMax octets. Renvoie null si elle est trop grosse.</summary>
    public static List<byte[]>? Fragmenter(byte[] image)
    {
        int nombre = (image.Length + PaquetMedia.DonneesMax - 1) / PaquetMedia.DonneesMax;
        if (image.Length == 0 || nombre > FragmentsMax)
            return null;

        var morceaux = new List<byte[]>(nombre);
        for (int i = 0; i < nombre; i++)
        {
            int debut = i * PaquetMedia.DonneesMax;
            int longueur = Math.Min(PaquetMedia.DonneesMax, image.Length - debut);
            morceaux.Add(image.AsSpan(debut, longueur).ToArray());
        }
        return morceaux;
    }
}

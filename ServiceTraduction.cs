using System.Collections.Concurrent;

namespace TALKORA.Core;

public record ResultatTraduction(string Texte, string Source, string Cible, string Moteur, bool Identique);

/// <summary>
/// Le chef d'orchestre : choisit la langue de départ, vérifie le cache, essaie les moteurs
/// dans l'ordre (hors ligne d'abord, en ligne ensuite SI l'utilisateur l'a autorisé).
/// </summary>
public class ServiceTraduction
{
    public const int LongueurMax = 2000;
    private const int CacheMax = 500;

    private readonly List<ITraducteur> _moteurs;
    private readonly ConcurrentDictionary<string, ResultatTraduction> _cache = new();

    public ServiceTraduction(IEnumerable<ITraducteur> moteurs) => _moteurs = moteurs.ToList();

    public bool ADesMoteursHorsLigne => _moteurs.Any(m => m.EstHorsLigne);

    // sourceAnnoncee : la langue que l'expéditeur dit utiliser (peut être fausse, on la vérifie)
    public async Task<ResultatTraduction> TraduireAsync(string texte, string sourceAnnoncee, string cible,
                                                        bool autoriserEnLigne, CancellationToken ct = default)
    {
        texte = (texte ?? "").Trim();
        cible = Langues.Normaliser(cible);
        if (cible.Length == 0)
            throw new TraductionException("Langue de destination inconnue.");
        if (texte.Length == 0)
            throw new TraductionException("Il n'y a rien à traduire.");
        if (texte.Length > LongueurMax)
            throw new TraductionException($"Message trop long pour la traduction (maximum {LongueurMax} caractères).");

        // La détection par mots a priorité ; sinon on croit la langue annoncée
        string source = DetecteurLangue.Detecter(texte);
        if (source.Length == 0)
            source = Langues.Normaliser(sourceAnnoncee);

        if (source == cible)
            return new ResultatTraduction(texte, source, cible, "", true);

        string cle = $"{source}>{cible}:{texte}";
        if (_cache.TryGetValue(cle, out var deja))
            return deja;

        string? derniereErreur = null;
        bool enLigneRefuse = false;

        foreach (var moteur in _moteurs)
        {
            if (!moteur.EstHorsLigne && !autoriserEnLigne)
            {
                enLigneRefuse = true;
                continue;
            }

            try
            {
                string traduit = await moteur.TraduireAsync(texte, source, cible, ct).ConfigureAwait(false);
                var resultat = new ResultatTraduction(traduit, source, cible, moteur.Nom, false);

                if (_cache.Count >= CacheMax) _cache.Clear();
                _cache[cle] = resultat;
                return resultat;
            }
            catch (TraductionException ex)
            {
                derniereErreur = ex.Message;   // on essaie le moteur suivant
            }
        }

        if (derniereErreur is not null)
            throw new TraductionException(derniereErreur);
        if (enLigneRefuse)
            throw new TraductionException("Aucun moteur hors ligne n'est installé. Autorisez la traduction en ligne dans les réglages 🌐.");
        throw new TraductionException("Aucun moteur de traduction disponible.");
    }
}

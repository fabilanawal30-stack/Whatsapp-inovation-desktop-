namespace TALKORA.Core;

/// <summary>
/// Traduction HORS LIGNE avec le modèle local de Tali : le texte ne quitte jamais l'ordinateur.
/// Cette classe ne connaît pas LLamaSharp : on lui donne simplement une fonction
/// "prompt -> réponse" (voir LISEZ-MOI-ETAPE7.txt pour la brancher sur Tali).
/// </summary>
public class TraducteurTali : ITraducteur
{
    private readonly Func<string, CancellationToken, Task<string>> _inference;
    private readonly Func<bool> _disponible;

    public TraducteurTali(Func<string, CancellationToken, Task<string>> inference, Func<bool>? disponible = null)
    {
        _inference = inference;
        _disponible = disponible ?? (() => true);
    }

    public string Nom => "Tali (hors ligne)";
    public bool EstHorsLigne => true;

    public static string ConstruirePrompt(string texte, string source, string cible)
    {
        string de = string.IsNullOrEmpty(source) ? "la langue détectée" : Langues.NomDe(source);
        return $"""
            Tu es un traducteur. Traduis le texte ci-dessous de {de} vers {Langues.NomDe(cible)}.
            Conserve le ton, les emojis et la mise en forme. N'ajoute aucune explication.
            Sortie UNIQUEMENT la traduction.

            Texte:
            {texte}

            Traduction:
            """;
    }

    public async Task<string> TraduireAsync(string texte, string source, string cible, CancellationToken ct = default)
    {
        if (!_disponible())
            throw new TraductionException("Le modèle hors ligne de Tali n'est pas installé.");

        string brut;
        try
        {
            brut = await _inference(ConstruirePrompt(texte, source, cible), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new TraductionException("Tali n'a pas réussi à traduire : " + ex.Message, ex);
        }

        string propre = Nettoyer(brut);
        if (propre.Length == 0)
            throw new TraductionException("Tali a renvoyé une réponse vide.");
        return propre;
    }

    // Les petits modèles ajoutent parfois "Traduction :" ou des guillemets : on les retire
    public static string Nettoyer(string? brut)
    {
        string t = (brut ?? "").Trim();

        foreach (string prefixe in new[] { "Traduction:", "Traduction :", "Translation:", "Translation :" })
            if (t.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase))
                t = t.Substring(prefixe.Length).Trim();

        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '«' && t[^1] == '»')))
            t = t.Substring(1, t.Length - 2).Trim();

        return t;
    }
}

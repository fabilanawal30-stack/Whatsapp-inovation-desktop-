namespace TALKORA.Core;

/// <summary>
/// Devine la langue d'un texte en comptant des mots très courants.
/// Volontairement simple : pour un message trop court ou ambigu, on répond "" (inconnu)
/// et l'appelant se rabat sur la langue annoncée par l'expéditeur.
/// </summary>
public static class DetecteurLangue
{
    private static readonly Dictionary<string, HashSet<string>> Mots = new()
    {
        ["fr"] = Liste("le la les un une des du de et est sont je tu il elle nous vous ils pas pour dans que qui avec sur ce cette mais ou où bonjour merci salut oui non comment très bien ça suis ai"),
        ["en"] = Liste("the a an and is are was were i you he she we they not for in that who with on this but or hello thanks hi yes no how very good it am have what your my"),
        ["es"] = Liste("el la los las un una unos y es son yo tú usted nosotros no para en que quien con sobre este esta pero o hola gracias sí cómo muy bien estoy tengo mi"),
        ["pt"] = Liste("o a os as um uma e é são eu tu você nós não para em que quem com sobre este esta mas ou olá obrigado obrigada sim como muito bem estou tenho meu"),
        ["de"] = Liste("der die das ein eine und ist sind ich du er sie wir ihr nicht für in dass wer mit auf dieser aber oder hallo danke ja nein wie sehr gut bin habe mein"),
        ["it"] = Liste("il lo la gli le un una e è sono io tu lui lei noi voi non per in che chi con su questo ma o ciao grazie sì come molto bene sto ho mio"),
    };

    private static HashSet<string> Liste(string mots)
        => new(mots.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    public static string Detecter(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return "";

        // Les alphabets non latins se reconnaissent directement
        foreach (char c in texte)
        {
            if (c >= '؀' && c <= 'ۿ') return "ar";
            if (c >= '一' && c <= '鿿') return "zh";
            if (c >= 'Ѐ' && c <= 'ӿ') return "ru";
        }

        var mots = new List<string>();
        var courant = new System.Text.StringBuilder();
        foreach (char c in texte.ToLowerInvariant())
        {
            if (char.IsLetter(c)) courant.Append(c);
            else if (courant.Length > 0) { mots.Add(courant.ToString()); courant.Clear(); }
        }
        if (courant.Length > 0) mots.Add(courant.ToString());

        string meilleure = "";
        int scoreMax = 0, second = 0;
        foreach (var (code, ensemble) in Mots)
        {
            int score = mots.Count(m => ensemble.Contains(m));
            if (score > scoreMax) { second = scoreMax; scoreMax = score; meilleure = code; }
            else if (score > second) second = score;
        }

        // Il faut au moins 2 mots reconnus et une vraie avance sur la deuxième langue
        return scoreMax >= 2 && scoreMax > second ? meilleure : "";
    }
}

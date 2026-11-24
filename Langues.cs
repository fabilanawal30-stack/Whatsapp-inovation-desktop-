namespace TALKORA.Core;

/// <summary>
/// La liste des langues que Talkora sait traduire, et quelques outils pour
/// transformer "Français (fr)", "fr-FR" ou "EN" en un code propre : "fr", "en".
/// </summary>
public static class Langues
{
    public record Langue(string Code, string Nom);

    public static readonly IReadOnlyList<Langue> Toutes = new List<Langue>
    {
        new("fr", "Français"),
        new("en", "English"),
        new("es", "Español"),
        new("pt", "Português"),
        new("de", "Deutsch"),
        new("it", "Italiano"),
        new("ar", "العربية"),
        new("zh", "中文"),
        new("ru", "Русский"),
        new("tr", "Türkçe"),
    };

    // "Français (fr)" -> "fr" ; "en-US" -> "en" ; "xx" (inconnu) -> ""
    public static string Normaliser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return "";

        string t = texte.Trim();
        int debut = t.IndexOf('(');
        int fin = t.IndexOf(')');
        if (debut >= 0 && fin > debut)
            t = t.Substring(debut + 1, fin - debut - 1);

        int tiret = t.IndexOfAny(new[] { '-', '_' });
        if (tiret > 0)
            t = t.Substring(0, tiret);

        t = t.Trim().ToLowerInvariant();
        return Toutes.Any(l => l.Code == t) ? t : "";
    }

    public static string NomDe(string code)
        => Toutes.FirstOrDefault(l => l.Code == Normaliser(code))?.Nom ?? code;
}

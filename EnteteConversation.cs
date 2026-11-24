using System.Text.RegularExpressions;

namespace TALKORA.Core;

public enum EtatEntete { Aucun, EnLigne, HorsLigne, Ecrit, Attention }


public static class EnteteConversation
{
    private static readonly Regex Adresse = new(@"^\s*\d{1,3}(\.\d{1,3}){3}(:\d+)?\s*$", RegexOptions.Compiled);

    public static (string Nom, string Statut, EtatEntete Etat) Analyser(string? texte)
    {
        texte = (texte ?? "").Trim();
        if (texte.Length == 0) return ("", "", EtatEntete.Aucun);

        // 1) « Nom   statut » (trois espaces) : le statut est tout ce qui suit
        string nom = texte, reste = "";
        int i3 = texte.IndexOf("   ", StringComparison.Ordinal);
        if (i3 >= 0) { nom = texte[..i3].TrimEnd(); reste = texte[(i3 + 3)..].Trim(); }

        // 2) « Nom  ·  adresse » : on retire l'adresse IP (inutile à l'écran), on garde les autres informations (groupes : « 5 membres »)
        string details = "";
        int ip = nom.IndexOf("  ·  ", StringComparison.Ordinal);
        if (ip >= 0)
        {
            details = nom[(ip + 5)..].Trim();
            nom = nom[..ip].TrimEnd();
            if (Adresse.IsMatch(details)) details = "";
        }

        if (reste.Contains("Talkora actif")) return (nom, "en ligne", EtatEntete.EnLigne);
        if (reste.Contains("pas de Talkora") || reste.Contains("injoignable")) return (nom, "hors ligne", EtatEntete.HorsLigne);
        if (reste.Contains("écrit")) return (nom, "écrit…", EtatEntete.Ecrit);
        if (reste.Contains('⏳') && reste.Length <= 3) return (nom, details, EtatEntete.Aucun);
        if (reste.Contains('⚠')) return (nom, reste, EtatEntete.Attention);
        if (reste.Length > 0) return (nom, reste, EtatEntete.Aucun);
        return (nom, details, EtatEntete.Aucun);
    }
}

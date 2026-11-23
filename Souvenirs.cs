namespace TALKORA.Core;

public record Souvenir(MessageChat Message, string Cle, int AnsAvant);
public record AnniversaireDiscussion(string Cle, DateTime Debut, int Ans);

/// <summary>
/// Talkora Memories : ce qui s'est dit « un 19 novembre » les années précédentes, les anniversaires de discussion
/// (« il y a 2 ans, vous avez commencé à parler avec Awa ») et les messages que l'on a gardés en souvenir (⭐).
/// Tout se calcule sur l'ordinateur à partir de la base de messages.
/// </summary>
public static class ServiceSouvenirs
{
    public const int LongueurMin = 15;            // un « ok » n'est pas un souvenir
    public const int ParAnneeEtConversation = 2;  // au plus 2 souvenirs par année et par conversation
    public const int MaxTotal = 30;

    private static bool EstAnniversaire(DateTime d, DateTime now)
    {
        if (d.Month == now.Month && d.Day == now.Day) return true;
        // né un 29 février : on le fête le 28 février des années non bissextiles
        return d.Month == 2 && d.Day == 29 && now.Month == 2 && now.Day == 28 && !DateTime.IsLeapYear(now.Year);
    }

    private static bool Memorable(MessageChat m, DateTime now)
        => !m.EstAction && !m.Supprime && !m.EstScellee(now) && !m.EstGroupe
           && ((m.Type == TypeMessage.Texte && (m.Contenu ?? "").Trim().Length >= LongueurMin)
               || (m.Type == TypeMessage.Fichier && !m.EstSticker && OutilsMemoire.EstImage(m.NomFichier)));

    /// <summary>Les messages écrits ce même jour (mois + jour) lors d'années précédentes, du plus récent au plus ancien.</summary>
    public static List<Souvenir> SurCeJour(IEnumerable<MessageChat> messages, DateTime now, Func<MessageChat, string> cle, int max = MaxTotal)
    {
        var res = new List<Souvenir>();
        var groupes = messages
            .Where(m => m.DateEnvoi.Year < now.Year && EstAnniversaire(m.DateEnvoi, now) && Memorable(m, now))
            .GroupBy(m => (m.DateEnvoi.Year, Cle: cle(m)));
        foreach (var g in groupes.OrderByDescending(g => g.Key.Year))
            foreach (var m in g.OrderByDescending(x => (x.Contenu ?? "").Length).ThenBy(x => x.DateEnvoi).Take(ParAnneeEtConversation).OrderBy(x => x.DateEnvoi))
                res.Add(new Souvenir(m, g.Key.Cle, now.Year - m.DateEnvoi.Year));
        return res.Take(max).ToList();
    }

    /// <summary>Conversations dont le tout premier message date d'un « jour pour jour » (1 an, 2 ans…).</summary>
    public static List<AnniversaireDiscussion> Anniversaires(IEnumerable<MessageChat> messages, DateTime now, Func<MessageChat, string> cle)
    {
        var res = new List<AnniversaireDiscussion>();
        var premiers = messages.Where(m => !m.EstAction && !m.Supprime && !m.EstGroupe)
                               .GroupBy(cle).Select(g => (Cle: g.Key, Debut: g.Min(m => m.DateEnvoi)));
        foreach (var (k, debut) in premiers)
            if (debut.Year < now.Year && EstAnniversaire(debut, now))
                res.Add(new AnniversaireDiscussion(k, debut, now.Year - debut.Year));
        return res.OrderByDescending(a => a.Ans).ToList();
    }

    public static string Ans(int n) => n <= 1 ? "il y a 1 an" : $"il y a {n} ans";

    public static string Resume(IReadOnlyList<Souvenir> souvenirs, IReadOnlyList<AnniversaireDiscussion> anniv)
    {
        var parts = new List<string>();
        if (souvenirs.Count > 0)
        {
            var ans = souvenirs.Select(s => s.AnsAvant).Distinct().OrderBy(x => x).Select(Ans);
            parts.Add($"{souvenirs.Count} message(s) d'{string.Join(", ", ans)}");
        }
        if (anniv.Count > 0) parts.Add($"{anniv.Count} anniversaire(s) de discussion");
        return parts.Count == 0 ? "" : "📸 Souvenirs du jour : " + string.Join(" · ", parts);
    }

    // ===== le rappel « Souvenirs du jour » dans la liste des suivis : un par jour =====

    /// <summary>Identifiant fixe pour une date : le même jour, on ne recrée pas le rappel.</summary>
    public static Guid IdDuJour(DateTime now)
        => new((int)now.Year, (short)now.Month, (short)now.Day, 0x53, 0x4F, 0x55, 0x56, 0x45, 0x4E, 0x49, 0x52);

    public static bool EstSuiviSouvenir(Suivi s) => s.Type == TypeSuivi.Manuel && s.Texte.StartsWith("📸");

    public static Suivi? SuiviDuJour(IReadOnlyList<Souvenir> souvenirs, IReadOnlyList<AnniversaireDiscussion> anniv, DateTime now)
    {
        string t = Resume(souvenirs, anniv);
        if (t.Length == 0) return null;
        string cle = souvenirs.Count > 0 ? souvenirs[0].Cle : anniv[0].Cle;
        return new Suivi { MessageId = IdDuJour(now), Cle = cle, Type = TypeSuivi.Manuel, Texte = t, Echeance = now.Date, Cree = now };
    }
}

static class OutilsMemoire
{
    public static bool EstImage(string? nom)
    {
        string e = Path.GetExtension(nom ?? "").ToLowerInvariant();
        return e is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp";
    }
}

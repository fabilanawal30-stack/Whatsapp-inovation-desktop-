using System.Text.RegularExpressions;

namespace TALKORA.Core;

public enum TypeSuivi
{
    ARepondre,     // un contact m'a posé une question et je n'ai pas répondu
    Promesse,      // j'ai écrit « je te rappelle demain » : à tenir
    SansReponse,   // j'ai posé une question et personne n'a répondu
    Manuel         // « Me le rappeler » sur un message
}

public enum EtatSuivi { Actif, Fait, Ignore }

/// <summary>Une chose à ne pas oublier, liée à une conversation (et à un message).</summary>
public class Suivi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public string Cle { get; set; } = "";            // conversation : IP du contact ou Id du groupe
    public TypeSuivi Type { get; set; }
    public string Texte { get; set; } = "";          // extrait du message
    public DateTime Echeance { get; set; }
    public EtatSuivi Etat { get; set; } = EtatSuivi.Actif;
    public DateTime Cree { get; set; } = DateTime.Now;

    public bool EstDu(DateTime maintenant) => Etat == EtatSuivi.Actif && Echeance <= maintenant;

    public string Libelle => Type switch
    {
        TypeSuivi.ARepondre => "À répondre",
        TypeSuivi.Promesse => "Promesse à tenir",
        TypeSuivi.SansReponse => "Sans réponse",
        _ => "Rappel"
    };
}

/// <summary>
/// Talkora Follow-up : repère ce qu'il ne faut pas oublier dans les conversations.
/// Aucune IA nécessaire : des règles lisibles (questions, « je te rappelle demain »…), en français et en anglais.
/// </summary>
public static class ServiceSuivi
{
    public const int JoursAnalyses = 3;     // on n'analyse que les messages récents (pas tout l'historique)

    // ===================== DÉTECTION =====================

    private static string Normal(string? t)
        => Regex.Replace(Organisation.Normaliser(t), @"[’'`\-–]", " ");

    private static readonly Regex Promesse = new(
        @"\bje (?:te|t|vous) (?:rappelle|rappellerai|appelle|appellerai|telephone|envoie|enverrai|ecris|ecrirai|redis|dirai|tiens au courant|fais signe|passe|donne|montre|ramene)\b"
      + @"|\bje (?:reviens|reviendrai) vers (?:toi|vous)\b"
      + @"|\bje (?:l|le|la|les) (?:envoie|enverrai|fais|ferai|regarde|regarderai|verifie|verifierai)\b"
      + @"|\bje vais (?:te |t |vous )?(?:envoyer|appeler|rappeler|ecrire|verifier|regarder|faire|passer|preparer)\b"
      + @"|\bje (?:m en occupe|regarde ca|verifie ca|vois ca)\b"
      + @"|\bi (?:ll|will) (?:call|send|text|check|get back|let you know|message|email)\b"
      + @"|\blet me (?:check|look|get back)\b",
        RegexOptions.Compiled);

    private static readonly Regex Interrogatif = new(
        @"^(?:est ce que|est ce qu|peux tu|peut on|pouvez vous|tu peux|vous pouvez|tu as|as tu|avez vous|vous avez|quand|ou |comment|pourquoi|combien|quel|quelle|qui |que |qu est|can you|could you|would you|do you|did you|are you|is there|when|where|how|why|what|who|will you)",
        RegexOptions.Compiled);

    private static readonly Regex Demande = new(@"\b(?:urgent|svp|stp|s il te plait|s il vous plait|please|asap|rappelle moi|call me)\b", RegexOptions.Compiled);

    public static bool EstPromesse(string? texte) => Promesse.IsMatch(Normal(texte));

    /// <summary>Une question, ou une demande (« stp », « urgent »…).</summary>
    public static bool EstQuestion(string? texte)
    {
        string n = Normal(texte);
        if (n.Length == 0) return false;
        return (texte ?? "").Contains('?') || Interrogatif.IsMatch(n) || Demande.IsMatch(n);
    }

    private static readonly string[] JoursFr = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };
    private static readonly string[] JoursEn = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    /// <summary>
    /// Cherche une échéance dans un texte : « demain », « ce soir », « dans 2 heures », « lundi à 15h »…
    /// Renvoie null si le texte n'en contient pas.
    /// </summary>
    public static DateTime? ExtraireEcheance(string? texte, DateTime maintenant)
    {
        string n = Normal(texte);
        if (n.Length == 0) return null;

        // « dans 2 heures », « dans une demi heure »
        if (Regex.IsMatch(n, @"\bdans (?:une )?demi heure\b")) return maintenant.AddMinutes(30);
        var rel = Regex.Match(n, @"\b(?:dans|in) (\d{1,6}|un|une|deux|trois|a|an) ?(minutes?|mins?|heures?|hours?|h|jours?|days?|semaines?|weeks?)\b");
        if (rel.Success)
        {
            int nb = rel.Groups[1].Value switch { "un" or "une" or "a" or "an" => 1, "deux" => 2, "trois" => 3, var v => int.Parse(v) };
            string u = rel.Groups[2].Value;
            nb = Math.Clamp(nb, 1, 365);
            if (u.StartsWith("min")) return maintenant.AddMinutes(nb);
            if (u.StartsWith("h")) return maintenant.AddHours(nb);
            if (u.StartsWith("j") || u.StartsWith("d")) return maintenant.AddDays(nb);
            return maintenant.AddDays(7 * nb);
        }
        if (Regex.IsMatch(n, @"\b(?:tout a l heure|bientot|plus tard|dans quelques|later|soon|in a bit)\b")) return maintenant.AddHours(2);

        // heure explicite : « à 15h », « à 9h30 », « à 14:00 »
        int? heure = null, minute = 0;
        var h = Regex.Match(n, @"\b(?:a|at|vers) (\d{1,2}) ?(?:h|:) ?(\d{2})?\b");
        if (h.Success && int.Parse(h.Groups[1].Value) is >= 0 and <= 23)
        {
            heure = int.Parse(h.Groups[1].Value);
            minute = h.Groups[2].Success ? Math.Min(59, int.Parse(h.Groups[2].Value)) : 0;
        }

        // moment de la journée
        int? defaut = null;
        if (Regex.IsMatch(n, @"\b(?:matin|morning)\b")) defaut = 9;
        else if (Regex.IsMatch(n, @"\b(?:apres midi|aprem|afternoon)\b")) defaut = 14;
        else if (Regex.IsMatch(n, @"\b(?:soir|soiree|evening|tonight)\b")) defaut = 19;

        // le jour
        DateTime? jour = null;
        bool aujourdhui = false;
        if (Regex.IsMatch(n, @"\bapres demain\b")) jour = maintenant.Date.AddDays(2);
        else if (Regex.IsMatch(n, @"\b(?:demain|tomorrow)\b")) jour = maintenant.Date.AddDays(1);
        else if (Regex.IsMatch(n, @"\b(?:ce soir|cet apres midi|cet aprem|ce matin|aujourd hui|today|tonight|this evening|this afternoon|this morning)\b"))
        { jour = maintenant.Date; aujourdhui = true; }
        else if (Regex.IsMatch(n, @"\b(?:semaine prochaine|next week)\b")) jour = ProchainJour(maintenant, DayOfWeek.Monday);
        else if (Regex.IsMatch(n, @"\b(?:ce week end|ce weekend|week end|weekend|this weekend)\b")) jour = ProchainJour(maintenant, DayOfWeek.Saturday);
        else
        {
            for (int i = 0; i < 7 && jour is null; i++)
                if (Regex.IsMatch(n, $@"\b(?:{JoursFr[i]}|{JoursEn[i]})\b"))
                    jour = ProchainJour(maintenant, (DayOfWeek)i);
        }

        if (jour is null && heure is null) return null;

        int hh = heure ?? defaut ?? (jour == maintenant.Date.AddDays(1) || jour > maintenant.Date ? 9 : 10);
        if (jour is null)    // « à 15h » tout seul : aujourd'hui si c'est à venir, sinon demain
        {
            var auj = maintenant.Date.AddHours(hh).AddMinutes(minute ?? 0);
            return auj > maintenant ? auj : auj.AddDays(1);
        }

        var dt = jour.Value.AddHours(hh).AddMinutes(minute ?? 0);
        if (dt <= maintenant && (aujourdhui || jour == maintenant.Date))
            return heure is not null ? dt.AddDays(1) : maintenant.AddHours(1);   // « ce soir » dit à 20 h : dans 1 h
        return dt;
    }

    // Le prochain jour de la semaine donné, STRICTEMENT après aujourd'hui
    private static DateTime ProchainJour(DateTime maintenant, DayOfWeek jour)
    {
        int d = ((int)jour - (int)maintenant.DayOfWeek + 7) % 7;
        return maintenant.Date.AddDays(d == 0 ? 7 : d);
    }

    // ===================== ANALYSE DES CONVERSATIONS =====================

    /// <summary>
    /// Nouveaux suivis trouvés dans les conversations. « deja » = les (message, type) déjà connus : on ne recrée jamais un suivi,
    /// même fait ou ignoré. Messages de groupe ignorés (trop de bruit).
    /// </summary>
    public static List<Suivi> Analyser(IEnumerable<MessageChat> messagesEnOrdre, Func<MessageChat, bool> estMoi,
        Func<MessageChat, string> cle, DateTime maintenant, int heuresARepondre, int heuresSansReponse,
        ISet<(Guid, TypeSuivi)> deja)
    {
        var res = new List<Suivi>();
        var parConv = new Dictionary<string, List<MessageChat>>();
        foreach (var m in messagesEnOrdre)
        {
            if (m.EstAction || m.Supprime || m.EstGroupe || m.Type != TypeMessage.Texte || m.EstScellee(maintenant)) continue;
            string k = cle(m);
            if (!parConv.TryGetValue(k, out var l)) parConv[k] = l = new();
            l.Add(m);
        }

        DateTime limite = maintenant.AddDays(-JoursAnalyses);

        foreach (var (k, liste) in parConv)
        {
            var dernier = liste[^1];

            // 1. Il m'a posé une question, je n'ai rien répondu depuis
            if (!estMoi(dernier) && dernier.DateEnvoi >= limite && EstQuestion(dernier.Contenu)
                && !deja.Contains((dernier.Id, TypeSuivi.ARepondre)))
            {
                var du = dernier.DateEnvoi.AddHours(Math.Max(1, heuresARepondre));
                if (du <= maintenant)
                    res.Add(Nouveau(dernier, k, TypeSuivi.ARepondre, du));
            }

            // 2. J'ai posé une question, silence depuis
            if (estMoi(dernier) && dernier.DateEnvoi >= limite.AddDays(-2) && EstQuestion(dernier.Contenu)
                && !deja.Contains((dernier.Id, TypeSuivi.SansReponse)))
            {
                var du = dernier.DateEnvoi.AddHours(Math.Max(1, heuresSansReponse));
                if (du <= maintenant)
                    res.Add(Nouveau(dernier, k, TypeSuivi.SansReponse, du));
            }

            // 3. Mes promesses récentes
            foreach (var m in liste)
            {
                if (!estMoi(m) || m.DateEnvoi < limite || !EstPromesse(m.Contenu) || deja.Contains((m.Id, TypeSuivi.Promesse)))
                    continue;
                DateTime du = ExtraireEcheance(m.Contenu, m.DateEnvoi) ?? m.DateEnvoi.AddHours(24);
                res.Add(Nouveau(m, k, TypeSuivi.Promesse, du));
            }
        }
        return res;
    }

    public static Suivi Nouveau(MessageChat m, string cle, TypeSuivi type, DateTime echeance) => new()
    {
        MessageId = m.Id, Cle = cle, Type = type, Echeance = echeance,
        Texte = Extrait(m.CorpsAffiche()), Cree = DateTime.Now
    };

    public static string Extrait(string t)
    {
        t = t.Replace('\n', ' ').Trim();
        return t.Length <= 120 ? t : t[..120] + "…";
    }

    // ===================== CHOIX DE RAPPEL ET ÉTAT =====================

    /// <summary>Les délais proposés dans « Me le rappeler » (libellé, date).</summary>
    public static List<(string Libelle, DateTime Quand)> ChoixRappel(DateTime maintenant)
    {
        var l = new List<(string, DateTime)> { ("Dans 1 heure", maintenant.AddHours(1)) };
        if (maintenant.Hour < 17) l.Add(("Ce soir (19 h)", maintenant.Date.AddHours(19)));
        l.Add(("Demain matin (9 h)", maintenant.Date.AddDays(1).AddHours(9)));
        l.Add(("Dans 3 jours", maintenant.Date.AddDays(3).AddHours(9)));
        l.Add(("Lundi prochain (9 h)", ProchainJour(maintenant, DayOfWeek.Monday).AddHours(9)));
        return l;
    }

    public static int NombreDus(IEnumerable<Suivi> suivis, DateTime maintenant) => suivis.Count(s => s.EstDu(maintenant));

    public static ISet<(Guid, TypeSuivi)> Connus(IEnumerable<Suivi> suivis)
        => suivis.Select(s => (s.MessageId, s.Type)).ToHashSet();

    /// <summary>Un suivi « à répondre » / « sans réponse » est réglé si un message est arrivé ou parti depuis.</summary>
    public static bool EstResolu(Suivi s, IReadOnlyList<MessageChat> conversation, Func<MessageChat, bool> estMoi)
    {
        int i = conversation.ToList().FindIndex(m => m.Id == s.MessageId);
        if (i < 0) return true;                                   // le message n'existe plus
        var apres = conversation.Skip(i + 1).Where(m => !m.EstAction && !m.Supprime);
        return s.Type switch
        {
            TypeSuivi.ARepondre => apres.Any(estMoi),             // j'ai répondu
            TypeSuivi.SansReponse => apres.Any(m => !estMoi(m)),  // il a répondu
            _ => false
        };
    }
}

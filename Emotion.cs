using System.Text;
using System.Text.RegularExpressions;

namespace TALKORA.Core;

public enum Emotion { Neutre, Joie, Amour, Gratitude, Tristesse, Colere, Stress }

public record ResultatEmotion(Emotion Emotion, double Intensite)
{
    public static readonly ResultatEmotion Neutre = new(Emotion.Neutre, 0);
}

/// <summary>
/// Talkora Emotion : devine l'humeur d'un message (joie, amour, gratitude, tristesse, colère, stress) avec des règles
/// en français et en anglais, sur l'ordinateur, sans IA ni Internet. C'est une AIDE : le sarcasme, l'humour noir ou
/// une langue mélangée la trompent. Elle ne sert qu'à afficher un petit emoji et à prévenir avant un message agressif.
/// </summary>
public static class ServiceEmotion
{
    public const double SeuilAffichage = 0.5;

    public static string Emoji(Emotion e) => e switch
    {
        Emotion.Joie => "😊", Emotion.Amour => "❤️", Emotion.Gratitude => "🙏",
        Emotion.Tristesse => "😢", Emotion.Colere => "😠", Emotion.Stress => "😟", _ => "😐"
    };

    public static string Nom(Emotion e) => e switch
    {
        Emotion.Joie => "joie", Emotion.Amour => "affection", Emotion.Gratitude => "gratitude",
        Emotion.Tristesse => "tristesse", Emotion.Colere => "colère", Emotion.Stress => "stress", _ => "neutre"
    };

    private static string Normal(string? t) => Regex.Replace(Organisation.Normaliser(t), @"[’'`\-–_]", " ");

    // Chaque motif = un mot ou début de mot (sur le texte sans accents). Poids 1 = indice, 2 = fort.
    // Mots entiers ; un « * » à la fin d'un mot = n'importe quelle fin (« heureu* » : heureux, heureuse, heureusement)
    private static readonly (Emotion E, Regex R, double Poids)[] Lexique =
    {
        (Emotion.Joie, R("content|contente|heureu*|genial*|super|excellent*|magnifique|formidable|parfait*|top|cool|bravo|felicitation*|youpi|yes|trop bien|j adore|enchante*|ravi|ravie|hourra|chouette|waouh|wow|great|awesome|amazing|happy|glad|congrats|fantastic|wonderful|haha*|hihi*|mdr|ptdr|lol|xd|so happy"), 1.2),
        (Emotion.Amour, R("je t aime|je taime|mon amour|ma cherie|mon cheri|bisou*|bises|calin*|tu me manques|tu m a manque|love you|miss you|i love|mon coeur|ma puce|bebe|je pense a toi"), 1.5),
        (Emotion.Gratitude, R("merci*|thanks|thank you|thx|reconnaissant*|je te remercie|je vous remercie|grace a toi|gentil*|appreciate*"), 1.2),
        (Emotion.Tristesse, R("triste*|deprim*|pleur*|chagrin|decu|decue|decevant*|malheureu*|desole*|navre*|dommage|seul|seule|ca va pas|je vais mal|mal au coeur|sad|sorry|depressed|crying|lonely|deces|enterrement|condoleance*"), 1.2),
        (Emotion.Colere, R("enerv*|furieu*|colere|marre|ras le bol|insupportable|inadmissible|scandaleu*|honteu*|ridicule|nul|nulle|idiot*|imbecile|debile|stupide|ferme la|degage|je te deteste|je deteste|fache*|angry|furious|hate|stupid|shut up|annoying|pathetic|disgusting|useless|incompetent*|menteu*|salaud|con|conne|connard|merde|putain|bordel|ta gueule|ferme ta gueule|abruti*|cretin*|bete|ordure|salope|pute|enfoire|trouduc|fdp|ntm|tg|tais toi|je te hais|tu pues|va te faire|degoutant*|minable|pauvre type|vaurien|nul a chier|tu me saoules|tu m enerves|get lost|loser|jerk|idiot|moron|bastard|bitch|fuck*|asshole|dumb"), 1.5),
        (Emotion.Stress, R("stress*|angoiss*|inquiet*|peur|panique|paniq*|urgent*|urgence|vite|en retard|pas le temps|deadline|a l aide|au secours|catastroph*|probleme*|bug|plante|ne marche pas|marche pas|worried|anxious|scared|afraid|panic|asap|emergency|late"), 1.0),
    };

    // « a|b* » -> \b(?:a|b\w*)\b
    private static Regex R(string mots)
        => new(@"\b(?:" + string.Join("|", mots.Split('|').Select(w => Regex.Escape(w).Replace(@"\*", @"\w*"))) + @")\b", RegexOptions.Compiled);

    private static readonly (Emotion E, string[] Signes)[] Emojis =
    {
        (Emotion.Joie, new[] { "😀","😃","😄","😁","😆","😂","🤣","😊","🙂","😎","🥳","🎉","👍","👏","😅" }),
        (Emotion.Amour, new[] { "😍","🥰","😘","😻","💕","💖","💗","💞","💘" }),
        (Emotion.Gratitude, new[] { "🙏","🤝" }),
        (Emotion.Tristesse, new[] { "😢","😭","😞","😔","🥺","💔","😿","☹️","🙁","😩","😫","😥","😪","🥲" }),
        (Emotion.Colere, new[] { "😡","🤬","😠","👎","🖕","💢","😤","🙄","😒" }),
        (Emotion.Stress, new[] { "😰","😱","😟","😓","😨","😬","🆘","⚠️" }),
    };

    private static readonly Regex Negation = new(@"(?:\bpas\b|\bjamais\b|\bplus\b|\bnot\b|\bn t\b|\bne\b|\bsans\b|\bno\b)\s+(?:\w+\s+){0,2}$", RegexOptions.Compiled);

    /// <summary>L'émotion dominante d'un texte, avec une intensité de 0 à 1 (sous 0,5 : on considère le message neutre).</summary>
    public static ResultatEmotion Analyser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return ResultatEmotion.Neutre;
        string brut = texte;
        string n = Normal(texte);
        var score = new Dictionary<Emotion, double>();
        void Ajouter(Emotion e, double v) => score[e] = score.GetValueOrDefault(e) + v;

        foreach (var (e, r, poids) in Lexique)
            foreach (Match m in r.Matches(n))
            {
                string avant = n[..m.Index];
                avant = avant.Length > 24 ? avant[^24..] : avant;
                bool nie = Negation.IsMatch(avant);
                if (nie)
                {
                    // « pas content » = plutôt triste ; « pas triste » = rien ; « pas de problème » = rien
                    if (e == Emotion.Joie) Ajouter(Emotion.Tristesse, poids);
                    continue;
                }
                Ajouter(e, poids);
            }

        foreach (var (e, signes) in Emojis)
            foreach (string s in signes)
            {
                int i = 0, nb = 0;
                while ((i = brut.IndexOf(s, i, StringComparison.Ordinal)) >= 0) { nb++; i += s.Length; }
                if (nb > 0) Ajouter(e, (e is Emotion.Tristesse or Emotion.Colere or Emotion.Stress ? 1.2 : 1.0) + Math.Min(2, nb - 1) * 0.3);
            }

        // Intensité : points d'exclamation et MAJUSCULES amplifient l'émotion la plus forte… et font basculer vers la colère/le stress
        int excl = brut.Count(c => c == '!');
        var lettres = brut.Where(char.IsLetter).ToList();
        bool cris = lettres.Count >= 8 && lettres.Count(char.IsUpper) >= lettres.Count * 0.7;
        if (score.Count == 0)
        {
            if (cris && excl >= 2) return new(Emotion.Colere, 0.6);
            return ResultatEmotion.Neutre;
        }

        var (top, val) = score.OrderByDescending(kv => kv.Value).First();
        double intensite = Math.Min(1.0, val / 2.0);
        if (excl >= 2) intensite = Math.Min(1.0, intensite + 0.15);
        if (cris) { intensite = Math.Min(1.0, intensite + 0.25); if (top is Emotion.Neutre) top = Emotion.Colere; }
        if (intensite < SeuilAffichage) return new(Emotion.Neutre, intensite);
        return new(top, Math.Round(intensite, 3));
    }

    private static readonly Regex Insulte = new(
        @"\b(?:idiot|idiote|imbecile|debile|stupide|con|conne|connard|salaud|ferme la|degage|nul a chier|je te deteste|tu es nul|t es nul|tu m enerves|shut up|stupid|hate you|you suck|ta gueule|ferme ta gueule|abruti|abrutie|cretin|cretine|ordure|salope|pute|enfoire|trouduc|fdp|ntm|tais toi|je te hais|tu pues|va te faire|pauvre type|minable|tu es bete|t es bete|tu es debile|tu es con|fuck you|asshole|bitch|moron|idiot)\b", RegexOptions.Compiled);

    /// <summary>Vrai si le brouillon risque de blesser : insultes, ou colère forte (cris, !!!).</summary>
    public static bool EstAgressif(string? brouillon)
    {
        if (string.IsNullOrWhiteSpace(brouillon)) return false;
        string n = Normal(brouillon);
        if (Insulte.IsMatch(n)) return true;
        var r = Analyser(brouillon);
        return r.Emotion == Emotion.Colere && r.Intensite >= 0.85;
    }

    // ===================== HUMEUR D'UNE CONVERSATION =====================

    public record Humeur(Emotion Dominante, double Intensite, int Messages, List<ResultatEmotion> Serie);

    /// <summary>
    /// Humeur d'un côté de la discussion : les « max » derniers messages texte de ceux dont « garde » est vrai,
    /// les plus récents pèsent plus. « Serie » est dans l'ordre du temps.
    /// </summary>
    public static Humeur HumeurDe(IEnumerable<MessageChat> messagesEnOrdre, Func<MessageChat, bool> garde, DateTime maintenant, int max = 10)
    {
        var l = messagesEnOrdre
            .Where(m => !m.EstAction && !m.Supprime && !m.EstScellee(maintenant) && m.Type == TypeMessage.Texte && m.Contenu.Trim().Length > 0 && garde(m))
            .TakeLast(max).Select(m => Analyser(m.Contenu)).ToList();
        if (l.Count == 0) return new(Emotion.Neutre, 0, 0, l);

        var poids = new Dictionary<Emotion, double>();
        for (int i = 0; i < l.Count; i++)
        {
            if (l[i].Emotion == Emotion.Neutre) continue;
            double w = (1 + i) / (double)l.Count;                // le dernier compte pour 1, le premier pour 1/n
            poids[l[i].Emotion] = poids.GetValueOrDefault(l[i].Emotion) + w * l[i].Intensite;
        }
        if (poids.Count == 0) return new(Emotion.Neutre, 0, l.Count, l);
        var (top, val) = poids.OrderByDescending(kv => kv.Value).First();
        double part = val / poids.Values.Sum();
        return new(top, Math.Round(Math.Min(1, part), 3), l.Count, l);
    }

    /// <summary>Le bilan en phrases : humeur de l'autre, de moi, évolution, et un conseil.</summary>
    public static string Bilan(IReadOnlyList<MessageChat> messagesEnOrdre, Func<MessageChat, bool> estMoi, string nomAutre, DateTime maintenant)
    {
        var autre = HumeurDe(messagesEnOrdre, m => !estMoi(m), maintenant);
        var moi = HumeurDe(messagesEnOrdre, estMoi, maintenant);
        if (autre.Messages + moi.Messages == 0) return "Pas assez de messages pour deviner l'humeur de cette discussion.";

        string Ligne(string qui, Humeur h) => h.Messages == 0 ? $"{qui} : pas de message récent"
            : h.Dominante == Emotion.Neutre ? $"{qui} : plutôt neutre ({h.Messages} message(s) analysés)"
            : $"{qui} : surtout {Emoji(h.Dominante)} {Nom(h.Dominante)} ({h.Messages} message(s) analysés)";

        var sb = new StringBuilder();
        sb.AppendLine("🎭 Humeur de la discussion");
        sb.AppendLine();
        sb.AppendLine(Ligne(nomAutre, autre));
        sb.AppendLine(Ligne("Vous", moi));
        if (autre.Serie.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Évolution de {nomAutre} (du plus ancien au plus récent) :");
            sb.AppendLine(string.Join(" ", autre.Serie.Select(r => Emoji(r.Emotion))));
        }
        sb.AppendLine();
        sb.Append(Conseil(autre, nomAutre));
        sb.AppendLine();
        sb.AppendLine();
        sb.Append("Ceci est une estimation par mots-clés : le sarcasme ou l'humour peuvent la tromper.");
        return sb.ToString();
    }

    public static string Conseil(Humeur autre, string nom)
    {
        if (autre.Messages == 0) return "Conseil : rien à signaler.";
        return autre.Dominante switch
        {
            Emotion.Colere => $"Conseil : {nom} semble en colère. Évitez de répondre à chaud : reconnaissez ce qu'il/elle ressent, puis proposez une solution.",
            Emotion.Tristesse => $"Conseil : {nom} semble triste. Un message d'écoute (« je suis là, tu veux en parler ? ») vaut mieux qu'une solution toute faite.",
            Emotion.Stress => $"Conseil : {nom} semble stressé(e) ou pressé(e). Répondez court et clair, et dites quand vous pourrez agir.",
            Emotion.Joie => $"Conseil : {nom} est de bonne humeur : bon moment pour proposer ou demander quelque chose.",
            Emotion.Amour => $"Conseil : {nom} vous montre de l'affection. Un petit mot en retour fera plaisir.",
            Emotion.Gratitude => $"Conseil : {nom} vous remercie. Un simple « avec plaisir » suffit.",
            _ => "Conseil : rien de particulier, la discussion est calme."
        };
    }
}

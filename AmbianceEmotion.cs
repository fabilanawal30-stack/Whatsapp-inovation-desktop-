namespace TALKORA.Core;

/// <summary>L'ambiance qu'on ajoute à SON message (voyage avec le message ; une ancienne version l'ignore simplement).</summary>
public enum Ambiance { Aucune = 0, Douceur = 1, Joie = 2, Nostalgie = 3, Surprise = 4 }

/// <summary>Ce que la « vague d'émotion » doit dessiner : une courbe sinusoïdale qui monte si l'autre est content, descend s'il est en colère, reste stable sinon.</summary>
public record StyleVague(double Niveau, double Hauteur, double Amplitude, double Vitesse, string Teinte);

public static class AmbianceEmotion
{
    public const double ZoneNeutre = 0.12;            // en dessous : « normal », la courbe reste stable
    public const int MinutesAmbianceRecente = 10;     // une ambiance de message influence la vague pendant 10 min

    public static string Emoji(Ambiance a) => a switch
    {
        Ambiance.Douceur => "🌸", Ambiance.Joie => "😄", Ambiance.Nostalgie => "🕰", Ambiance.Surprise => "😮", _ => ""
    };

    public static string Nom(Ambiance a) => a switch
    {
        Ambiance.Douceur => "douceur", Ambiance.Joie => "joie", Ambiance.Nostalgie => "nostalgie", Ambiance.Surprise => "surprise", _ => "aucune"
    };

    /// <summary>De -1 (très négatif) à +1 (très positif).</summary>
    public static double Valence(Emotion e) => e switch
    {
        Emotion.Joie => 1.0, Emotion.Amour => 0.9, Emotion.Gratitude => 0.7,
        Emotion.Tristesse => -0.5, Emotion.Stress => -0.35, Emotion.Colere => -1.0, _ => 0.0
    };

    /// <summary>
    /// Humeur ambiante de l'AUTRE, de -1 à +1 : moyenne pondérée des derniers messages (les récents comptent plus).
    /// Un message neutre « dilue » : une conversation calme donne ~0 (courbe stable).
    /// </summary>
    public static double Niveau(IEnumerable<MessageChat> messagesEnOrdre, Func<MessageChat, bool> garde, DateTime maintenant, int max = 8)
    {
        var l = messagesEnOrdre
            .Where(m => !m.EstAction && !m.Supprime && !m.EstScellee(maintenant) && m.Type == TypeMessage.Texte && (m.Contenu ?? "").Trim().Length > 0 && garde(m))
            .TakeLast(max).Select(m => ServiceEmotion.Analyser(m.Contenu)).ToList();
        if (l.Count == 0) return 0;
        double somme = 0, poids = 0;
        for (int i = 0; i < l.Count; i++)
        {
            double w = (1 + i) / (double)l.Count;
            somme += w * Valence(l[i].Emotion) * Math.Clamp(0.5 + 0.5 * l[i].Intensite, 0, 1);
            poids += w;
        }
        double n = poids == 0 ? 0 : somme / poids;
        // Un message très marqué (colère, joie) doit se voir même noyé dans des messages neutres
        n *= 1.6;
        n = Math.Clamp(n, -1, 1);
        return Math.Abs(n) < ZoneNeutre ? 0 : n;
    }

    /// <summary>L'ambiance du dernier message qui en porte une, si elle est récente.</summary>
    public static Ambiance AmbianceRecente(IEnumerable<MessageChat> messagesEnOrdre, DateTime maintenant)
    {
        var dernier = messagesEnOrdre.Where(m => !m.EstAction && !m.Supprime && m.Ambiance != Ambiance.Aucune).LastOrDefault();
        return dernier is not null && maintenant - dernier.DateEnvoi <= TimeSpan.FromMinutes(MinutesAmbianceRecente) ? dernier.Ambiance : Ambiance.Aucune;
    }

    /// <summary>Hauteur de la ligne centrale : 0 (bas) à 1 (haut). Monte avec la joie, descend avec la colère, 0,5 si normal.</summary>
    public static StyleVague Style(double niveau, Ambiance ambiance)
    {
        niveau = Math.Clamp(niveau, -1, 1);
        double hauteur = 0.5 + 0.30 * niveau;
        double amplitude = 0.10 + 0.14 * Math.Abs(niveau);
        double vitesse = 1.2 + 1.4 * Math.Max(niveau, 0) + 1.0 * Math.Max(-niveau, 0);
        string teinte = niveau > ZoneNeutre ? "or" : niveau < -ZoneNeutre ? "rouge" : "argent";

        switch (ambiance)
        {
            case Ambiance.Douceur: vitesse *= 0.6; amplitude *= 0.8; teinte = "rose"; break;
            case Ambiance.Joie: vitesse *= 1.4; amplitude *= 1.3; teinte = "or"; break;
            case Ambiance.Nostalgie: vitesse *= 0.5; teinte = "sepia"; break;
            case Ambiance.Surprise: vitesse *= 1.8; amplitude *= 1.5; teinte = "bleu"; break;
        }
        return new StyleVague(niveau, Math.Clamp(hauteur, 0.15, 0.85), Math.Min(amplitude, 0.4), vitesse, teinte);
    }
}

using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Un sujet (branche) vu depuis une conversation.</summary>
public sealed class Sujet
{
    public Guid Id { get; set; }
    public string Titre { get; set; } = "";
    public int Nombre { get; set; }
    public DateTime Dernier { get; set; }
    public string Dossier { get; set; } = ServiceBranches.SansDossier;
    public string Origine { get; set; } = "";
}

/// <summary>Réglages PERSONNELS des branches : dossiers de rangement et titres renommés (ne sont pas envoyés aux autres).</summary>
public sealed class PreferencesBranches
{
    public Dictionary<string, string> Dossiers { get; set; } = new();   // id de branche -> nom du dossier
    public Dictionary<string, string> Titres { get; set; } = new();     // id de branche -> titre perso

    [System.Text.Json.Serialization.JsonIgnore] public string Chemin { get; set; } = "";

    public static PreferencesBranches Charger(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        string chemin = Path.Combine(dossier, "branches.json");
        PreferencesBranches p;
        try { p = File.Exists(chemin) ? JsonSerializer.Deserialize<PreferencesBranches>(File.ReadAllText(chemin)) ?? new() : new(); }
        catch (Exception) { p = new(); }
        p.Chemin = chemin;
        return p;
    }

    public void Sauver()
    {
        try { File.WriteAllText(Chemin, JsonSerializer.Serialize(this)); } catch (Exception) { }
    }

    public void Ranger(Guid id, string dossier)
    {
        dossier = ServiceBranches.NettoyerTitre(dossier);
        if (dossier.Length == 0 || dossier == ServiceBranches.SansDossier) Dossiers.Remove(id.ToString("N"));
        else Dossiers[id.ToString("N")] = dossier;
        Sauver();
    }

    public void Renommer(Guid id, string titre)
    {
        titre = ServiceBranches.NettoyerTitre(titre);
        if (titre.Length == 0) Titres.Remove(id.ToString("N")); else Titres[id.ToString("N")] = titre;
        Sauver();
    }
}

/// <summary>Discussions par sujet : chaque branche a son propre fil, rangeable en dossiers.</summary>
public static class ServiceBranches
{
    public const int TitreMax = 60;
    public const string SansDossier = "Sans dossier";

    /// <summary>Supprime les caractères de contrôle, les espaces en trop, coupe à 60 caractères.</summary>
    public static string NettoyerTitre(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var propre = new string(t.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return propre.Length > TitreMax ? propre[..TitreMax].TrimEnd() : propre;
    }

    public static string TitrePropose(MessageChat? racine)
    {
        string t = NettoyerTitre(racine?.Contenu);
        if (t.Length > 40) t = t[..40].TrimEnd() + "…";
        return t.Length == 0 ? "Nouveau sujet" : t;
    }

    /// <summary>Les messages d'une branche, du plus ancien au plus récent.</summary>
    public static List<MessageChat> Fil(IEnumerable<MessageChat> conversation, Guid id)
        => conversation.Where(m => m.BrancheId == id && !m.Supprime).OrderBy(m => m.DateEnvoi).ToList();

    public static List<Sujet> Sujets(IReadOnlyCollection<MessageChat> conversation, PreferencesBranches prefs)
    {
        var liste = new List<Sujet>();
        foreach (var g in conversation.Where(m => m.BrancheId != Guid.Empty && !m.Supprime).GroupBy(m => m.BrancheId))
        {
            var racine = conversation.FirstOrDefault(m => m.Id == g.Key);
            string cle = g.Key.ToString("N");
            string titre = prefs.Titres.TryGetValue(cle, out var perso) && perso.Length > 0
                ? perso
                : NettoyerTitre(g.OrderByDescending(m => m.DateEnvoi).Select(m => m.BrancheTitre).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            if (titre.Length == 0) titre = TitrePropose(racine);
            liste.Add(new Sujet
            {
                Id = g.Key, Titre = titre, Nombre = g.Count(), Dernier = g.Max(m => m.DateEnvoi),
                Dossier = prefs.Dossiers.TryGetValue(cle, out var d) && d.Length > 0 ? d : SansDossier,
                Origine = racine?.Contenu ?? ""
            });
        }
        return liste;
    }

    /// <summary>Regroupe par dossier : dossiers par ordre alphabétique, « Sans dossier » à la fin ; sujets récents d'abord.</summary>
    public static List<(string Dossier, List<Sujet> Sujets)> Regrouper(IEnumerable<Sujet> sujets)
        => sujets.GroupBy(s => s.Dossier)
                 .OrderBy(g => g.Key == SansDossier ? 1 : 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                 .Select(g => (g.Key, g.OrderByDescending(s => s.Dernier).ToList())).ToList();
}

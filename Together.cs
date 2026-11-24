using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Un élément de l'espace partagé (une tâche, une course, une idée…).</summary>
public class ItemTogether
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Espace { get; set; } = "";       // local seulement : clé de la conversation (IP du contact ou Id du groupe) ; jamais envoyé
    public string Texte { get; set; } = "";
    public bool Fait { get; set; }
    public bool Supprime { get; set; }             // « pierre tombale » : l'élément supprimé reste connu pour que la suppression se propage
    public string CreePar { get; set; } = "";
    public string ModifiePar { get; set; } = "";
    public int Version { get; set; }               // +1 à chaque modification : la plus haute version gagne
    public DateTime Cree { get; set; } = DateTime.UtcNow;
    public DateTime Modifie { get; set; } = DateTime.UtcNow;
}

/// <summary>Le contenu d'un message « Together ». « Espace » est « d » (discussion directe) ou « g:{Id du groupe} ».</summary>
public class PaquetTogether
{
    public string Espace { get; set; } = "d";
    public List<ItemTogether> Items { get; set; } = new();
}

/// <summary>
/// Talkora Together : un espace partagé par conversation (liste de choses à faire / à acheter / idées) que tous les participants
/// modifient en même temps, y compris hors ligne : chacun garde sa copie et, quand deux ordinateurs se retrouvent,
/// les copies sont FUSIONNÉES (même résultat chez tout le monde, quel que soit l'ordre d'arrivée).
/// Règle : pour chaque élément, la version la plus haute gagne ; à égalité, la suppression gagne, puis l'heure, puis l'IP.
/// </summary>
public static class ServiceTogether
{
    public const int TexteMax = 200;
    public const int ItemsParPaquet = 60;
    public const int ItemsMaxParEspace = 300;       // (supprimés compris) : protège contre un contact qui inonderait l'espace
    public const int VersionMax = 1_000_000;
    public const int JoursPierres = 30;

    // ===================== OPÉRATIONS LOCALES =====================

    /// <summary>Nettoie un texte : sans retours à la ligne ni caractères de contrôle, coupé à 200 caractères.</summary>
    public static string NettoyerTexte(string? t)
    {
        if (t is null) return "";
        var sb = new System.Text.StringBuilder(t.Length);
        foreach (char c in t)
            sb.Append(char.IsControl(c) ? ' ' : c);
        string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        return r.Length > TexteMax ? r[..TexteMax].TrimEnd() : r;
    }

    public static ItemTogether? Creer(string espace, string? texte, string moiIp, DateTime maintenantUtc)
    {
        string t = NettoyerTexte(texte);
        if (t.Length == 0 || string.IsNullOrEmpty(espace)) return null;
        return new ItemTogether
        {
            Espace = espace, Texte = t, CreePar = moiIp, ModifiePar = moiIp, Version = 1, Cree = maintenantUtc, Modifie = maintenantUtc
        };
    }

    private static bool Toucher(ItemTogether it, string moiIp, DateTime maintenantUtc)
    {
        if (it.Version >= VersionMax) return false;
        it.Version++;
        it.ModifiePar = moiIp;
        it.Modifie = maintenantUtc;
        return true;
    }

    public static bool Basculer(ItemTogether it, string moiIp, DateTime maintenantUtc)
    {
        if (it.Supprime) return false;
        bool ok = Toucher(it, moiIp, maintenantUtc);
        if (ok) it.Fait = !it.Fait;
        return ok;
    }

    public static bool Modifier(ItemTogether it, string? texte, string moiIp, DateTime maintenantUtc)
    {
        string t = NettoyerTexte(texte);
        if (it.Supprime || t.Length == 0 || t == it.Texte) return false;
        bool ok = Toucher(it, moiIp, maintenantUtc);
        if (ok) it.Texte = t;
        return ok;
    }

    public static bool Supprimer(ItemTogether it, string moiIp, DateTime maintenantUtc)
    {
        if (it.Supprime) return false;
        bool ok = Toucher(it, moiIp, maintenantUtc);
        if (ok) it.Supprime = true;
        return ok;
    }

    // ===================== FUSION =====================

    /// <summary>« a » l'emporte-t-il sur « b » ? (même élément, deux copies)</summary>
    public static bool Gagne(ItemTogether a, ItemTogether b)
    {
        if (a.Version != b.Version) return a.Version > b.Version;
        if (a.Supprime != b.Supprime) return a.Supprime;
        if (a.Modifie != b.Modifie) return a.Modifie > b.Modifie;
        return string.CompareOrdinal(a.ModifiePar, b.ModifiePar) > 0;
    }

    /// <summary>
    /// Fusionne des éléments reçus dans la liste locale (qui est modifiée). Renvoie ceux qui ont changé ou qui sont nouveaux.
    /// Idempotent (appliquer deux fois = une fois) et indépendant de l'ordre. « espace » est la clé LOCALE de la conversation.
    /// </summary>
    public static List<ItemTogether> Fusionner(List<ItemTogether> local, string espace, IEnumerable<ItemTogether> recus)
    {
        var changes = new List<ItemTogether>();
        var parId = local.Where(x => x.Espace == espace).ToDictionary(x => x.Id);
        foreach (var brut in recus)
        {
            var r = Valider(brut);
            if (r is null) continue;
            r.Espace = espace;
            if (parId.TryGetValue(r.Id, out var existant))
            {
                if (!Gagne(r, existant)) continue;
                existant.Texte = r.Texte; existant.Fait = r.Fait; existant.Supprime = r.Supprime;
                existant.Version = r.Version; existant.Modifie = r.Modifie; existant.ModifiePar = r.ModifiePar;
                changes.Add(existant);
            }
            else
            {
                if (parId.Count >= ItemsMaxParEspace) continue;
                local.Add(r);
                parId[r.Id] = r;
                changes.Add(r);
            }
        }
        return changes;
    }

    /// <summary>Copie assainie d'un élément venant du réseau, ou null s'il est invalide.</summary>
    public static ItemTogether? Valider(ItemTogether? it)
    {
        if (it is null || it.Id == Guid.Empty || it.Version < 1 || it.Version > VersionMax) return null;
        string t = NettoyerTexte(it.Texte);
        if (t.Length == 0 && !it.Supprime) return null;
        var maintenant = DateTime.UtcNow;
        return new ItemTogether
        {
            Id = it.Id, Texte = t, Fait = it.Fait, Supprime = it.Supprime, Version = it.Version,
            CreePar = Court(it.CreePar), ModifiePar = Court(it.ModifiePar),
            Cree = Borner(it.Cree, maintenant), Modifie = Borner(it.Modifie, maintenant)
        };
    }

    private static string Court(string? s) => (s ?? "").Length > 45 ? s![..45] : (s ?? "");

    // Une date folle (an 1, an 9999) ne doit pas fausser les égalités : on la ramène à une plage raisonnable
    private static DateTime Borner(DateTime d, DateTime maintenant)
    {
        var u = d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
        if (u < maintenant.AddYears(-20)) return maintenant.AddYears(-20);
        if (u > maintenant.AddDays(1)) return maintenant.AddDays(1);
        return u;
    }

    // ===================== AFFICHAGE =====================

    /// <summary>Éléments visibles d'un espace : à faire d'abord (les plus anciens en premier), puis faits.</summary>
    public static List<ItemTogether> Visibles(IEnumerable<ItemTogether> items, string espace)
        => items.Where(x => x.Espace == espace && !x.Supprime).OrderBy(x => x.Fait).ThenBy(x => x.Cree).ThenBy(x => x.Id).ToList();

    public static int Restant(IEnumerable<ItemTogether> items, string espace)
        => items.Count(x => x.Espace == espace && !x.Supprime && !x.Fait);

    /// <summary>Retire les pierres tombales trop vieilles (tout le monde a eu le temps d'apprendre la suppression). Renvoie les retirés.</summary>
    public static List<ItemTogether> Purger(List<ItemTogether> items, DateTime maintenantUtc)
    {
        var vieux = items.Where(x => x.Supprime && x.Modifie < maintenantUtc.AddDays(-JoursPierres)).ToList();
        foreach (var v in vieux) items.Remove(v);
        return vieux;
    }

    // ===================== RÉSEAU =====================

    public static string EspaceFil(string cleLocale, bool groupe) => groupe ? "g:" + cleLocale : "d";

    /// <summary>
    /// Clé locale de l'espace pour un paquet reçu : « d » -> l'IP de l'expéditeur ; « g:{id} » -> l'Id du groupe, SEULEMENT si
    /// l'expéditeur en est membre. Null sinon (paquet ignoré).
    /// </summary>
    public static string? ResoudreEspace(string? fil, string expediteurIp, Func<string, string, bool> expediteurMembreDuGroupe)
    {
        if (string.IsNullOrEmpty(fil) || string.IsNullOrEmpty(expediteurIp)) return null;
        if (fil == "d") return expediteurIp;
        if (fil.StartsWith("g:") && Guid.TryParse(fil[2..], out var id))
        {
            string cle = id.ToString();
            return expediteurMembreDuGroupe(cle, expediteurIp) ? cle : null;
        }
        return null;
    }

    /// <summary>Un ou plusieurs messages (60 éléments au plus chacun) qui portent ces éléments vers « destinataire ».</summary>
    public static List<MessageChat> CreerMessages(ProfilUtilisateur moi, string destinataire, string fil, IReadOnlyList<ItemTogether> items)
    {
        var res = new List<MessageChat>();
        for (int i = 0; i < items.Count; i += ItemsParPaquet)
        {
            var paquet = new PaquetTogether { Espace = fil, Items = items.Skip(i).Take(ItemsParPaquet).Select(Sortant).ToList() };
            var m = MessageActions.Nouveau(moi, destinataire, JsonSerializer.Serialize(paquet));
            m.Type = TypeMessage.Together;
            res.Add(m);
        }
        return res;
    }

    private static ItemTogether Sortant(ItemTogether x) => new()
    {
        Id = x.Id, Texte = x.Texte, Fait = x.Fait, Supprime = x.Supprime, CreePar = x.CreePar, ModifiePar = x.ModifiePar,
        Version = x.Version, Cree = x.Cree, Modifie = x.Modifie, Espace = ""
    };

    /// <summary>Lit un message Together reçu. Null si ce n'en est pas un, ou s'il est trop gros ou illisible.</summary>
    public static PaquetTogether? Lire(MessageChat m)
    {
        if (m.Type != TypeMessage.Together) return null;
        string c = m.Contenu ?? "";
        if (c.Length == 0 || c.Length > 200_000) return null;
        try
        {
            var p = JsonSerializer.Deserialize<PaquetTogether>(c);
            if (p is null || p.Items is null) return null;
            if (p.Items.Count > ItemsParPaquet * 2) p.Items = p.Items.Take(ItemsParPaquet * 2).ToList();
            return p;
        }
        catch (JsonException) { return null; }
    }
}

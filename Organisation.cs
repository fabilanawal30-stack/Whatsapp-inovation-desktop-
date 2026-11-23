using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Les onglets de la liste des discussions.</summary>
public enum FiltreListe { Toutes, NonLues, Groupes, Archivees }

/// <summary>Un résultat de la recherche globale.</summary>
public record ResultatRecherche(MessageChat Message, string Cle, string Extrait);

/// <summary>Règles d'affichage de la liste des discussions : filtre, tri, recherche. Aucun code d'interface ici (donc testable).</summary>
public static class Organisation
{
    public static string CleDe(object o) => o is Groupe g ? g.Id.ToString() : ((Contact)o).AdresseIp;

    public static string NomDe(object o) => o is Groupe g ? g.Nom : ((Contact)o).Nom;

    public static bool EstEpingle(object o) => o is Groupe g ? g.Epingle : ((Contact)o).Epingle;
    public static bool EstArchive(object o) => o is Groupe g ? g.Archive : ((Contact)o).Archive;
    public static bool EstMuet(object o) => o is Groupe g ? g.Muet : ((Contact)o).Muet;

    /// <summary>
    /// Construit la liste à afficher.
    /// « Moi-même » est toujours en tête (onglet Toutes). Ensuite : les épinglés, puis les plus récents.
    /// Les archivées n'apparaissent que dans leur onglet (ou si on cherche par nom).
    /// </summary>
    public static List<object> Construire(Contact moiMeme, IEnumerable<Contact> contacts, IEnumerable<Groupe> groupes,
        FiltreListe filtre, string texte,
        IReadOnlyDictionary<string, DateTime> derniereActivite, IReadOnlyDictionary<string, int> nonLus)
    {
        string motif = Normaliser(texte);
        bool cherche = motif.Length > 0;

        var tous = new List<object>();
        tous.AddRange(contacts);
        tous.AddRange(groupes);

        bool Garde(object o)
        {
            if (cherche && !Normaliser(NomDe(o)).Contains(motif))
                return false;
            if (cherche) return true;                        // une recherche par nom trouve aussi les archivées
            return filtre switch
            {
                FiltreListe.Archivees => EstArchive(o),
                FiltreListe.NonLues => !EstArchive(o) && nonLus.GetValueOrDefault(CleDe(o)) > 0,
                FiltreListe.Groupes => !EstArchive(o) && o is Groupe,
                _ => !EstArchive(o)
            };
        }

        DateTime Activite(object o) => derniereActivite.TryGetValue(CleDe(o), out var d) ? d : DateTime.MinValue;

        var res = tous.Where(Garde)
            .OrderByDescending(EstEpingle)
            .ThenByDescending(Activite)
            .ThenBy(o => NomDe(o), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        bool moiOk = filtre == FiltreListe.Toutes && (!cherche || Normaliser(moiMeme.Nom).Contains(motif));
        if (moiOk)
            res.Insert(0, moiMeme);
        return res;
    }

    /// <summary>Épingler : 3 conversations au maximum (comme WhatsApp). Renvoie faux si la limite est atteinte.</summary>
    public const int EpinglesMax = 3;

    public static bool PeutEpingler(IEnumerable<Contact> contacts, IEnumerable<Groupe> groupes)
        => contacts.Count(c => c.Epingle) + groupes.Count(g => g.Epingle) < EpinglesMax;

    // ----- Recherche globale -----

    /// <summary>"Éléphant" -> "elephant" : on cherche sans tenir compte des majuscules ni des accents.</summary>
    public static string Normaliser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return "";
        var d = texte.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Cherche un texte dans TOUTES les conversations. Les plus récents d'abord.</summary>
    public static List<ResultatRecherche> Chercher(IEnumerable<MessageChat> messages, string texte,
                                                   Func<MessageChat, string> cle, int max = 200)
    {
        string motif = Normaliser(texte);
        var res = new List<ResultatRecherche>();
        if (motif.Length < 2) return res;

        foreach (var m in messages.Reverse())     // les plus récents en premier
        {
            if (m.EstAction || m.Supprime || m.Type == TypeMessage.Sondage && m.Contenu.Length == 0)
                continue;

            string corps = m.Type is TypeMessage.Fichier or TypeMessage.Vocal
                ? (m.NomFichier ?? "") + " " + m.Contenu
                : m.CorpsAffiche();
            string n = Normaliser(corps);
            int i = n.IndexOf(motif, StringComparison.Ordinal);
            if (i < 0) continue;

            res.Add(new ResultatRecherche(m, cle(m), Extrait(corps, i, motif.Length)));
            if (res.Count >= max) break;
        }
        return res;
    }

    // Quelques mots autour de l'endroit trouvé (l'index vient du texte normalisé : même longueur dans 99 % des cas)
    private static string Extrait(string corps, int debut, int longueur)
    {
        string c = corps.Replace('\n', ' ').Replace('\r', ' ');
        int a = Math.Clamp(debut - 25, 0, Math.Max(0, c.Length - 1));
        int b = Math.Min(c.Length, Math.Max(a, debut) + longueur + 45);
        string t = c[a..b].Trim();
        return (a > 0 ? "…" : "") + t + (b < c.Length ? "…" : "");
    }
}

/// <summary>
/// « Où j'en suis de ma lecture » : pour chaque conversation, le dernier message que j'ai vu.
/// On retient l'IDENTIFIANT du message (pas une heure) : les horloges des PC n'ont pas besoin d'être d'accord.
/// Enregistré dans DataTalkora/lecture.json.
/// </summary>
public class ServiceLecture
{
    public Dictionary<string, Guid> DernierLu { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstNouveau { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Dossier { get; set; } = "DataTalkora";

    public static ServiceLecture Charger(string dossier = "DataTalkora")
    {
        try
        {
            string chemin = Path.Combine(dossier, "lecture.json");
            if (File.Exists(chemin))
            {
                var s = JsonSerializer.Deserialize<ServiceLecture>(File.ReadAllText(chemin)) ?? new();
                s.Dossier = dossier;
                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new ServiceLecture { EstNouveau = true, Dossier = dossier };
    }

    public void Sauvegarder()
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            string chemin = Path.Combine(Dossier, "lecture.json");
            string tmp = chemin + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, chemin, overwrite: true);
            EstNouveau = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>« J'ai tout lu » dans cette conversation, jusqu'à ce message.</summary>
    public void MarquerLu(string cle, Guid? dernierMessage)
    {
        if (dernierMessage is null) return;
        if (DernierLu.TryGetValue(cle, out var deja) && deja == dernierMessage) return;
        DernierLu[cle] = dernierMessage.Value;
        Sauvegarder();
    }

    /// <summary>Première utilisation : tout ce qui existe déjà est considéré comme lu (pas de faux badges).</summary>
    public void ToutMarquerLu(IReadOnlyDictionary<string, Guid> derniers)
    {
        foreach (var (cle, id) in derniers) DernierLu[cle] = id;
        Sauvegarder();
    }

    /// <summary>
    /// Messages non lus par conversation + identifiant du dernier message de chaque conversation.
    /// Un message non lu = reçu (pas de moi), affichable (pas une action), situé APRÈS le dernier message lu.
    /// </summary>
    public (Dictionary<string, int> NonLus, Dictionary<string, Guid> Derniers) Calculer(
        IEnumerable<MessageChat> messagesEnOrdre, Func<MessageChat, string> cle, Func<MessageChat, bool> estMoi)
    {
        var parConv = new Dictionary<string, List<MessageChat>>();
        foreach (var m in messagesEnOrdre)
        {
            if (m.EstAction) continue;
            string k = cle(m);
            if (!parConv.TryGetValue(k, out var l)) parConv[k] = l = new();
            l.Add(m);
        }

        var nonLus = new Dictionary<string, int>();
        var derniers = new Dictionary<string, Guid>();
        foreach (var (k, liste) in parConv)
        {
            derniers[k] = liste[^1].Id;
            int depart = 0;                                   // pas d'entrée : tout est à lire
            if (DernierLu.TryGetValue(k, out var lu))
            {
                int i = liste.FindIndex(m => m.Id == lu);
                if (i < 0) { nonLus[k] = 0; continue; }       // le message de repère a disparu : on ne crée pas de faux badge
                depart = i + 1;
            }
            int n = 0;
            for (int j = depart; j < liste.Count; j++)
                if (!estMoi(liste[j]) && !liste[j].Supprime) n++;
            nonLus[k] = n;
        }
        return (nonLus, derniers);
    }
}

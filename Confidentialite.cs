using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TALKORA.Core;

/// <summary>Un signalement de conversation : gardé sur CET ordinateur (rien n'est envoyé automatiquement).</summary>
public class RapportSignalement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Now;
    public string Conversation { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Motif { get; set; } = "";
    public string Commentaire { get; set; } = "";
    public bool Bloque { get; set; }
    public List<string> Extraits { get; set; } = new();       // derniers messages : « [14:30] Nom : texte »
}

public static class ServiceSignalement
{
    public static readonly string[] Motifs =
        { "Insultes ou harcèlement", "Menaces", "Contenu inapproprié", "Spam ou publicité", "Usurpation d'identité", "Autre" };

    public const int ExtraitsMax = 20;
    public const int TexteMax = 300;
    public const int RapportsMax = 200;

    private static string Court(string? t)
    {
        string s = Regex.Replace(t ?? "", @"[\r\n\t]+", " ").Trim();
        return s.Length > TexteMax ? s[..TexteMax] + "…" : s;
    }

    /// <summary>Construit le rapport avec les derniers messages texte de la conversation (jamais les fichiers).</summary>
    public static RapportSignalement Creer(string conversation, string ip, string motif, string commentaire, bool bloque,
                                           IEnumerable<MessageChat> messagesEnOrdre, DateTime maintenant)
    {
        var extraits = messagesEnOrdre
            .Where(m => !m.EstAction && !m.Supprime && m.Type == TypeMessage.Texte && (m.Contenu ?? "").Trim().Length > 0)
            .TakeLast(ExtraitsMax)
            .Select(m => $"[{m.DateEnvoi:dd/MM HH:mm}] {Court(m.Expediteur)} : {Court(m.Contenu)}").ToList();
        return new RapportSignalement
        {
            Date = maintenant, Conversation = Court(conversation), Ip = Court(ip),
            Motif = Motifs.Contains(motif) ? motif : "Autre", Commentaire = Court(commentaire), Bloque = bloque, Extraits = extraits
        };
    }

    public static string VersTexte(RapportSignalement r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SIGNALEMENT TALKORA");
        sb.AppendLine($"Date : {r.Date:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Conversation : {r.Conversation} ({r.Ip})");
        sb.AppendLine($"Motif : {r.Motif}");
        if (r.Commentaire.Length > 0) sb.AppendLine($"Commentaire : {r.Commentaire}");
        sb.AppendLine(r.Bloque ? "Contact bloqué : oui" : "Contact bloqué : non");
        sb.AppendLine();
        sb.AppendLine("Derniers messages :");
        foreach (string e in r.Extraits) sb.AppendLine("  " + e);
        return sb.ToString();
    }

    /// <summary>Ajoute le rapport à signalements.json et écrit un fichier .txt lisible. Renvoie le chemin du .txt.</summary>
    public static string Enregistrer(RapportSignalement r, string dossier = "DataTalkora")
    {
        string dossierRapports = Path.Combine(dossier, "Signalements");
        Directory.CreateDirectory(dossierRapports);

        string json = Path.Combine(dossier, "signalements.json");
        var liste = new List<RapportSignalement>();
        try { if (File.Exists(json)) liste = JsonSerializer.Deserialize<List<RapportSignalement>>(File.ReadAllText(json)) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { liste = new(); }
        liste.Add(r);
        if (liste.Count > RapportsMax) liste.RemoveRange(0, liste.Count - RapportsMax);
        File.WriteAllText(json, JsonSerializer.Serialize(liste, new JsonSerializerOptions { WriteIndented = true }));

        string txt = Path.Combine(dossierRapports, $"signalement-{r.Date:yyyyMMdd-HHmmss}-{r.Id.ToString("N")[..4]}.txt");
        File.WriteAllText(txt, VersTexte(r), new UTF8Encoding(true));
        return txt;
    }
}

public static class ServiceInvitation
{
    private static readonly Regex Courriel = new(@"^[^@\s<>,;]{1,64}@[^@\s<>,;]{1,190}\.[A-Za-z]{2,}$", RegexOptions.Compiled);

    public static bool CourrielValide(string? e) => !string.IsNullOrWhiteSpace(e) && e.Trim().Length <= 254 && Courriel.IsMatch(e.Trim());

    public static string Sujet(string monNom) => $"{Nettoyer(monNom)} vous invite sur Talkora";

    private static string Nettoyer(string? t) => Regex.Replace(t ?? "", @"[\r\n\t<>]+", " ").Trim();

    /// <summary>Texte de l'invitation : qui invite, comment installer, et comment s'ajouter (nom + adresse).</summary>
    public static string Corps(string monNom, string monIp, int port, string? nomFichierSetup)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Bonjour,");
        sb.AppendLine();
        sb.AppendLine($"{Nettoyer(monNom)} vous invite à discuter sur Talkora, une messagerie qui marche sans internet.");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(nomFichierSetup)
            ? "1. Demandez-moi le fichier d'installation de Talkora (Setup) et lancez-le."
            : $"1. Installez Talkora avec le fichier joint : {Nettoyer(Path.GetFileName(nomFichierSetup))}");
        sb.AppendLine("2. Ouvrez Talkora, saisissez votre nom.");
        sb.AppendLine("3. Connectez-vous au même Wi-Fi que moi : je m'affiche tout seul. Sinon, ajoutez-moi avec :");
        sb.AppendLine($"   Nom : {Nettoyer(monNom)}   Adresse : {monIp}   Port : {port}");
        return sb.ToString();
    }

    /// <summary>Lien « mailto: » : ouvre le logiciel de messagerie de la personne, e-mail déjà rédigé.</summary>
    public static string? LienMailto(string? courriel, string sujet, string corps)
    {
        if (!CourrielValide(courriel)) return null;
        if (corps.Length > 1500) corps = corps[..1500];
        return $"mailto:{Uri.EscapeDataString(courriel!.Trim()).Replace("%40", "@")}?subject={Uri.EscapeDataString(sujet)}&body={Uri.EscapeDataString(corps)}";
    }
}

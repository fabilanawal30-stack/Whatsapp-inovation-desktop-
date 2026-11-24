using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Les réglages d'apparence et de notifications de CET utilisateur (DataTalkora/apparence.json).</summary>
public class ParametresApparence
{
    // Fond d'écran de la conversation : une couleur "#RRGGBB" ("" = blanc argenté par défaut)
    public string FondCouleur { get; set; } = "";

    // Nom d'un fichier image copié dans DataTalkora/Fonds ("" = pas d'image)
    public string FondImage { get; set; } = "";

    // Taille de la police des messages (9 à 18)
    public int TaillePolice { get; set; } = 11;

    // Petit son à l'arrivée d'un message
    public bool SonNotifications { get; set; } = true;

    // Dictée vocale : envoyer le message tout de suite après la phrase dictée
    public bool DicteeEnvoiAuto { get; set; }

    // Verrouillage automatique après X minutes sans activité (0 = jamais). Utile seulement si un PIN est défini.
    public int VerrouillageMinutes { get; set; }

    // Cacher le texte des derniers messages dans la liste des discussions
    public bool MasquerApercus { get; set; }

    // Prévenir mes contacts quand je suis en train d'écrire (« écrit… »)
    public bool IndicateurEcriture { get; set; } = true;

    // Tali : propose des réponses sous la conversation
    public bool SuggestionsTali { get; set; } = true;

    // Talkora Follow-up : repérer les questions sans réponse et les promesses
    public bool SuivisActifs { get; set; } = true;

    // Délai avant de me rappeler qu'une question reçue attend ma réponse (heures, 1 à 72)
    public int SuiviARepondreHeures { get; set; } = 4;

    // Délai avant de signaler qu'une de MES questions est restée sans réponse (heures, 1 à 168)
    public int SuiviSansReponseHeures { get; set; } = 24;

    // Talkora Emotion : petit emoji d'humeur sous les messages reçus
    public bool AfficherEmotions { get; set; } = true;

    // Talkora Emotion : la vague sinusoïdale sous la conversation (monte si l'autre est content, descend s'il est en colère)
    public bool VagueEmotion { get; set; } = true;

    // Talkora Memories : un rappel par jour quand des souvenirs existent (« il y a 1 an… »)
    public bool SouvenirsDuJour { get; set; } = true;

    // Prévenir avant d'envoyer un message qui semble agressif
    public bool AlerteTon { get; set; } = true;

    // Ma photo de profil : visible par mes contacts (true) ou par moi seul (false)
    public bool PhotoVisible { get; set; } = true;

    // Déverrouillage par empreinte / visage (Windows Hello)
    public bool BiometrieActive { get; set; }

    // Groupes dont j'ai refusé l'invitation (identifiants) : on ne redemande pas à chaque mise à jour
    public List<string> GroupesRefuses { get; set; } = new();

    // Thème sombre (s'applique au prochain démarrage)
    public bool ThemeSombre { get; set; } = true;

    // Version du design (étape 22) : tant qu'elle est ancienne, on passe UNE fois au nouveau thème « Bordeaux & Argent » (sombre) ;
    // ensuite le choix fait dans Paramètres > Apparence est respecté.
    public int VersionDesign { get; set; }

    // Langue de l'interface : "fr" ou "en" (s'applique au prochain démarrage)
    public string LangueInterface { get; set; } = "fr";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Garde seulement le nom du fichier (retire tout dossier, avec / ou \)
    public static string NomSeul(string? chemin)
    {
        string t = chemin ?? "";
        int i = t.LastIndexOfAny(new[] { '/', '\\' });
        t = i >= 0 ? t[(i + 1)..] : t;
        return t == ".." || t == "." ? "" : t;
    }

    public static int BornerMinutes(int m) => Math.Clamp(m, 0, 240);

    public static int BornerHeures(int h, int max) => Math.Clamp(h, 1, max);

    public static int BornerPolice(int taille) => Math.Clamp(taille, 9, 18);

    // "#6E0E26" -> (110,14,38) ; texte invalide -> null
    public static (int R, int V, int B)? LireCouleur(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out int v)) return null;
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    public static ParametresApparence Charger(string dossier = "DataTalkora")
    {
        try
        {
            string chemin = Path.Combine(dossier, "apparence.json");
            if (File.Exists(chemin))
            {
                var p = JsonSerializer.Deserialize<ParametresApparence>(File.ReadAllText(chemin)) ?? new();
                if (p.VersionDesign < 2) { p.ThemeSombre = true; p.VersionDesign = 2; }
                p.TaillePolice = BornerPolice(p.TaillePolice);
                p.VerrouillageMinutes = BornerMinutes(p.VerrouillageMinutes);
                p.SuiviARepondreHeures = BornerHeures(p.SuiviARepondreHeures, 72);
                p.SuiviSansReponseHeures = BornerHeures(p.SuiviSansReponseHeures, 168);
                if (p.LangueInterface != "en") p.LangueInterface = "fr";
                // On ne garde qu'un NOM de fichier : jamais un chemin (évite de lire un fichier ailleurs sur le disque)
                p.FondImage = NomSeul(p.FondImage);
                return p;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new ParametresApparence();
    }

    public void Sauvegarder(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "apparence.json"), JsonSerializer.Serialize(this, Options));
    }
}

using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Les réglages de traduction de CET utilisateur (fichier DataTalkora/traduction.json).</summary>
public class ParametresTraduction
{
    // Traduire automatiquement les messages reçus dans une autre langue
    public bool TraductionAuto { get; set; }

    // Langue dans laquelle je veux lire ("" = celle de mon profil)
    public string LangueCible { get; set; } = "";

    // Accord explicite pour envoyer des textes au service en ligne (désactivé par défaut)
    public bool AutoriserEnLigne { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ParametresTraduction Charger(string dossier = "DataTalkora")
    {
        try
        {
            string chemin = Path.Combine(dossier, "traduction.json");
            if (File.Exists(chemin))
                return JsonSerializer.Deserialize<ParametresTraduction>(File.ReadAllText(chemin)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new ParametresTraduction();
    }

    public void Sauvegarder(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "traduction.json"), JsonSerializer.Serialize(this, Options));
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace TALKORA.Core;

public enum GraviteSecurite { Info, Attention, Alerte }

public sealed class EvenementSecurite
{
    public DateTime Date { get; set; } = DateTime.Now;
    public GraviteSecurite Gravite { get; set; }
    public string Type { get; set; } = "";      // identite, fichier, pin, appareil, biometrie…
    public string Texte { get; set; } = "";
}

/// <summary>Journal des événements de sécurité (fichier local DataTalkora/securite.json) + score de confiance.</summary>
public sealed class JournalSecurite
{
    private readonly string _chemin;
    private readonly object _verrou = new();
    private List<EvenementSecurite> _liste = new();
    public event Action? Change;

    public JournalSecurite(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        _chemin = Path.Combine(dossier, "securite.json");
        try { if (File.Exists(_chemin)) _liste = JsonSerializer.Deserialize<List<EvenementSecurite>>(File.ReadAllText(_chemin)) ?? new(); }
        catch (Exception) { _liste = new(); }
    }

    public void Ajouter(GraviteSecurite g, string type, string texte)
    {
        lock (_verrou)
        {
            _liste.Add(new EvenementSecurite { Gravite = g, Type = type, Texte = texte });
            if (_liste.Count > 500) _liste.RemoveRange(0, _liste.Count - 500);
            try { File.WriteAllText(_chemin, JsonSerializer.Serialize(_liste)); } catch (Exception) { }
        }
        Change?.Invoke();
    }

    public List<EvenementSecurite> Recents(int n = 20)
    {
        lock (_verrou) return _liste.OrderByDescending(e => e.Date).Take(n).ToList();
    }

    public int Compter(string type, GraviteSecurite? g = null, int jours = 30)
    {
        lock (_verrou)
            return _liste.Count(e => e.Type == type && (g is null || e.Gravite == g) && e.Date > DateTime.Now.AddDays(-jours));
    }

    /// <summary>Score 0–100 : part de 100, moins des pénalités pour les alertes récentes ; bonus si PIN/biométrie actifs.</summary>
    public int Score(bool pinActif, bool biometrieActive)
    {
        int s = 70 + (pinActif ? 20 : 0) + (biometrieActive ? 10 : 0);
        lock (_verrou)
        {
            var d = DateTime.Now.AddDays(-30);
            foreach (var e in _liste.Where(e => e.Date > d))
                s -= e.Gravite == GraviteSecurite.Alerte ? 8 : e.Gravite == GraviteSecurite.Attention ? 3 : 0;
        }
        return Math.Clamp(s, 0, 100);
    }

    public static string Niveau(int score) => score >= 85 ? "Excellent" : score >= 65 ? "Bon" : score >= 40 ? "À surveiller" : "Faible";
}

/// <summary>Empreinte SHA-256 d'un fichier : l'expéditeur l'envoie, le destinataire la recalcule.</summary>
public static class IntegriteFichier
{
    public static string Calculer(string chemin)
    {
        using var f = File.OpenRead(chemin);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    /// <returns>true = intègre, false = modifié, null = l'expéditeur n'a pas fourni d'empreinte (ancienne version)</returns>
    public static bool? Verifier(string chemin, string attendu)
    {
        if (string.IsNullOrEmpty(attendu)) return null;
        try { return string.Equals(Calculer(chemin), attendu, StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }
}

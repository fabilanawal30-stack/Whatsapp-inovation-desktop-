using System.Net;
using System.Text.Json;

namespace TALKORA.Core;

/// <summary>
/// Traduction via l'API gratuite MyMemory (aucune clé nécessaire, environ 5000 caractères par jour).
/// ATTENTION : le texte est envoyé à un service externe. L'interface demande donc l'accord de l'utilisateur.
/// </summary>
public class TraducteurEnLigne : ITraducteur
{
    // MyMemory refuse les requêtes de plus de 500 caractères : on découpe
    public const int TailleMorceau = 450;

    private static readonly HttpClient ClientParDefaut = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _http;
    private readonly string _urlBase;

    public TraducteurEnLigne(HttpClient? http = null, string urlBase = "https://api.mymemory.translated.net/get")
    {
        _http = http ?? ClientParDefaut;
        _urlBase = urlBase;
    }

    public string Nom => "MyMemory (en ligne)";
    public bool EstHorsLigne => false;

    public async Task<string> TraduireAsync(string texte, string source, string cible, CancellationToken ct = default)
    {
        string depart = string.IsNullOrEmpty(source) ? "Autodetect" : source;
        var resultat = new List<string>();

        foreach (string morceau in Decouper(texte))
        {
            string url = $"{_urlBase}?q={Uri.EscapeDataString(morceau)}&langpair={depart}|{cible}";
            string json;
            try
            {
                using var reponse = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (reponse.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new TraductionException("Limite du service de traduction atteinte pour aujourd'hui.");
                reponse.EnsureSuccessStatusCode();
                json = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (TraductionException) { throw; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TraductionException("Le service de traduction ne répond pas (délai dépassé).");
            }
            catch (HttpRequestException ex)
            {
                throw new TraductionException("Impossible de joindre le service de traduction (pas d'Internet ?).", ex);
            }

            resultat.Add(LireReponse(json));
        }

        return string.Join(" ", resultat);
    }

    // Lit {"responseData":{"translatedText":"..."},"responseStatus":200}
    internal static string LireReponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var racine = doc.RootElement;

            int statut = 200;
            if (racine.TryGetProperty("responseStatus", out var s))
            {
                if (s.ValueKind == JsonValueKind.Number) statut = s.GetInt32();
                else if (s.ValueKind == JsonValueKind.String && int.TryParse(s.GetString(), out int n)) statut = n;
            }

            string texte = racine.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";

            // Quota dépassé : MyMemory répond parfois "200" avec un message d'avertissement à la place du texte
            if (texte.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                throw new TraductionException("Limite quotidienne du service de traduction atteinte.");

            if (statut != 200 || texte.Length == 0)
                throw new TraductionException("Le service n'a pas pu traduire ce message.");

            return WebUtility.HtmlDecode(texte);   // le service renvoie parfois &#39; au lieu de '
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TraductionException("Réponse inattendue du service de traduction.", ex);
        }
    }

    // Découpe un long texte en morceaux <= TailleMorceau, en coupant de préférence après une phrase, sinon à un espace
    public static List<string> Decouper(string texte)
    {
        var morceaux = new List<string>();
        string reste = texte.Trim();

        while (reste.Length > TailleMorceau)
        {
            string debut = reste.Substring(0, TailleMorceau);
            int coupe = debut.LastIndexOfAny(new[] { '.', '!', '?', '\n' });
            if (coupe < TailleMorceau / 3) coupe = debut.LastIndexOf(' ');
            if (coupe <= 0) coupe = TailleMorceau - 1;   // aucun espace : coupe brutale

            morceaux.Add(reste.Substring(0, coupe + 1).Trim());
            reste = reste.Substring(coupe + 1).Trim();
        }

        if (reste.Length > 0)
            morceaux.Add(reste);
        return morceaux;
    }
}

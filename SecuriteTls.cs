using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TALKORA.Core;

/// <summary>Mémoire des identités déjà vues : IP -> empreinte du certificat (SHA-256, en hexadécimal).</summary>
public interface IMagasinEmpreintes
{
    string? ObtenirEmpreinte(string ip);
    void EnregistrerEmpreinte(string ip, string empreinte);
    void OublierEmpreinte(string ip);
}

/// <summary>Magasin en mémoire (tests, ou secours si la base n'est pas disponible).</summary>
public class MagasinEmpreintesMemoire : IMagasinEmpreintes
{
    private readonly Dictionary<string, string> _d = new();
    public string? ObtenirEmpreinte(string ip) { lock (_d) return _d.GetValueOrDefault(ip); }
    public void EnregistrerEmpreinte(string ip, string e) { lock (_d) _d[ip] = e; }
    public void OublierEmpreinte(string ip) { lock (_d) _d.Remove(ip); }
}

/// <summary>
/// L'identité cryptographique de CE poste : un certificat auto-signé (ECDSA P-256) créé au premier
/// lancement et gardé dans DataTalkora/identite.pfx. Son empreinte sert de "carte d'identité".
/// </summary>
public sealed class IdentiteTls : IDisposable
{
    public X509Certificate2 Certificat { get; }
    public string Empreinte { get; }

    private IdentiteTls(X509Certificate2 cert)
    {
        Certificat = cert;
        Empreinte = EmpreinteDe(cert);
    }

    public static string EmpreinteDe(X509Certificate cert)
        => Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));

    /// <summary>Charge l'identité existante, ou en crée une nouvelle (fichier absent ou abîmé).</summary>
    public static IdentiteTls ChargerOuCreer(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        string chemin = Path.Combine(dossier, "identite.pfx");

        if (File.Exists(chemin))
        {
            try
            {
                var c = X509CertificateLoader.LoadPkcs12FromFile(chemin, null, X509KeyStorageFlags.Exportable);
                if (c.HasPrivateKey && c.NotAfter > DateTime.Now.AddDays(7))
                    return new IdentiteTls(c);
                c.Dispose();
            }
            catch (Exception ex) when (ex is CryptographicException or IOException) { }
        }

        using var cle = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requete = new CertificateRequest("CN=Talkora", cle, HashAlgorithmName.SHA256);
        using var brut = requete.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        byte[] pfx = brut.Export(X509ContentType.Pfx);

        // Écriture complète puis renommage : jamais de fichier à moitié écrit
        string tmp = chemin + ".tmp";
        File.WriteAllBytes(tmp, pfx);
        File.Move(tmp, chemin, overwrite: true);

        // On recharge depuis le PFX : sous Windows, c'est ce qui rend la clé utilisable par TLS
        return new IdentiteTls(X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable));
    }

    public void Dispose() => Certificat.Dispose();
}

/// <summary>
/// Chiffre les connexions (TLS 1.2/1.3) avec authentification mutuelle.
/// Pas d'autorité de certification en réseau local : on applique le principe "TOFU"
/// (Trust On First Use, comme SSH) — la première fois qu'on parle à une IP, on retient l'empreinte de
/// son certificat ; ensuite, si l'empreinte change, la connexion est REFUSÉE et l'utilisateur est prévenu.
/// </summary>
public class CanalSecurise
{
    private readonly IdentiteTls _identite;
    private readonly IMagasinEmpreintes _magasin;

    /// <summary>(ip, ancienneEmpreinte, nouvelleEmpreinte) — déclenché sur un thread d'arrière-plan.</summary>
    public event Action<string, string, string>? IdentiteChangee;

    public string MonEmpreinte => _identite.Empreinte;

    public CanalSecurise(IdentiteTls identite, IMagasinEmpreintes magasin)
    {
        _identite = identite;
        _magasin = magasin;
    }

    /// <summary>Côté qui reçoit la connexion.</summary>
    public async Task<SslStream> ServeurAsync(Stream brut, string ip, CancellationToken jeton)
    {
        var ssl = new SslStream(brut, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _identite.Certificat,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (s, cert, chaine, erreurs) => Valider(ip, cert)
            }, jeton);
            return ssl;
        }
        catch { ssl.Dispose(); throw; }
    }

    /// <summary>Côté qui ouvre la connexion.</summary>
    public async Task<SslStream> ClientAsync(Stream brut, string ip, CancellationToken jeton)
    {
        var ssl = new SslStream(brut, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "talkora",
                ClientCertificates = new X509CertificateCollection { _identite.Certificat },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (s, cert, chaine, erreurs) => Valider(ip, cert)
            }, jeton);
            return ssl;
        }
        catch { ssl.Dispose(); throw; }
    }

    // Les erreurs de chaîne sont normales (auto-signé) : seule l'empreinte compte
    private bool Valider(string ip, X509Certificate? cert)
    {
        if (cert is null || string.IsNullOrEmpty(ip))
            return false;
        try
        {
            string recue = IdentiteTls.EmpreinteDe(cert);
            string? connue = _magasin.ObtenirEmpreinte(ip);
            if (connue is null)
            {
                _magasin.EnregistrerEmpreinte(ip, recue);
                return true;
            }
            if (string.Equals(connue, recue, StringComparison.OrdinalIgnoreCase))
                return true;

            IdentiteChangee?.Invoke(ip, connue, recue);
            return false;
        }
        catch { return false; }   // un callback ne doit jamais lever d'exception
    }

    /// <summary>Texte affichable : "AB12 CD34 …" (pour comparer à voix haute avec le contact).</summary>
    public static string Formater(string empreinte)
    {
        string court = empreinte.Length > 32 ? empreinte[..32] : empreinte;
        return string.Join(" ", Enumerable.Range(0, court.Length / 4).Select(i => court.Substring(i * 4, 4)));
    }
}

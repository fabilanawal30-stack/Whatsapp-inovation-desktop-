using System.Text;

namespace TALKORA.Core;

/// <summary>
/// Étape 21 : faire en sorte que rien ne se perde quand un contact est hors ligne.
/// Tout repose sur un principe : on RENVOIE ce qui n'a pas été confirmé quand le contact revient, et le récepteur ignore les doublons
/// (les messages, actions et statuts ont un identifiant unique).
/// </summary>
public static class ServiceRenvoi
{
    public const int JoursMax = 30;
    public const int MessagesMax = 50;
    public const int TentativesMax = 20;          // une action en file d'attente est abandonnée après 20 essais
    public const int JoursFile = 14;              // …ou après 14 jours

    /// <summary>Mes messages (discussion directe) à ce contact qui n'ont pas été confirmés : à renvoyer dans l'ordre.</summary>
    public static List<MessageChat> MessagesARenvoyer(IEnumerable<MessageChat> messages, string ipContact,
        Func<MessageChat, bool> estMoi, DateTime maintenant)
        => messages
            .Where(m => !m.EstGroupe && m.Destinataire == ipContact && estMoi(m) && !m.EstAction && !m.Supprime
                        && m.Etat is EtatMessage.Echec or EtatMessage.EnAttente
                        && m.DateEnvoi >= maintenant.AddDays(-JoursMax)
                        && (m.ExpireLe is null || m.ExpireLe > maintenant)
                        && m.Type is TypeMessage.Texte or TypeMessage.Sondage or TypeMessage.Fichier or TypeMessage.Vocal)
            .OrderBy(m => m.DateEnvoi).Take(MessagesMax).ToList();

    /// <summary>Mes statuts encore valides, destinés à ce contact, dont il n'a pas confirmé la réception.</summary>
    public static List<Statut> StatutsARenvoyer(IEnumerable<Statut> statuts, string monIp, string ipContact, DateTime maintenant)
        => statuts.Where(s => s.AuteurIp == monIp && s.ExpireLe > maintenant
                              && s.Destinataires.Contains(ipContact) && !s.LivreA.Contains(ipContact)).ToList();

    /// <summary>Faut-il garder une action en file d'attente ? (pas trop d'essais, pas trop vieille)</summary>
    public static bool GarderSortant(int tentatives, DateTime cree, DateTime maintenant)
        => tentatives < TentativesMax && cree >= maintenant.AddDays(-JoursFile);

    /// <summary>
    /// Essaie une opération réseau plusieurs fois, avec des pauses (en ms) entre les essais. Vrai dès qu'un essai réussit.
    /// Une annulation n'est jamais avalée. Les erreurs d'un essai comptent comme un échec de cet essai.
    /// </summary>
    public static async Task<bool> EssayerAsync(Func<Task<bool>> tentative, int[] pausesMs, CancellationToken ct = default)
    {
        for (int i = 0; ; i++)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await tentative().ConfigureAwait(false)) return true; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException) { }
            if (i >= pausesMs.Length) return false;
            await Task.Delay(Math.Clamp(pausesMs[i], 0, 10_000), ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Où Talkora range ses données : à côté du programme en mode « développement », dans le dossier de l'utilisateur une fois installé.</summary>
public static class EmplacementDonnees
{
    public const string NomFlag = "installe.flag";
    public const string DossierApp = "Talkora";
    public const string DossierDonnees = "DataTalkora";

    /// <summary>
    /// Le dossier de travail à utiliser. Si « installe.flag » (écrit par l'installateur) est à côté du programme : %LOCALAPPDATA%\Talkora
    /// (un utilisateur normal n'a PAS le droit d'écrire dans « Program Files »). Sinon : le dossier du programme, comme avant.
    /// </summary>
    public static (string Dossier, bool Installe) Resoudre(string dossierProgramme, string localAppData)
    {
        bool installe = File.Exists(Path.Combine(dossierProgramme, NomFlag)) && !string.IsNullOrWhiteSpace(localAppData);
        return installe ? (Path.Combine(localAppData, DossierApp), true) : (dossierProgramme, false);
    }

    /// <summary>
    /// Reprend les données d'une ancienne version (DataTalkora à côté du programme) la première fois. Copie, ne déplace jamais.
    /// Renvoie vrai si une copie a été faite.
    /// </summary>
    public static bool MigrerAncienneBase(string dossierProgramme, string dossierTravail)
    {
        string ancien = Path.Combine(dossierProgramme, DossierDonnees);
        string nouveau = Path.Combine(dossierTravail, DossierDonnees);
        if (!Directory.Exists(ancien) || Directory.Exists(nouveau)) return false;
        if (Path.GetFullPath(ancien).TrimEnd('\\', '/') == Path.GetFullPath(nouveau).TrimEnd('\\', '/')) return false;
        Directory.CreateDirectory(dossierTravail);
        CopierDossier(ancien, nouveau);
        return true;
    }

    private static void CopierDossier(string source, string cible)
    {
        Directory.CreateDirectory(cible);
        foreach (string f in Directory.GetFiles(source))
        {
            if (Path.GetFileName(f).EndsWith("-wal") || Path.GetFileName(f).EndsWith("-shm")) continue;   // la base est déjà cohérente sans eux si Talkora était fermé
            File.Copy(f, Path.Combine(cible, Path.GetFileName(f)), overwrite: false);
        }
        foreach (string d in Directory.GetDirectories(source))
            CopierDossier(d, Path.Combine(cible, Path.GetFileName(d)));
    }
}

/// <summary>Quand Talkora plante, on garde une trace lisible (sans messages ni données personnelles) pour comprendre pourquoi.</summary>
public static class JournalErreurs
{
    public const int FichiersMax = 20;

    public static string? Ecrire(string dossierJournal, Exception ex, string version, DateTime maintenant)
    {
        try
        {
            Directory.CreateDirectory(dossierJournal);
            string chemin = Path.Combine(dossierJournal, $"erreur-{maintenant:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"Talkora {version} — {maintenant:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Système : {Environment.OSVersion} / .NET {Environment.Version}");
            sb.AppendLine();
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                sb.AppendLine($"{e.GetType().FullName}: {e.Message}");
                sb.AppendLine(e.StackTrace);
                sb.AppendLine();
            }
            File.WriteAllText(chemin, sb.ToString());

            foreach (string vieux in Directory.GetFiles(dossierJournal, "erreur-*.txt").OrderByDescending(f => f).Skip(FichiersMax))
                try { File.Delete(vieux); } catch (IOException) { }
            return chemin;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

namespace TALKORA.Core;

/// <summary>
/// Talkora Capsule : un message « scellé » qui ne s'affiche qu'à partir d'une date
/// (une lettre à un ami pour son anniversaire, ou une lettre à soi-même dans un an).
/// ATTENTION : le texte voyage et reste sur le disque du destinataire. Talkora le CACHE partout (liste, recherche, IA, copie)
/// jusqu'à la date, mais ce n'est pas du chiffrement : quelqu'un qui fouille la base peut le lire.
/// </summary>
public static class ServiceCapsule
{
    public const int LongueurMax = 2000;
    public static readonly TimeSpan DelaiMin = TimeSpan.FromMinutes(1);
    public const int AnneesMax = 10;

    public static DateTime LimiteMax(DateTime maintenant) => maintenant.AddYears(AnneesMax);

    /// <summary>Crée la capsule, ou null si le texte est vide/trop long ou si la date n'est pas entre « dans 1 minute » et 10 ans.</summary>
    public static MessageChat? Creer(ProfilUtilisateur moi, string destinataire, string texte, DateTime ouvreLe, DateTime maintenant)
    {
        texte = (texte ?? "").Trim();
        if (texte.Length == 0 || texte.Length > LongueurMax) return null;
        if (ouvreLe < maintenant + DelaiMin || ouvreLe > LimiteMax(maintenant)) return null;

        var m = MessageActions.Nouveau(moi, destinataire, texte);
        m.OuvreLe = ouvreLe;
        return m;
    }

    /// <summary>Les dates proposées dans la fenêtre « Capsule » (libellé, date).</summary>
    public static List<(string Libelle, DateTime Quand)> Choix(DateTime maintenant)
    {
        var j = maintenant.Date;
        return new()
        {
            ("Demain matin (8 h)", j.AddDays(1).AddHours(8)),
            ("Dans 1 semaine", j.AddDays(7).AddHours(8)),
            ("Dans 1 mois", j.AddMonths(1).AddHours(8)),
            ("Dans 6 mois", j.AddMonths(6).AddHours(8)),
            ("Dans 1 an", j.AddYears(1).AddHours(8)),
        };
    }

    /// <summary>« dans 3 jours », « dans 2 h »… pour montrer le temps qu'il reste.</summary>
    public static string TempsRestant(DateTime ouvreLe, DateTime maintenant)
    {
        var t = ouvreLe - maintenant;
        if (t <= TimeSpan.Zero) return "maintenant";
        if (t.TotalMinutes < 60) return $"dans {Math.Max(1, (int)t.TotalMinutes)} min";
        if (t.TotalHours < 48) return $"dans {(int)t.TotalHours} h";
        if (t.TotalDays < 60) return $"dans {(int)t.TotalDays} jours";
        if (t.TotalDays < 730) return $"dans {(int)(t.TotalDays / 30.4)} mois";
        return $"dans {(int)(t.TotalDays / 365.25)} ans";
    }

    /// <summary>Capsules reçues dont l'ouverture est entre deux instants (pour annoncer « 💌 elle s'ouvre »).</summary>
    public static List<MessageChat> OuvertesEntre(IEnumerable<MessageChat> messages, DateTime depuis, DateTime jusqua)
        => messages.Where(m => !m.Supprime && m.OuvreLe is DateTime d && d > depuis && d <= jusqua).ToList();

    /// <summary>Texte de la bulle : le contenu pour son auteur (avec rappel du scellé), la ligne « Capsule » pour les autres et pour une lettre à soi-même.</summary>
    public static string TexteBulle(MessageChat m, bool estMoi, bool aMoiMeme, DateTime maintenant)
    {
        if (!m.EstScellee(maintenant)) return m.CorpsAffiche(maintenant);
        if (estMoi && !aMoiMeme)
            return $"⏳ Capsule scellée jusqu'au {m.OuvreLe:dd/MM/yyyy} à {m.OuvreLe:HH:mm} ({TempsRestant(m.OuvreLe!.Value, maintenant)})\n{m.Contenu}";
        return m.CorpsAffiche(maintenant) + $"  ({TempsRestant(m.OuvreLe!.Value, maintenant)})";
    }
}

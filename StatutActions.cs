namespace TALKORA.Core;

/// <summary>
/// Les règles des statuts, sans réseau ni affichage.
/// Un statut est envoyé sous forme de message à chaque contact choisi ;
/// seuls les contacts connus (et non bloqués) peuvent en publier chez moi.
/// </summary>
public static class StatutActions
{
    public const int DureeHeures = 24;
    public const int LongueurMax = 700;
    public const int NombreCouleurs = 6;

    // ===================== CRÉATION =====================

    public static Statut? CreerTexte(ProfilUtilisateur moi, string texte, int couleur, IEnumerable<string> destinataires)
    {
        texte = texte.Trim();
        if (texte.Length == 0 || texte.Length > LongueurMax)
            return null;

        return Nouveau(moi, texte, couleur, destinataires, TypeStatut.Texte);
    }

    /// <summary>Statut image : le fichier doit déjà être rangé dans le dossier de Talkora.</summary>
    public static Statut CreerImage(ProfilUtilisateur moi, Guid id, string legende, string nomFichier,
                                    string cheminLocal, IEnumerable<string> destinataires)
    {
        legende = legende.Trim();
        if (legende.Length > LongueurMax) legende = legende[..LongueurMax];

        var statut = Nouveau(moi, legende, 0, destinataires, TypeStatut.Image);
        statut.Id = id;
        statut.NomFichier = nomFichier;
        statut.CheminLocal = cheminLocal;
        return statut;
    }

    private static Statut Nouveau(ProfilUtilisateur moi, string texte, int couleur,
                                  IEnumerable<string> destinataires, TypeStatut type) => new()
                                  {
                                      AuteurIp = moi.AdresseIp,
                                      AuteurNom = moi.Pseudo,
                                      Type = type,
                                      Texte = texte,
                                      CouleurFond = Math.Clamp(couleur, 0, NombreCouleurs - 1),
                                      DateCreation = DateTime.Now,
                                      ExpireLe = DateTime.Now.AddHours(DureeHeures),
                                      Destinataires = destinataires.Where(ip => ip != moi.AdresseIp).Distinct().ToList()
                                  };

    // ===================== MESSAGES À ENVOYER =====================

    public static MessageChat MessagePourStatut(Statut statut, ProfilUtilisateur moi, string destinataireIp) => new()
    {
        Id = statut.Id,                     // le statut garde le même identifiant partout
        Expediteur = moi.Pseudo,
        AdresseIpExpediteur = moi.AdresseIp,
        Destinataire = destinataireIp,
        Type = statut.Type == TypeStatut.Image ? TypeMessage.StatutImage : TypeMessage.StatutTexte,
        Contenu = statut.Texte,
        CouleurFond = statut.CouleurFond,
        NomFichier = statut.NomFichier,
        ExpireLe = statut.ExpireLe
    };

    public static MessageChat MessageVu(Statut statut, ProfilUtilisateur moi) => new()
    {
        Expediteur = moi.Pseudo,
        AdresseIpExpediteur = moi.AdresseIp,
        Destinataire = statut.AuteurIp,
        Type = TypeMessage.StatutVu,
        CibleId = statut.Id
    };

    public static MessageChat MessageSuppression(Statut statut, ProfilUtilisateur moi, string destinataireIp) => new()
    {
        Expediteur = moi.Pseudo,
        AdresseIpExpediteur = moi.AdresseIp,
        Destinataire = destinataireIp,
        Type = TypeMessage.StatutSupprime,
        CibleId = statut.Id
    };

    // ===================== RÉCEPTION =====================

    /// <summary>
    /// Un statut reçu. Ajouté à la liste s'il est valide ; sinon renvoie null.
    /// Pour une image, le réseau a déjà rangé le fichier (CheminLocal).
    /// </summary>
    public static Statut? Recevoir(List<Statut> statuts, MessageChat message, DateTime maintenant)
    {
        bool image = message.Type == TypeMessage.StatutImage;
        if (!image && message.Type != TypeMessage.StatutTexte)
            return null;

        if (statuts.Exists(s => s.Id == message.Id))
            return null;                                    // déjà reçu

        string texte = message.Contenu.Trim();
        if (texte.Length > LongueurMax) texte = texte[..LongueurMax];
        if (!image && texte.Length == 0)
            return null;
        if (image && message.CheminLocal.Length == 0)
            return null;

        // On ne fait pas confiance à la durée annoncée : 24 h au maximum, à partir de maintenant
        DateTime limite = maintenant.AddHours(DureeHeures);
        DateTime expire = message.ExpireLe is DateTime annonce && annonce < limite ? annonce : limite;
        if (expire <= maintenant)
            return null;

        var statut = new Statut
        {
            Id = message.Id,
            AuteurIp = message.AdresseIpExpediteur,       // l'IP vue par le réseau, pas celle écrite dans le message
            AuteurNom = message.Expediteur,
            Type = image ? TypeStatut.Image : TypeStatut.Texte,
            Texte = texte,
            CouleurFond = Math.Clamp(message.CouleurFond, 0, NombreCouleurs - 1),
            NomFichier = message.NomFichier,
            CheminLocal = message.CheminLocal,
            DateCreation = maintenant,
            ExpireLe = expire
        };

        statuts.Add(statut);
        return statut;
    }

    /// <summary>Quelqu'un dit avoir vu MON statut.</summary>
    public static bool AppliquerVu(List<Statut> statuts, MessageChat action, string monIp)
    {
        Statut? statut = statuts.Find(s => s.Id == action.CibleId && s.EstA(monIp));
        if (statut is null
            || !statut.Destinataires.Contains(action.AdresseIpExpediteur)   // il devait pouvoir le voir
            || statut.VuPar.Contains(action.AdresseIpExpediteur))
            return false;

        statut.VuPar.Add(action.AdresseIpExpediteur);
        return true;
    }

    /// <summary>Un contact retire son statut. Seul l'auteur peut le faire. Renvoie le statut retiré.</summary>
    public static Statut? AppliquerSuppression(List<Statut> statuts, MessageChat action, string monIp)
    {
        Statut? statut = statuts.Find(s => s.Id == action.CibleId);
        if (statut is null || statut.EstA(monIp) || statut.AuteurIp != action.AdresseIpExpediteur)
            return null;

        statuts.Remove(statut);
        return statut;
    }

    // ===================== OUTILS =====================

    /// <summary>Retire les statuts de plus de 24 h et les renvoie (pour effacer leurs images).</summary>
    public static List<Statut> Purger(List<Statut> statuts, DateTime maintenant)
    {
        var perimes = statuts.Where(s => s.ExpireLe <= maintenant).ToList();
        statuts.RemoveAll(s => s.ExpireLe <= maintenant);
        return perimes;
    }

    public static int NonVus(List<Statut> statuts, string monIp)
        => statuts.Count(s => !s.EstA(monIp) && !s.Vu);

    /// <summary>Un statut par auteur : mes statuts d'abord, puis les contacts (non vus en premier).</summary>
    public static List<IGrouping<string, Statut>> ParAuteur(List<Statut> statuts, string monIp)
        => statuts.GroupBy(s => s.AuteurIp)
                  .OrderByDescending(g => g.Key == monIp)
                  .ThenByDescending(g => g.Any(s => !s.Vu))
                  .ThenByDescending(g => g.Max(s => s.DateCreation))
                  .ToList();
}
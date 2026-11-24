namespace TALKORA.Core;

/// <summary>Où en est un message dans son voyage.</summary>
public enum EtatMessage
{
    EnAttente,
    Envoye,
    Recu,
    Lu,
    Echec
}

/// <summary>
/// Ce que contient le message. Certains types ne sont pas affichés :
/// ce sont des "actions" envoyées au contact pour modifier un message existant.
/// </summary>
public enum TypeMessage
{
    Texte,
    Fichier,
    Vocal,
    Reaction,      // action : ajouter / retirer un emoji sur le message CibleId
    Modification,  // action : remplacer le texte du message CibleId
    Suppression,   // action : supprimer le message CibleId
    Epinglage,     // action : épingler / désépingler le message CibleId
    GroupeMaj,     // action : création ou mise à jour d'un groupe (Contenu = le groupe en JSON)
    GroupeQuitter, // action : "je quitte le groupe" (Contenu = Id du groupe)
    Sondage,       // message affiché : une question et des options
    Vote,          // action : voter pour l'option Contenu du sondage CibleId
    StatutTexte,   // action : un contact publie un statut texte
    StatutImage,   // action : un contact publie un statut image (arrive avec son fichier)
    StatutVu,      // action : "j'ai vu ton statut" (CibleId)
    StatutSupprime,// action : un contact retire son statut (CibleId)
    AppelDemande,  // signal d'appel : "je t'appelle" (Contenu = SignalAppel en JSON)
    AppelAccepte,  // signal d'appel : "j'ai décroché"
    AppelRefuse,   // signal d'appel : "refusé" ou "occupé"
    AppelFin,      // signal d'appel : "j'ai raccroché"
    Ecrit,         // signal : "je suis en train d'écrire" (jamais stocké ni affiché)
    Avatar,        // signal : ma photo de profil (Contenu = « empreinte|image en base64 », vide = je retire ma photo) ; jamais stocké
    Together,      // espace partagé : des éléments à fusionner (Contenu = paquet JSON) ; jamais stocké ni affiché comme message
    Sync           // Talkora Sync : échange entre MES appareils (appairage, brouillons, messages...) ; jamais stocké ni affiché
}

public class MessageChat
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Qui écrit
    public string Expediteur { get; set; } = "";
    public string AdresseIpExpediteur { get; set; } = "";

    // À qui : l'IP d'un contact, ou le nom d'un groupe si EstGroupe est vrai
    public string Destinataire { get; set; } = "";
    public bool EstGroupe { get; set; }

    public TypeMessage Type { get; set; } = TypeMessage.Texte;
    public string Contenu { get; set; } = "";
    public DateTime DateEnvoi { get; set; } = DateTime.Now;
    public string LangueOriginale { get; set; } = "fr";
    public EtatMessage Etat { get; set; } = EtatMessage.EnAttente;

    // ----- Messages avancés -----
    public Guid? CibleId { get; set; }          // message visé par une action
    public Guid? ReponseAId { get; set; }       // "répondre à un message précis"
    public string ReponseExtrait { get; set; } = "";
    public bool Transfere { get; set; }
    public string SyncDe { get; set; } = "";      // Talkora Sync : Id de MON appareil qui envoie ce message (vide = message normal)
    public string PreuveSync { get; set; } = "";
    public string SyncIp { get; set; } = "";        // Talkora Sync : l'adresse que MON appareil se donne à lui-même (peut différer de celle vue par le réseau : VPN, cartes virtuelles)
    public string SyncOrigine { get; set; } = "";   // Talkora Sync (fichiers) : l'expéditeur d'origine (le réseau remplace AdresseIpExpediteur par celle de l'appareil)   // signature HMAC (clé partagée entre mes appareils appairés)
    public Ambiance Ambiance { get; set; }         // Talkora Emotion : douceur, joie, nostalgie, surprise (choisie par l'expéditeur)
    public bool Auto { get; set; }              // réponse automatique de l'IA (affichée 🤖, jamais re-répondue)
    public bool Modifie { get; set; }
    public bool Supprime { get; set; }
    public bool Epingle { get; set; }
    public DateTime? ExpireLe { get; set; }     // message temporaire

    // Capsule temporelle : message qui ne s'affiche qu'à partir de cette date (étape 18)
    public DateTime? OuvreLe { get; set; }
    public bool EstCapsule => OuvreLe is not null;
    public bool EstScellee(DateTime maintenant) => OuvreLe is DateTime d && d > maintenant && !Supprime;

    // IP de la personne -> emoji
    public Dictionary<string, string> Reactions { get; set; } = new();

    // Sondage : les options, et qui a voté quoi (IP -> numéro de l'option)
    public int CouleurFond { get; set; }        // statuts texte : numéro de couleur

    public List<string> OptionsSondage { get; set; } = new();
    public Dictionary<string, int> Votes { get; set; } = new();

    // Pseudos cités avec @
    public List<string> Mentions { get; set; } = new();

    // Fichiers et vocaux (étape suivante)
    public string NomFichier { get; set; } = "";
    public long TailleFichier { get; set; }
    public double DureeSecondes { get; set; }   // messages vocaux

    // Intégrité : empreinte SHA-256 envoyée ; résultat du contrôle (toujours recalculé à la réception)
    public string Sha256 { get; set; } = "";

    // Branche : sujet de discussion lancé à partir d'un message. Vide = fil principal ; sinon l'identifiant du message d'origine.
    public Guid BrancheId { get; set; }
    public string BrancheTitre { get; set; } = "";
    public bool? IntegriteOk { get; set; }

    // Où se trouve le fichier SUR CET ORDINATEUR (jamais envoyé : le réseau le remet à zéro)
    public string CheminLocal { get; set; } = "";

    // Un sticker est un fichier image dont le texte est ce marqueur (l'application le dessine en grand, sans bulle)
    public const string MarqueurSticker = "[[sticker]]";
    public bool EstSticker => Type == TypeMessage.Fichier && Contenu == MarqueurSticker;

    // Une action n'est jamais affichée dans la conversation
    public bool EstAction => Type is TypeMessage.Reaction or TypeMessage.Modification
                                  or TypeMessage.Suppression or TypeMessage.Epinglage
                                  or TypeMessage.GroupeMaj or TypeMessage.GroupeQuitter
                                  or TypeMessage.Vote
                                  or TypeMessage.StatutTexte or TypeMessage.StatutImage
                                  or TypeMessage.StatutVu or TypeMessage.StatutSupprime
                                  or TypeMessage.AppelDemande or TypeMessage.AppelAccepte
                                  or TypeMessage.AppelRefuse or TypeMessage.AppelFin
                                  or TypeMessage.Ecrit or TypeMessage.Avatar or TypeMessage.Together or TypeMessage.Sync;

    // Le texte du message SANS « [heure] expéditeur : » (c'est ce qu'on met dans la bulle)
    public string CorpsAffiche() => CorpsAffiche(DateTime.Now);

    public string CorpsAffiche(DateTime maintenant)
    {
        if (Supprime)
            return "🚫 message supprimé";

        // Capsule pas encore ouverte : jamais de contenu (ni aperçu, ni recherche, ni IA)
        if (EstScellee(maintenant))
            return $"🔒 Capsule — s'ouvre le {OuvreLe:dd/MM/yyyy} à {OuvreLe:HH:mm}";

        string corps = Type switch
        {
            TypeMessage.Fichier when EstSticker => "🏷 Sticker",
            TypeMessage.Fichier => $"📎 {NomFichier} ({FichierService.FormaterTaille(TailleFichier)})"
                                   + (Contenu.Length > 0 ? " — " + Contenu : ""),
            TypeMessage.Vocal => $"🎤 Message vocal ({OutilsAudio.FormaterDuree(DureeSecondes)})",
            TypeMessage.Sondage => "📊 " + Contenu + string.Concat(OptionsSondage.Select((option, i) =>
                                   $"\n      {i + 1}. {option}  —  {Votes.Values.Count(v => v == i)} vote(s)")),
            _ => Contenu
        };

        if (EstCapsule) corps = "💌 " + corps;
        if (Transfere) corps = "↪ Transféré — " + corps;
        if (ReponseAId is not null) corps = $"(en réponse à « {ReponseExtrait} ») " + corps;
        if (Modifie) corps += " (modifié)";
        if (Epingle) corps = "📌 " + corps;
        if (ExpireLe is not null) corps += "  ⏳";

        if (Reactions.Count > 0)
            corps += "  " + string.Concat(Reactions.Values);

        return corps;
    }

    public string VersTexte() => $"[{DateEnvoi:HH:mm}] {Expediteur} : " + CorpsAffiche();
}

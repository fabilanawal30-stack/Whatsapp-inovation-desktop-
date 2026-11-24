using System.Text.RegularExpressions;

namespace TALKORA.Core;

/// <summary>
/// Toutes les opérations sur les messages : création des actions à envoyer
/// et application des actions reçues. Ne fait ni réseau ni affichage.
/// </summary>
public static class MessageActions
{
    private const int LongueurExtrait = 40;

    // ===================== CRÉATION =====================

    public static MessageChat CreerReponse(MessageChat original, string texte, ProfilUtilisateur moi, string destinataire)
    {
        var message = Nouveau(moi, destinataire, texte);
        message.ReponseAId = original.Id;
        message.ReponseExtrait = Extrait(original.Contenu);
        return message;
    }

    public static MessageChat CreerTransfert(MessageChat original, ProfilUtilisateur moi, string destinataire)
    {
        var message = Nouveau(moi, destinataire, original.Contenu);
        message.Transfere = true;
        message.Type = original.Type;
        message.NomFichier = original.NomFichier;
        message.TailleFichier = original.TailleFichier;
        message.DureeSecondes = original.DureeSecondes;
        message.CheminLocal = original.CheminLocal;   // source à recopier avant l'envoi
        return message;
    }

    public static MessageChat CreerMessageTemporaire(ProfilUtilisateur moi, string destinataire, string texte, TimeSpan duree)
    {
        var message = Nouveau(moi, destinataire, texte);
        message.ExpireLe = DateTime.Now + duree;
        return message;
    }

    public static MessageChat CreerModification(MessageChat cible, string nouveauTexte, ProfilUtilisateur moi, string destinataire)
    {
        var action = Nouveau(moi, destinataire, nouveauTexte);
        action.Type = TypeMessage.Modification;
        action.CibleId = cible.Id;
        return action;
    }

    public static MessageChat CreerSuppression(MessageChat cible, ProfilUtilisateur moi, string destinataire)
    {
        var action = Nouveau(moi, destinataire, "");
        action.Type = TypeMessage.Suppression;
        action.CibleId = cible.Id;
        return action;
    }

    // Emoji vide = retirer ma réaction
    public static MessageChat CreerReaction(MessageChat cible, string emoji, ProfilUtilisateur moi, string destinataire)
    {
        var action = Nouveau(moi, destinataire, emoji);
        action.Type = TypeMessage.Reaction;
        action.CibleId = cible.Id;
        return action;
    }

    public static MessageChat CreerEpinglage(MessageChat cible, bool epingler, ProfilUtilisateur moi, string destinataire)
    {
        var action = Nouveau(moi, destinataire, epingler ? "1" : "0");
        action.Type = TypeMessage.Epinglage;
        action.CibleId = cible.Id;
        return action;
    }

    // ===================== APPLICATION =====================

    /// <summary>
    /// Applique une action sur la liste de messages. Renvoie vrai si un message a changé.
    /// L'IP de l'expéditeur est celle de la vraie connexion (ReseauService l'impose),
    /// donc on peut s'y fier pour vérifier qui a le droit de modifier.
    /// </summary>
    public static bool Appliquer(List<MessageChat> messages, MessageChat action)
    {
        if (!action.EstAction || action.CibleId is null)
            return false;

        MessageChat? cible = messages.Find(m => m.Id == action.CibleId);
        if (cible is null)
            return false;

        switch (action.Type)
        {
            case TypeMessage.Modification:
                if (cible.Supprime || cible.AdresseIpExpediteur != action.AdresseIpExpediteur)
                    return false;                       // on ne modifie que ses propres messages
                cible.Contenu = action.Contenu;
                cible.Modifie = true;
                return true;

            case TypeMessage.Suppression:
                if (cible.AdresseIpExpediteur != action.AdresseIpExpediteur)
                    return false;
                cible.Supprime = true;
                cible.Contenu = "";
                return true;

            case TypeMessage.Reaction:
                if (cible.Supprime)
                    return false;
                if (string.IsNullOrEmpty(action.Contenu))
                    cible.Reactions.Remove(action.AdresseIpExpediteur);
                else
                    cible.Reactions[action.AdresseIpExpediteur] = action.Contenu;
                return true;

            case TypeMessage.Epinglage:
                cible.Epingle = action.Contenu == "1";
                return true;

            case TypeMessage.Vote:
                if (cible.Type != TypeMessage.Sondage || cible.Supprime
                    || !int.TryParse(action.Contenu, out int option)
                    || option < 0 || option >= cible.OptionsSondage.Count)
                    return false;
                cible.Votes[action.AdresseIpExpediteur] = option;   // un seul vote par personne, modifiable
                return true;

            default:
                return false;
        }
    }

    // ===================== SONDAGES =====================

    public const int OptionsMin = 2, OptionsMax = 6, LongueurOptionMax = 100, LongueurQuestionMax = 300;

    /// <summary>Crée un sondage. Renvoie null si la question ou les options sont invalides.</summary>
    public static MessageChat? CreerSondage(ProfilUtilisateur moi, string destinataire, string question, IEnumerable<string> options)
    {
        question = question.Trim();
        var propres = options.Select(o => o.Trim()).Where(o => o.Length > 0)
                             .Select(o => o.Length > LongueurOptionMax ? o[..LongueurOptionMax] : o)
                             .Distinct().Take(OptionsMax).ToList();

        if (question.Length == 0 || question.Length > LongueurQuestionMax || propres.Count < OptionsMin)
            return null;

        var message = Nouveau(moi, destinataire, question);
        message.Type = TypeMessage.Sondage;
        message.OptionsSondage = propres;
        return message;
    }

    public static MessageChat CreerVote(MessageChat sondage, int option, ProfilUtilisateur moi, string destinataire)
    {
        var action = Nouveau(moi, destinataire, option.ToString());
        action.Type = TypeMessage.Vote;
        action.CibleId = sondage.Id;
        return action;
    }

    /// <summary>
    /// Un message reçu ne doit pas arriver avec des "décorations" déjà posées par l'expéditeur
    /// (réactions, épingle, votes...) : on repart d'un message propre.
    /// </summary>
    public static void AssainirRecu(MessageChat message)
    {
        message.Reactions = new Dictionary<string, string>();
        message.Votes = new Dictionary<string, int>();
        message.Epingle = false;
        message.Supprime = false;
        message.Modifie = false;
        message.BrancheTitre = ServiceBranches.NettoyerTitre(message.BrancheTitre);

        if (message.Type == TypeMessage.Sondage)
            message.OptionsSondage = message.OptionsSondage.Take(OptionsMax)
                .Select(o => o.Length > LongueurOptionMax ? o[..LongueurOptionMax] : o).ToList();
        else
            message.OptionsSondage = new List<string>();
    }

    // ===================== OUTILS =====================

    /// <summary>Supprime les messages temporaires périmés. Renvoie combien ont disparu.</summary>
    public static int PurgerExpires(List<MessageChat> messages)
        => messages.RemoveAll(m => m.ExpireLe is not null && m.ExpireLe <= DateTime.Now);

    /// <summary>Messages d'une conversation contenant un mot (sans tenir compte des majuscules).</summary>
    public static List<MessageChat> Rechercher(IEnumerable<MessageChat> messages, string mot)
    {
        if (string.IsNullOrWhiteSpace(mot))
            return new List<MessageChat>();

        return messages
            .Where(m => !m.EstAction && !m.Supprime && !m.EstScellee(DateTime.Now)     // une capsule fermée ne se laisse pas fouiller
                        && (m.Contenu.Contains(mot, StringComparison.CurrentCultureIgnoreCase)
                                || m.NomFichier.Contains(mot, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }

    /// <summary>Pseudos cités avec @ dans un texte : "salut @Alice et @Bob" -> Alice, Bob.</summary>
    public static List<string> ExtraireMentions(string texte)
        => Regex.Matches(texte, @"@(\w+)")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    /// <summary>Un message à soi-même : on se l'envoie sans passer par le réseau.</summary>
    public static MessageChat CreerNoteAMoiMeme(ProfilUtilisateur moi, string texte)
    {
        var message = Nouveau(moi, moi.AdresseIp, texte);
        message.Etat = EtatMessage.Recu;
        return message;
    }

    public static MessageChat Nouveau(ProfilUtilisateur moi, string destinataire, string texte) => new()
    {
        Expediteur = moi.Pseudo,
        AdresseIpExpediteur = moi.AdresseIp,
        Destinataire = destinataire,
        Contenu = texte,
        LangueOriginale = moi.ObtenirCodeLangue(),
        Mentions = ExtraireMentions(texte)
    };

    private static string Extrait(string texte)
        => texte.Length <= LongueurExtrait ? texte : texte[..LongueurExtrait] + "…";
}

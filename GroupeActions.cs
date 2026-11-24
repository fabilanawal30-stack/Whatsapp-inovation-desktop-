using System.Net;
using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Ce qu'a changé une action de groupe reçue.</summary>
public enum ResultatGroupe
{
    Ignore,    
    Cree,      // je viens d'être ajouté à un nouveau groupe
    Modifie,   // le groupe existant a changé
    Retire     // je ne fais plus partie du groupe (retiré par l'admin)
}


public static class GroupeActions
{
    public const int MembresMax = 256;
    public const int LongueurNomMax = 60;
    public const int LongueurDescriptionMax = 300;

    // ===================== CRÉATION DES MESSAGES =====================

    public static MessageChat CreerMiseAJour(Groupe groupe, ProfilUtilisateur moi, string destinataireIp)
    {
        // On n'envoie pas mes réglages personnels (qui j'ai bloqué)
        Groupe copie = Cloner(groupe);
        foreach (var membre in copie.Membres)
            membre.EstBloque = false;
        copie.Epingle = copie.Archive = copie.Muet = false;   // mes réglages de liste ne regardent pas les autres

        return new MessageChat
        {
            Expediteur = moi.Pseudo,
            AdresseIpExpediteur = moi.AdresseIp,
            Destinataire = destinataireIp,
            Type = TypeMessage.GroupeMaj,
            Contenu = JsonSerializer.Serialize(copie)
        };
    }

    /// <summary>Message "je quitte ce groupe".</summary>
    public static MessageChat CreerDepart(Groupe groupe, ProfilUtilisateur moi, string destinataireIp) => new()
    {
        Expediteur = moi.Pseudo,
        AdresseIpExpediteur = moi.AdresseIp,
        Destinataire = destinataireIp,
        Type = TypeMessage.GroupeQuitter,
        Contenu = groupe.Id.ToString()
    };

    public static Groupe Cloner(Groupe groupe)
        => JsonSerializer.Deserialize<Groupe>(JsonSerializer.Serialize(groupe))!;

    // ===================== RÈGLES =====================

    /// <summary>Cette personne a-t-elle le droit d'écrire dans ce groupe ?</summary>
    public static bool PeutEcrire(Groupe groupe, string ip)
        => groupe.EstMembre(ip) && (!groupe.SeulAdminEcrit || groupe.EstAdmin(ip));

    // ===================== APPLICATION D'UNE ACTION REÇUE =====================

    public static ResultatGroupe Appliquer(List<Groupe> groupes, MessageChat action, string monIp)
    {
        return action.Type switch
        {
            TypeMessage.GroupeMaj => AppliquerMiseAJour(groupes, action, monIp),
            TypeMessage.GroupeQuitter => AppliquerDepart(groupes, action),
            _ => ResultatGroupe.Ignore
        };
    }

    private static ResultatGroupe AppliquerMiseAJour(List<Groupe> groupes, MessageChat action, string monIp)
    {
        Groupe? recu = Lire(action.Contenu);
        if (recu is null)
            return ResultatGroupe.Ignore;

        // Seul l'administrateur désigné peut envoyer son groupe...
        if (recu.AdminIp != action.AdresseIpExpediteur)
            return ResultatGroupe.Ignore;

        // ...et si je connais déjà ce groupe, seul MON administrateur actuel peut le modifier
        int index = groupes.FindIndex(g => g.Id == recu.Id);
        if (index >= 0 && groupes[index].AdminIp != action.AdresseIpExpediteur)
            return ResultatGroupe.Ignore;

        if (!recu.EstMembre(monIp))
        {
            if (index < 0)
                return ResultatGroupe.Ignore;     // un groupe auquel je n'appartiens pas : on n'en veut pas

            groupes.RemoveAt(index);              // l'admin m'a retiré du groupe
            return ResultatGroupe.Retire;
        }

        // Les réglages de liste sont personnels : jamais ceux de l'administrateur
        recu.Epingle = recu.Archive = recu.Muet = false;

        if (index >= 0)
        {
            recu.Epingle = groupes[index].Epingle;
            recu.Archive = groupes[index].Archive;
            recu.Muet = groupes[index].Muet;
            groupes[index] = recu;
            return ResultatGroupe.Modifie;
        }

        groupes.Add(recu);
        return ResultatGroupe.Cree;
    }

    private static ResultatGroupe AppliquerDepart(List<Groupe> groupes, MessageChat action)
    {
        if (!Guid.TryParse(action.Contenu, out Guid id))
            return ResultatGroupe.Ignore;

        Groupe? groupe = groupes.Find(g => g.Id == id);
        if (groupe is null || !groupe.RetirerMembre(action.AdresseIpExpediteur))
            return ResultatGroupe.Ignore;

        // Si l'admin part, le plus ancien membre restant devient admin
        // (même règle partout : tout le monde arrive au même résultat)
        if (groupe.AdminIp == action.AdresseIpExpediteur && groupe.Membres.Count > 0)
            groupe.AdminIp = groupe.Membres[0].AdresseIp;

        if (groupe.Membres.Count == 0)
            groupes.Remove(groupe);

        return ResultatGroupe.Modifie;
    }

    // Lit et nettoie un groupe reçu (ne jamais faire confiance au contenu d'un message)
    private static Groupe? Lire(string json)
    {
        Groupe? groupe;
        try { groupe = JsonSerializer.Deserialize<Groupe>(json); }
        catch (JsonException) { return null; }

        if (groupe is null || groupe.Id == Guid.Empty || string.IsNullOrWhiteSpace(groupe.Nom))
            return null;

        groupe.Nom = Couper(groupe.Nom.Trim(), LongueurNomMax);
        groupe.Description = Couper(groupe.Description ?? "", LongueurDescriptionMax);

        var propres = new List<Contact>();
        foreach (var membre in groupe.Membres ?? new List<Contact>())
        {
            if (membre is null
                || !IPAddress.TryParse(membre.AdresseIp, out _)
                || membre.Port < 1 || membre.Port > 65535
                || propres.Exists(m => m.AdresseIp == membre.AdresseIp))
                continue;

            membre.Nom = Couper((membre.Nom ?? "").Trim(), LongueurNomMax);
            if (membre.Nom.Length == 0) membre.Nom = membre.AdresseIp;
            membre.EstBloque = false;
            propres.Add(membre);

            if (propres.Count >= MembresMax)
                break;
        }
        groupe.Membres = propres;

        // L'admin doit faire partie du groupe
        return groupe.EstMembre(groupe.AdminIp) ? groupe : null;
    }

    private static string Couper(string texte, int max) => texte.Length <= max ? texte : texte[..max];
}

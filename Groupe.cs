namespace TALKORA.Core;

/// <summary>
/// Un groupe de discussion : un nom et une liste de membres (identifiés par leur IP).
/// La liste des membres contient TOUT le monde, y compris moi : ainsi chaque
/// ordinateur a exactement la même copie du groupe.
/// </summary>
public class Groupe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "";
    public string Description { get; set; } = "";

    // IP de l'administrateur (celui qui a créé le groupe)
    public string AdminIp { get; set; } = "";

    // Mode "annonces" : seul l'administrateur peut écrire
    public bool SeulAdminEcrit { get; set; }

    // Réglages PERSONNELS (étape 14) : chacun les siens, jamais partagés
    public bool Epingle { get; set; }
    public bool Archive { get; set; }
    public bool Muet { get; set; }

    public DateTime DateCreation { get; set; } = DateTime.Now;

    public List<Contact> Membres { get; set; } = new();

    // Ajoute un membre, sauf s'il est déjà dans le groupe (même adresse IP)
    public bool AjouterMembre(Contact contact)
    {
        if (Membres.Exists(m => m.AdresseIp == contact.AdresseIp))
            return false;

        Membres.Add(contact);
        return true;
    }

    // Retire le membre qui a cette adresse IP ; renvoie vrai si quelqu'un a été retiré
    public bool RetirerMembre(string adresseIp)
        => Membres.RemoveAll(m => m.AdresseIp == adresseIp) > 0;

    public bool EstMembre(string adresseIp) => Membres.Exists(m => m.AdresseIp == adresseIp);

    public bool EstAdmin(string adresseIp) => AdminIp == adresseIp;

    public override string ToString() => $"👥 {Nom} ({Membres.Count})";
}

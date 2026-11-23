namespace TALKORA.Core;

public enum TypeStatut { Texte, Image }

/// <summary>
/// Un statut : un texte sur fond coloré, ou une image, visible 24 h par les contacts choisis.
/// Les statuts des autres et les miens sont rangés dans la même liste (statuts.json).
/// </summary>
public class Statut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AuteurIp { get; set; } = "";
    public string AuteurNom { get; set; } = "";
    public TypeStatut Type { get; set; } = TypeStatut.Texte;
    public string Texte { get; set; } = "";        // le texte, ou la légende de l'image
    public int CouleurFond { get; set; }           // numéro de couleur (0 à 5)
    public string NomFichier { get; set; } = "";
    public string CheminLocal { get; set; } = "";  // l'image, sur CET ordinateur
    public DateTime DateCreation { get; set; } = DateTime.Now;
    public DateTime ExpireLe { get; set; } = DateTime.Now.AddHours(24);

    // Mes statuts : à qui je l'ai envoyé, et qui l'a vu
    public List<string> Destinataires { get; set; } = new();
    public List<string> VuPar { get; set; } = new();
    public List<string> LivreA { get; set; } = new();   // les contacts dont on a la confirmation de réception (pour renvoyer aux autres quand ils reviennent)

    // Statuts des autres : l'ai-je déjà regardé ?
    public bool Vu { get; set; }

    public bool EstA(string ip) => AuteurIp == ip;
}

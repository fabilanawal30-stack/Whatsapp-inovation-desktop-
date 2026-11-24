using System.Net;
using System.Text.Json;

namespace TALKORA.Core;

public enum TypeAppel { Audio, Video }

public enum EtatAppel { Inactif, Sortant, Entrant, EnCours }

/// <summary>Comment un appel s'est terminé.</summary>
public enum FinAppel
{
    Raccroche,      // conversation normale terminée
    Refuse,         // l'autre (ou moi) a refusé
    Occupe,         // l'autre est déjà en appel
    PasDeReponse,   // personne n'a décroché à temps (appel sortant)
    Manque,         // je n'ai pas décroché (appel entrant)
    Injoignable     // machine éteinte, pas de Talkora, réseau coupé...
}

public record ResumeAppel(Contact Interlocuteur, TypeAppel Type, bool Sortant, FinAppel Raison, TimeSpan Duree);

/// <summary>Ce que contient le Contenu d'un signal d'appel.</summary>
public class SignalAppel
{
    public Guid IdAppel { get; set; }
    public bool Video { get; set; }
    public int PortUdp { get; set; }
    public string Raison { get; set; } = "";
}

/// <summary>
/// Le chef d'orchestre d'un appel 1 à 1. La signalisation (sonnerie, décrocher, raccrocher)
/// passe par les messages TCP habituels ; le son et l'image passent ensuite en UDP (SessionMedia).
/// Un seul appel à la fois : un deuxième appelant reçoit « occupé ».
/// </summary>
public sealed class GestionnaireAppels : IDisposable
{
    private readonly ReseauService _reseau;
    private readonly ProfilUtilisateur _moi;
    private readonly Func<string, Contact?> _trouverContact;
    private readonly object _verrou = new();

    private Guid _idAppel;
    private Contact? _interlocuteur;
    private TypeAppel _type;
    private bool _sortant;
    private int _portDistant;
    private DateTime? _debut;
    private SessionMedia? _media;
    private CancellationTokenSource? _delai;

    public EtatAppel Etat { get; private set; } = EtatAppel.Inactif;
    public Contact? Interlocuteur => _interlocuteur;
    public TypeAppel Type => _type;
    public SessionMedia? Media => _media;

    /// <summary>Combien de temps ça sonne avant d'abandonner.</summary>
    public TimeSpan DelaiSonnerie { get; set; } = TimeSpan.FromSeconds(30);

    // Ces événements peuvent arriver sur n'importe quel thread : l'interface utilise BeginInvoke.
    public event Action<Contact, TypeAppel>? AppelEntrant;
    public event Action<SessionMedia>? AppelDemarre;       // la conversation commence
    public event Action<ResumeAppel>? AppelTermine;

    public GestionnaireAppels(ReseauService reseau, ProfilUtilisateur moi, Func<string, Contact?> trouverContact)
    {
        _reseau = reseau;
        _moi = moi;
        _trouverContact = trouverContact;
    }

    // ===================== CE QUE FAIT L'UTILISATEUR =====================

    /// <summary>Appelle un contact. Faux si un appel est déjà en cours ou si le contact est injoignable.</summary>
    public async Task<bool> AppelerAsync(Contact contact, TypeAppel type)
    {
        SessionMedia media;
        Guid id;

        lock (_verrou)
        {
            if (Etat != EtatAppel.Inactif || contact.EstBloque)
                return false;

            id = Guid.NewGuid();
            media = new SessionMedia(id);
            _idAppel = id;
            _media = media;
            _interlocuteur = contact;
            _type = type;
            _sortant = true;
            _debut = null;
            Etat = EtatAppel.Sortant;
        }

        var signal = new SignalAppel { IdAppel = id, Video = type == TypeAppel.Video, PortUdp = media.PortLocal };
        bool envoye = await _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelDemande, contact, signal));

        if (!envoye)
        {
            Terminer(id, FinAppel.Injoignable);
            return false;
        }

        LancerDelai(id);
        return true;
    }

    public async Task AccepterAsync()
    {
        SessionMedia media;
        Contact contact;
        Guid id;
        IPAddress ip;
        int port;

        lock (_verrou)
        {
            if (Etat != EtatAppel.Entrant || _interlocuteur is null || !IPAddress.TryParse(_interlocuteur.AdresseIp, out ip!))
                return;

            id = _idAppel;
            contact = _interlocuteur;
            port = _portDistant;
            _delai?.Cancel();
            media = new SessionMedia(id);
            _media = media;
            Etat = EtatAppel.EnCours;
            _debut = DateTime.Now;
            media.Demarrer(ip, port);
        }

        AppelDemarre?.Invoke(media);

        var reponse = new SignalAppel { IdAppel = id, PortUdp = media.PortLocal };
        if (!await _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelAccepte, contact, reponse)))
            Terminer(id, FinAppel.Injoignable);
    }

    public async Task RefuserAsync()
    {
        Contact? contact;
        Guid id;
        lock (_verrou)
        {
            if (Etat != EtatAppel.Entrant) return;
            contact = _interlocuteur;
            id = _idAppel;
        }

        Terminer(id, FinAppel.Refuse);
        if (contact is not null)
            await _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelRefuse, contact, new SignalAppel { IdAppel = id }));
    }

    /// <summary>Raccroche (ou annule un appel qui sonne encore).</summary>
    public async Task RaccrocherAsync()
    {
        Contact? contact;
        Guid id;
        lock (_verrou)
        {
            if (Etat is EtatAppel.Inactif or EtatAppel.Entrant) return;
            contact = _interlocuteur;
            id = _idAppel;
        }

        bool avaitDecroche = Etat == EtatAppel.EnCours;
        Terminer(id, avaitDecroche ? FinAppel.Raccroche : FinAppel.PasDeReponse);
        if (contact is not null)
            await _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelFin, contact, new SignalAppel { IdAppel = id }));
    }

    // ===================== CE QUE REÇOIT LE RÉSEAU =====================

    /// <summary>À appeler pour chaque message de type Appel* reçu.</summary>
    public void TraiterSignal(MessageChat message)
    {
        // Seuls mes contacts (non bloqués) peuvent m'appeler
        Contact? contact = _trouverContact(message.AdresseIpExpediteur);
        if (contact is null || contact.EstBloque)
            return;

        SignalAppel? signal;
        try { signal = JsonSerializer.Deserialize<SignalAppel>(message.Contenu); }
        catch (JsonException) { return; }
        if (signal is null || signal.IdAppel == Guid.Empty)
            return;

        switch (message.Type)
        {
            case TypeMessage.AppelDemande: RecevoirDemande(contact, signal); break;
            case TypeMessage.AppelAccepte: RecevoirAcceptation(contact, signal); break;
            case TypeMessage.AppelRefuse: RecevoirRefus(contact, signal); break;
            case TypeMessage.AppelFin: RecevoirFin(contact, signal); break;
        }
    }

    private void RecevoirDemande(Contact contact, SignalAppel signal)
    {
        if (signal.PortUdp < 1 || signal.PortUdp > 65535 || !IPAddress.TryParse(contact.AdresseIp, out _))
            return;

        lock (_verrou)
        {
            if (Etat != EtatAppel.Inactif)
            {
                if (signal.IdAppel == _idAppel)
                    return;      // la même demande reçue deux fois

                // Déjà en appel : on répond « occupé » sans déranger l'utilisateur
                _ = _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelRefuse, contact,
                        new SignalAppel { IdAppel = signal.IdAppel, Raison = "occupé" }));
                return;
            }

            _idAppel = signal.IdAppel;
            _interlocuteur = contact;
            _type = signal.Video ? TypeAppel.Video : TypeAppel.Audio;
            _sortant = false;
            _portDistant = signal.PortUdp;
            _debut = null;
            _media = null;
            Etat = EtatAppel.Entrant;
        }

        LancerDelai(signal.IdAppel);
        AppelEntrant?.Invoke(contact, signal.Video ? TypeAppel.Video : TypeAppel.Audio);
    }

    private void RecevoirAcceptation(Contact contact, SignalAppel signal)
    {
        SessionMedia media;
        lock (_verrou)
        {
            if (!EstCetAppel(contact, signal) || Etat != EtatAppel.Sortant || _media is null
                || signal.PortUdp < 1 || signal.PortUdp > 65535
                || !IPAddress.TryParse(contact.AdresseIp, out IPAddress? ip))
                return;

            _delai?.Cancel();
            media = _media;
            Etat = EtatAppel.EnCours;
            _debut = DateTime.Now;
            media.Demarrer(ip, signal.PortUdp);
        }

        AppelDemarre?.Invoke(media);
    }

    private void RecevoirRefus(Contact contact, SignalAppel signal)
    {
        lock (_verrou)
        {
            if (!EstCetAppel(contact, signal) || Etat != EtatAppel.Sortant) return;
        }
        Terminer(signal.IdAppel, signal.Raison == "occupé" ? FinAppel.Occupe : FinAppel.Refuse);
    }

    private void RecevoirFin(Contact contact, SignalAppel signal)
    {
        FinAppel raison;
        lock (_verrou)
        {
            if (!EstCetAppel(contact, signal) || Etat == EtatAppel.Inactif) return;

            raison = Etat switch
            {
                EtatAppel.Entrant => FinAppel.Manque,        // il a raccroché avant que je décroche
                EtatAppel.Sortant => FinAppel.Refuse,
                _ => FinAppel.Raccroche
            };
        }
        Terminer(signal.IdAppel, raison);
    }

    // Le signal vient-il bien de la personne et de l'appel en cours ? (à appeler sous verrou)
    private bool EstCetAppel(Contact contact, SignalAppel signal)
        => _interlocuteur is not null && signal.IdAppel == _idAppel
           && contact.AdresseIp == _interlocuteur.AdresseIp;

    // ===================== FIN D'APPEL =====================

    private void LancerDelai(Guid id)
    {
        CancellationTokenSource delai;
        lock (_verrou)
        {
            _delai?.Cancel();
            delai = _delai = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(DelaiSonnerie, delai.Token); }
            catch (OperationCanceledException) { return; }

            Contact? contact;
            FinAppel raison;
            lock (_verrou)
            {
                if (_idAppel != id || Etat is EtatAppel.Inactif or EtatAppel.EnCours) return;
                contact = _interlocuteur;
                raison = Etat == EtatAppel.Sortant ? FinAppel.PasDeReponse : FinAppel.Manque;
            }

            Terminer(id, raison);
            if (contact is not null)
                await _reseau.EnvoyerMessageAsync(contact, CreerSignal(TypeMessage.AppelFin, contact, new SignalAppel { IdAppel = id }));
        });
    }

    private void Terminer(Guid id, FinAppel raison)
    {
        ResumeAppel resume;
        lock (_verrou)
        {
            if (Etat == EtatAppel.Inactif || _idAppel != id || _interlocuteur is null)
                return;

            TimeSpan duree = _debut is DateTime debut ? DateTime.Now - debut : TimeSpan.Zero;
            resume = new ResumeAppel(_interlocuteur, _type, _sortant, raison, duree);

            _delai?.Cancel();
            _media?.Dispose();
            _media = null;
            _debut = null;
            Etat = EtatAppel.Inactif;
        }

        AppelTermine?.Invoke(resume);
    }

    private MessageChat CreerSignal(TypeMessage type, Contact contact, SignalAppel signal) => new()
    {
        Expediteur = _moi.Pseudo,
        AdresseIpExpediteur = _moi.AdresseIp,
        Destinataire = contact.AdresseIp,
        Type = type,
        Contenu = JsonSerializer.Serialize(signal)
    };

    public void Dispose()
    {
        lock (_verrou)
        {
            _delai?.Cancel();
            _media?.Dispose();
            _media = null;
            Etat = EtatAppel.Inactif;
        }
    }
}

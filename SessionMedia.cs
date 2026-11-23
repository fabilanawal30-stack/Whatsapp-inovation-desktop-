using System.Net;
using System.Net.Sockets;

namespace TALKORA.Core;

/// <summary>Constantes du son d'un appel : voix mono, 16 000 échantillons/s, 16 bits, paquets de 20 ms.</summary>
public static class FormatAudio
{
    public const int Frequence = 16000;
    public const int Bits = 16;
    public const int Canaux = 1;
    public const int OctetsPar20ms = Frequence * Bits / 8 * Canaux / 50;   // 640
}

/// <summary>
/// Le tuyau du son et de l'image d'un appel : un socket UDP, qui n'accepte que les paquets
/// venant de l'autre personne (même IP) et portant le bon identifiant d'appel.
/// </summary>
public sealed class SessionMedia : IDisposable
{
    private readonly Guid _idAppel;
    private readonly UdpClient _udp;
    private readonly ReassembleurVideo _reassembleur = new();
    private readonly object _verrouEnvoi = new();
    private IPEndPoint? _distant;
    private uint _sequence;
    private uint _idImage;
    private bool _arrete;

    public int PortLocal { get; }
    public long PaquetsRecus { get; private set; }
    public long PaquetsEnvoyes { get; private set; }

    // Déclenchés sur un thread d'arrière-plan : l'interface doit passer par BeginInvoke
    public event Action<byte[]>? AudioRecu;
    public event Action<byte[]>? ImageRecue;

    public SessionMedia(Guid idAppel)
    {
        _idAppel = idAppel;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));   // port libre choisi par Windows
        _udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;           // une grosse image arrive d'un coup : grand tampon
        _udp.Client.SendBufferSize = 1024 * 1024;
        PortLocal = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    }

    /// <summary>Commence à envoyer et à recevoir, avec l'adresse de l'autre personne.</summary>
    public void Demarrer(IPAddress ipDistante, int portDistant)
    {
        _distant = new IPEndPoint(ipDistante.MapToIPv4(), portDistant);
        _ = BoucleReceptionAsync();
    }

    // ===================== ENVOI =====================

    /// <summary>Envoie un morceau de son (PCM 16 bits).</summary>
    public void EnvoyerAudio(byte[] pcm)
    {
        if (_distant is null || _arrete || pcm.Length == 0)
            return;

        for (int debut = 0; debut < pcm.Length; debut += PaquetMedia.DonneesMax)
        {
            int longueur = Math.Min(PaquetMedia.DonneesMax, pcm.Length - debut);
            longueur -= longueur % 2;                  // jamais au milieu d'un échantillon de 2 octets
            if (longueur <= 0) break;
            Envoyer(PaquetMedia.Encoder(_idAppel, TypePaquet.Audio, ProchaineSequence(), 0, 0, 1,
                                        pcm.AsSpan(debut, longueur)));
        }
    }

    /// <summary>Envoie une image JPEG (coupée en morceaux). Faux si elle est trop grosse.</summary>
    public bool EnvoyerImage(byte[] jpeg)
    {
        if (_distant is null || _arrete)
            return false;

        var morceaux = ReassembleurVideo.Fragmenter(jpeg);
        if (morceaux is null)
            return false;

        uint idImage;
        lock (_verrouEnvoi) idImage = ++_idImage;

        for (int i = 0; i < morceaux.Count; i++)
        {
            Envoyer(PaquetMedia.Encoder(_idAppel, TypePaquet.Video, ProchaineSequence(), idImage,
                                        i, morceaux.Count, morceaux[i]));

            // Petite pause toutes les 16 paquets : on n'inonde pas le réseau ni le récepteur
            if ((i + 1) % 16 == 0)
                Thread.Sleep(1);
        }
        return true;
    }

    private uint ProchaineSequence()
    {
        lock (_verrouEnvoi) return ++_sequence;
    }

    private void Envoyer(byte[] paquet)
    {
        try
        {
            lock (_verrouEnvoi)
            {
                if (_arrete || _distant is null) return;
                _udp.Send(paquet, paquet.Length, _distant);
                PaquetsEnvoyes++;
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // l'autre poste ne répond plus : l'appel sera coupé par la signalisation
        }
    }

    // ===================== RÉCEPTION =====================

    private async Task BoucleReceptionAsync()
    {
        while (!_arrete)
        {
            UdpReceiveResult recu;
            try
            {
                recu = await _udp.ReceiveAsync();
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (_arrete) return; continue; }   // ex. "port injoignable" : on continue

            // Seule l'autre personne a le droit de nous envoyer du son/image
            if (_distant is null || !recu.RemoteEndPoint.Address.MapToIPv4().Equals(_distant.Address))
                continue;
            if (!PaquetMedia.Decoder(recu.Buffer, out PaquetDecode p) || p.IdAppel != _idAppel)
                continue;

            PaquetsRecus++;

            if (p.Type == TypePaquet.Audio)
            {
                AudioRecu?.Invoke(p.Donnees);
            }
            else
            {
                byte[]? image = _reassembleur.Ajouter(p.IdImage, p.Fragment, p.NombreFragments, p.Donnees);
                if (image is not null)
                    ImageRecue?.Invoke(image);
            }
        }
    }

    public void Dispose()
    {
        _arrete = true;
        try { _udp.Dispose(); } catch (SocketException) { }
    }
}

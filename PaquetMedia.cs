using System.Buffers.Binary;

namespace TALKORA.Core;

public enum TypePaquet : byte { Audio = 1, Video = 2 }

/// <summary>Un paquet décodé (son ou morceau d'image).</summary>
public readonly record struct PaquetDecode(
    Guid IdAppel, TypePaquet Type, uint Sequence, uint IdImage, int Fragment, int NombreFragments, byte[] Donnees);

/// <summary>
/// Format des paquets UDP d'un appel. En-tête de 29 octets :
/// IdAppel (16) | type (1) | numéro de paquet (4) | numéro d'image (4) | fragment (2) | nb de fragments (2)
/// L'IdAppel sert de "mot de passe" : un paquet qui ne le porte pas est jeté.
/// </summary>
public static class PaquetMedia
{
    public const int TailleEntete = 29;
    public const int DonneesMax = 1100;     // reste sous la taille d'un paquet Ethernet (1500 octets)

    public static byte[] Encoder(Guid idAppel, TypePaquet type, uint sequence, uint idImage,
                                 int fragment, int nombreFragments, ReadOnlySpan<byte> donnees)
    {
        var paquet = new byte[TailleEntete + donnees.Length];
        idAppel.TryWriteBytes(paquet.AsSpan(0, 16));
        paquet[16] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(paquet.AsSpan(17, 4), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(paquet.AsSpan(21, 4), idImage);
        BinaryPrimitives.WriteUInt16BigEndian(paquet.AsSpan(25, 2), (ushort)fragment);
        BinaryPrimitives.WriteUInt16BigEndian(paquet.AsSpan(27, 2), (ushort)nombreFragments);
        donnees.CopyTo(paquet.AsSpan(TailleEntete));
        return paquet;
    }

    /// <summary>Décode un paquet reçu. Renvoie faux s'il est trop court, inconnu ou incohérent.</summary>
    public static bool Decoder(ReadOnlySpan<byte> brut, out PaquetDecode paquet)
    {
        paquet = default;
        if (brut.Length < TailleEntete || brut.Length > TailleEntete + 1500)
            return false;

        byte type = brut[16];
        if (type != (byte)TypePaquet.Audio && type != (byte)TypePaquet.Video)
            return false;

        int fragment = BinaryPrimitives.ReadUInt16BigEndian(brut.Slice(25, 2));
        int nombre = BinaryPrimitives.ReadUInt16BigEndian(brut.Slice(27, 2));
        if (nombre < 1 || fragment >= nombre)
            return false;

        paquet = new PaquetDecode(
            new Guid(brut.Slice(0, 16)),
            (TypePaquet)type,
            BinaryPrimitives.ReadUInt32BigEndian(brut.Slice(17, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(brut.Slice(21, 4)),
            fragment, nombre,
            brut.Slice(TailleEntete).ToArray());
        return true;
    }
}

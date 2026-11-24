using System.Text;

namespace TALKORA.Core;

/// <summary>Outils sur les fichiers WAV : durée, lecture accélérée, affichage.</summary>
public static class OutilsAudio
{
    public static string FormaterDuree(double secondes)
    {
        int total = (int)Math.Round(Math.Max(0, secondes));
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Durée en secondes d'un fichier WAV (0 si le fichier est illisible).</summary>
    public static double DureeWav(string chemin)
    {
        try
        {
            byte[] wav = File.ReadAllBytes(chemin);
            if (!LireEntete(wav, out int fmt, out int tailleDonnees))
                return 0;

            int octetsParSeconde = BitConverter.ToInt32(wav, fmt + 8);
            return octetsParSeconde <= 0 ? 0 : (double)tailleDonnees / octetsParSeconde;
        }
        catch (IOException) { return 0; }
    }

    /// <summary>
    /// Crée une copie du WAV qui se lit plus vite : on annonce une fréquence d'échantillonnage
    /// plus élevée dans l'en-tête, sans toucher aux données. La voix devient un peu plus aiguë.
    /// </summary>
    public static void CreerCopieAcceleree(string source, string destination, double facteur)
    {
        byte[] wav = File.ReadAllBytes(source);
        if (!LireEntete(wav, out int fmt, out _))
            throw new InvalidDataException("Fichier WAV invalide.");

        int frequence = BitConverter.ToInt32(wav, fmt + 4);
        int octetsParSeconde = BitConverter.ToInt32(wav, fmt + 8);

        BitConverter.GetBytes((int)Math.Round(frequence * facteur)).CopyTo(wav, fmt + 4);
        BitConverter.GetBytes((int)Math.Round(octetsParSeconde * facteur)).CopyTo(wav, fmt + 8);

        File.WriteAllBytes(destination, wav);
    }

    // Un WAV = "RIFF" + une suite de blocs (chunks). On cherche "fmt " (format) et "data" (le son).
    private static bool LireEntete(byte[] wav, out int positionFmt, out int tailleDonnees)
    {
        positionFmt = -1;
        tailleDonnees = 0;

        if (wav.Length < 44
            || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            return false;

        int position = 12;
        while (position + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, position, 4);
            int taille = BitConverter.ToInt32(wav, position + 4);
            if (taille < 0) break;

            if (id == "fmt " && position + 8 + 16 <= wav.Length)
                positionFmt = position + 8;
            else if (id == "data")
            {
                tailleDonnees = Math.Min(taille, wav.Length - (position + 8));
                break;
            }

            position += 8 + taille + (taille & 1);
        }

        return positionFmt >= 0 && tailleDonnees > 0;
    }
}
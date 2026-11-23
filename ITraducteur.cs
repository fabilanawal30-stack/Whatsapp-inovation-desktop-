namespace TALKORA.Core;

/// <summary>Une erreur de traduction expliquée en français (réseau coupé, quota, modèle absent...).</summary>
public class TraductionException : Exception
{
    public TraductionException(string message, Exception? interne = null) : base(message, interne) { }
}

/// <summary>
/// Un moteur de traduction. Il y en a deux : en ligne (MyMemory) et hors ligne (Tali).
/// </summary>
public interface ITraducteur
{
    string Nom { get; }

    // true : le texte ne quitte jamais l'ordinateur
    bool EstHorsLigne { get; }

    // source = "" signifie "langue inconnue" ; cible est toujours un code valide ("fr", "en"...)
    Task<string> TraduireAsync(string texte, string source, string cible, CancellationToken ct = default);
}

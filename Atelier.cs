using System.Text.Json;

namespace TALKORA.Core;

/// <summary>Un morceau de code enregistré dans l'atelier.</summary>
public sealed class ExtraitCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titre { get; set; } = "Sans titre";
    public string Langage { get; set; } = "C#";
    public string Code { get; set; } = "";
    public DateTime Modifie { get; set; } = DateTime.Now;
}

/// <summary>Carnet de code local : DataTalkora/atelier.json. Rien ne sort de l'ordinateur, sauf si on partage.</summary>
public sealed class AtelierCode
{
    public static readonly string[] Langages = { "C#", "Python", "C", "JavaScript", "PHP", "SQL", "HTML", "VBA", "Autre" };

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["C#"] = ".cs", ["Python"] = ".py", ["C"] = ".c", ["JavaScript"] = ".js", ["PHP"] = ".php",
        ["SQL"] = ".sql", ["HTML"] = ".html", ["VBA"] = ".bas", ["Autre"] = ".txt"
    };

    private readonly string _chemin;
    public List<ExtraitCode> Extraits { get; private set; } = new();

    public AtelierCode(string dossier = "DataTalkora")
    {
        Directory.CreateDirectory(dossier);
        _chemin = Path.Combine(dossier, "atelier.json");
        try { if (File.Exists(_chemin)) Extraits = JsonSerializer.Deserialize<List<ExtraitCode>>(File.ReadAllText(_chemin)) ?? new(); }
        catch (Exception) { Extraits = new(); }
    }

    public void Enregistrer(ExtraitCode e)
    {
        e.Modifie = DateTime.Now;
        int i = Extraits.FindIndex(x => x.Id == e.Id);
        if (i >= 0) Extraits[i] = e; else Extraits.Add(e);
        Sauver();
    }

    public void Supprimer(Guid id) { Extraits.RemoveAll(x => x.Id == id); Sauver(); }

    private void Sauver()
    {
        string tmp = _chemin + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Extraits.OrderByDescending(x => x.Modifie).ToList()));
        File.Move(tmp, _chemin, overwrite: true);
        Extraits = Extraits.OrderByDescending(x => x.Modifie).ToList();
    }

    /// <summary>Écrit l'extrait dans un vrai fichier (ex. « Tri.py ») pour le partager comme n'importe quel fichier.</summary>
    public static string ExporterFichier(ExtraitCode e, string dossier)
    {
        Directory.CreateDirectory(dossier);
        string nom = new string((string.IsNullOrWhiteSpace(e.Titre) ? "code" : e.Titre.Trim())
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c).ToArray());
        string ext = Extensions.TryGetValue(e.Langage, out var x) ? x : ".txt";
        string chemin = Path.Combine(dossier, nom + ext);
        File.WriteAllText(chemin, e.Code);
        return chemin;
    }

    public static string LangagePourExtension(string fichier) => Path.GetExtension(fichier).ToLowerInvariant() switch
    {
        ".cs" => "C#", ".py" => "Python", ".c" => "C", ".js" => "JavaScript", ".php" => "PHP",
        ".sql" => "SQL", ".html" or ".htm" => "HTML", ".bas" or ".vba" => "VBA", _ => "Autre"
    };
}

public sealed record Chapitre(string Titre, string Icone, string Texte);

/// <summary>« L'ordinateur et ses secrets » : un petit livre lu hors ligne.</summary>
public static class LivreInformatique
{
    public const string Titre = "L'ordinateur et ses secrets";

    public static readonly IReadOnlyList<Chapitre> Chapitres = new List<Chapitre>
    {
        new("Ce qu'il y a dans la boîte", "🖥",
@"Un ordinateur est un assemblage de pièces qui ont chacune un rôle.

• Le processeur (CPU) est le cerveau : il exécute les instructions, des milliards de fois par seconde.
• La mémoire vive (RAM) est la table de travail : elle garde ce qu'on utilise MAINTENANT. Quand on éteint, elle s'efface.
• Le disque (SSD ou disque dur) est l'armoire : il garde tout, même éteint.
• La carte mère relie toutes les pièces entre elles.
• La carte graphique dessine l'image (les jeux, la vidéo, l'IA l'utilisent beaucoup).
• L'alimentation transforme le courant de la prise en courant adapté aux pièces.

Le secret : tout ce que l'ordinateur sait faire se résume à des 0 et des 1. Un 1 = « il y a du courant », un 0 = « il n'y en a pas ». Les photos, la musique et ce texte ne sont que des suites de 0 et de 1."),

        new("Le système d'exploitation", "⚙",
@"Le système d'exploitation (Windows, Linux, macOS, Android) est le chef d'orchestre.

Il fait trois grandes choses :
1. Il partage le processeur et la mémoire entre tous les programmes ouverts.
2. Il range les fichiers dans des dossiers sur le disque.
3. Il sert d'intermédiaire entre les logiciels et les pièces : un logiciel ne parle jamais directement au disque, il demande au système.

Le noyau (kernel) est le cœur du système. Linux, par exemple, est un noyau libre créé par Linus Torvalds en 1991. Il fait tourner la plupart des serveurs du monde et Android.

Astuce : le Gestionnaire des tâches (Ctrl + Maj + Échap) montre qui consomme quoi."),

        new("Les logiciels", "📦",
@"Un logiciel est une suite d'instructions écrites par un programmeur.

Du code au programme :
• Le programmeur écrit du code source (C#, Python, C…).
• Un compilateur le traduit en langage machine (des 0 et des 1) : c'est le fichier .exe.
• D'autres langages (Python, JavaScript) sont interprétés : lus ligne par ligne au moment de l'exécution.

Types de logiciels : système, applications (Word, Talkora), outils de développement, jeux.

Logiciel libre : le code est public, on peut l'étudier et l'améliorer (Linux, Firefox). Logiciel propriétaire : le code est secret (Windows).

Secret de métier : un programme est surtout des décisions (if) et des répétitions (for, while). Tout le reste en découle."),

        new("Les réseaux", "🌐",
@"Un réseau relie des ordinateurs pour qu'ils échangent des données.

• Chaque appareil a une adresse IP, comme une adresse postale (ex. 192.168.1.20).
• Un port est une porte précise de cette adresse : Talkora écoute sur le port 5000.
• TCP envoie les données dans l'ordre et vérifie qu'elles sont arrivées (messages, fichiers).
• UDP envoie vite sans vérifier (appels, découverte des voisins).
• Le routeur (la box) relie le réseau local à Internet.

P2P (pair à pair) : deux ordinateurs se parlent directement, sans serveur au milieu. C'est le principe de Talkora en réseau local.

Chiffrement (TLS) : les données sont brouillées pour que personne en route ne puisse les lire."),

        new("Les sites web", "🕸",
@"Un site web est un ensemble de fichiers servis par un ordinateur appelé serveur.

Quand on tape une adresse :
1. Le DNS transforme le nom (exemple.com) en adresse IP.
2. Le navigateur demande la page au serveur (protocole HTTP, ou HTTPS en sécurisé).
3. Le serveur renvoie des fichiers, et le navigateur les affiche.

Les trois langages de la page :
• HTML : la structure (titres, paragraphes, images).
• CSS : l'apparence (couleurs, mise en page).
• JavaScript : le comportement (boutons, animations).

Derrière, un serveur (PHP, Node.js, Python…) parle à une base de données (SQL) pour garder les comptes et les contenus.

Le Web a été inventé par Tim Berners-Lee en 1989-1991. Le Web n'est pas Internet : Internet est le réseau, le Web est un service qui roule dessus."),

        new("Sécurité et bons réflexes", "🔐",
@"La sécurité repose sur quelques habitudes simples.

• Mot de passe long (une phrase), différent pour chaque compte.
• Double authentification quand elle existe.
• Mises à jour : elles bouchent des failles connues.
• Ne pas cliquer sur un lien suspect (hameçonnage / phishing).
• Sauvegarder ses fichiers importants à deux endroits.

Notions utiles :
• Chiffrement : brouiller les données avec une clé.
• Hachage (SHA-256) : une empreinte unique d'un fichier. Si un seul bit change, l'empreinte change. C'est ainsi que Talkora vérifie l'intégrité d'un document.
• TOFU : on fait confiance à l'identité d'un contact la première fois, puis on alerte si elle change.

Secret : la plus grande faille est presque toujours l'humain, pas la machine."),

        new("Petits secrets et astuces", "✨",
@"• Win + V : historique du presse-papiers. Win + Maj + S : capture d'écran.
• Ctrl + Z annule, Ctrl + Y refait, Alt + Tab change de fenêtre.
• Une extension de fichier (.docx, .exe, .jpg) dit au système avec quel logiciel l'ouvrir. Méfiez-vous de « photo.jpg.exe » !
• Supprimer un fichier ne l'efface pas vraiment : le système oublie juste où il est. Pour l'effacer pour de bon, il faut l'écraser.
• La commande ipconfig montre votre adresse IP ; ping teste si un autre ordinateur répond.
• Le premier « bug » célèbre était un vrai insecte (un papillon de nuit) coincé dans un relais de l'ordinateur Harvard Mark II en 1947, trouvé dans l'équipe de Grace Hopper.
• Un 0 ou un 1 s'appelle un bit ; 8 bits font un octet ; 1 000 octets font un kilo-octet (Ko).")
    };
}

public sealed record Informaticien(string Nom, string Annee, string Domaine, string Resume, string Impact, string Date, string Anecdote);

/// <summary>Agenda des 5 informaticiens qui ont changé le monde d'aujourd'hui.</summary>
public static class AgendaInformaticiens
{
    public static readonly IReadOnlyList<Informaticien> Liste = new List<Informaticien>
    {
        new("Tim Berners-Lee", "né en 1955 · Royaume-Uni", "Le Web",
            "Physicien au CERN, il invente en 1989 le World Wide Web : un moyen de relier des documents entre eux par des liens.",
            "Il crée les trois bases du Web : l'adresse (URL), le protocole (HTTP) et le langage des pages (HTML). Il fonde le W3C en 1994 pour garder le Web ouvert et gratuit pour tous. Prix Turing 2016.",
            "1989-1991 : naissance du Web",
            "Il n'a pas breveté son invention : le Web est resté libre d'accès."),
        new("Linus Torvalds", "né en 1969 · Finlande", "Linux et Git",
            "Étudiant à Helsinki, il écrit en 1991 le noyau Linux, qu'il partage librement sur Internet.",
            "Linux fait tourner la majorité des serveurs, des supercalculateurs et le cœur d'Android. En 2005 il crée Git, l'outil avec lequel presque tous les développeurs gèrent leur code (GitHub en dépend).",
            "1991 : Linux · 2005 : Git",
            "Git a été écrit en quelques semaines parce qu'il avait besoin d'un meilleur outil pour Linux."),
        new("Geoffrey Hinton", "né en 1947 · Royaume-Uni / Canada", "Apprentissage profond (IA)",
            "Pionnier des réseaux de neurones : il a défendu pendant des décennies l'idée qu'une machine peut apprendre comme un cerveau.",
            "Ses travaux (rétropropagation, AlexNet en 2012 avec ses étudiants) lancent la révolution de l'IA moderne : reconnaissance d'images, voix, traduction, assistants. Prix Turing 2018 et prix Nobel de physique 2024.",
            "2012 : AlexNet · 2024 : Nobel de physique",
            "Il a quitté Google en 2023 pour pouvoir alerter librement sur les risques de l'IA."),
        new("Demis Hassabis", "né en 1976 · Royaume-Uni", "IA et sciences (DeepMind)",
            "Ancien prodige des échecs et du jeu vidéo, il cofonde DeepMind en 2010.",
            "AlphaGo bat un champion du monde de go en 2016. AlphaFold prédit la forme des protéines, ce qui accélère la recherche médicale. Prix Nobel de chimie 2024 (avec John Jumper et David Baker).",
            "2016 : AlphaGo · 2024 : Nobel de chimie",
            "AlphaFold a prédit la structure de centaines de millions de protéines, mises à la disposition des chercheurs du monde entier."),
        new("Dario Amodei", "né en 1983 · États-Unis", "IA sûre (Anthropic, Claude)",
            "Chercheur en IA, ancien responsable de la recherche chez OpenAI. Il cofonde Anthropic en 2021 et en est le directeur général.",
            "Anthropic développe Claude et met la sécurité de l'IA au centre de ses recherches : rendre les IA utiles, honnêtes et sans danger. C'est l'assistant qui t'aide à construire Talkora.",
            "2021 : création d'Anthropic",
            "Il est devenu chercheur en IA après des études de physique.")
    };

    /// <summary>Le « portrait du jour » : change chaque jour, tourne sur les 5.</summary>
    public static Informaticien PortraitDuJour(DateTime date) => Liste[date.DayOfYear % Liste.Count];
}

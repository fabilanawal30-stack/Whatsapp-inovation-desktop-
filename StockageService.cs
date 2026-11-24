using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TALKORA.Core;


public class StockageService : IMagasinEmpreintes
{
    private const int VersionSchema = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _dossier;
    private readonly string _cheminBase;

    // Dossier des fichiers reçus et envoyés (DataTalkora/Fichiers)
    public FichierService Fichiers { get; }

    // Un seul thread à la fois écrit dans la base (le réseau reçoit sur un autre thread que l'interface)
    private readonly object _verrou = new();

    public string CheminBase => _cheminBase;

    public StockageService(string dossier = "DataTalkora")
    {
        _dossier = dossier;
        Directory.CreateDirectory(_dossier);
        _cheminBase = Path.Combine(_dossier, "talkora.db");
        Fichiers = new FichierService(Path.Combine(_dossier, "Fichiers"));

        InitialiserBase();
        MigrerDepuisJson();
    }

    // =====================================================================
    //  OUTILS SQLITE
    // =====================================================================

    private SqliteConnection Ouvrir()
    {
        var connexion = new SqliteConnection("Data Source=" + _cheminBase);
        connexion.Open();
        Exec(connexion, "PRAGMA busy_timeout=5000");     // attend 5 s si la base est occupée au lieu d'échouer
        return connexion;
    }

    private static int Exec(SqliteConnection c, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nom, valeur) in parametres)
            cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalaire(SqliteConnection c, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nom, valeur) in parametres)
            cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
        object? r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    // Lit une colonne « json » et la transforme en objet (une ligne abîmée est ignorée, jamais fatale)
    private static List<T> LireObjets<T>(SqliteConnection c, string sql, params (string Nom, object? Valeur)[] parametres) where T : class
    {
        var resultat = new List<T>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nom, valeur) in parametres)
            cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);

        using var lecteur = cmd.ExecuteReader();
        while (lecteur.Read())
        {
            try
            {
                var objet = JsonSerializer.Deserialize<T>(lecteur.GetString(0));
                if (objet is not null)
                    resultat.Add(objet);
            }
            catch (JsonException) { }
        }
        return resultat;
    }

    // BEGIN ... COMMIT : tout ou rien. Si une erreur survient au milieu, rien n'est modifié.
    private void Transaction(Action<SqliteConnection> travail)
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            Exec(c, "BEGIN IMMEDIATE");
            try
            {
                travail(c);
                Exec(c, "COMMIT");
            }
            catch
            {
                try { Exec(c, "ROLLBACK"); } catch (SqliteException) { }
                throw;
            }
        }
    }

    // Les dates sont stockées en texte « 2026-11-18T23:20:00.0000000 » : l'ordre alphabétique = l'ordre du temps
    private static string DateTexte(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string Json<T>(T objet) => JsonSerializer.Serialize(objet);

    // =====================================================================
    //  CRÉATION DES TABLES
    // =====================================================================

    private void InitialiserBase()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            Scalaire(c, "PRAGMA journal_mode=WAL");          // lecture et écriture en même temps, plus robuste aux coupures
            Exec(c, "PRAGMA synchronous=NORMAL");

            Exec(c, "CREATE TABLE IF NOT EXISTS meta (cle TEXT PRIMARY KEY, valeur TEXT NOT NULL)");
            Exec(c, "CREATE TABLE IF NOT EXISTS profil (id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL)");
            Exec(c, @"CREATE TABLE IF NOT EXISTS contacts (
                        ip TEXT PRIMARY KEY, nom TEXT NOT NULL, bloque INTEGER NOT NULL DEFAULT 0,
                        muet INTEGER NOT NULL DEFAULT 0, ordre INTEGER NOT NULL, json TEXT NOT NULL)");
            Exec(c, @"CREATE TABLE IF NOT EXISTS groupes (
                        id TEXT PRIMARY KEY, nom TEXT NOT NULL, ordre INTEGER NOT NULL, json TEXT NOT NULL)");
            Exec(c, @"CREATE TABLE IF NOT EXISTS messages (
                        id TEXT PRIMARY KEY, destinataire TEXT NOT NULL, expediteur_ip TEXT NOT NULL,
                        est_groupe INTEGER NOT NULL, type INTEGER NOT NULL, date_envoi TEXT NOT NULL,
                        expire_le TEXT NULL, supprime INTEGER NOT NULL DEFAULT 0, epingle INTEGER NOT NULL DEFAULT 0,
                        contenu TEXT NOT NULL DEFAULT '', json TEXT NOT NULL)");
            Exec(c, "CREATE INDEX IF NOT EXISTS idx_messages_dest ON messages (destinataire, date_envoi)");
            Exec(c, "CREATE INDEX IF NOT EXISTS idx_messages_exp ON messages (expediteur_ip, date_envoi)");
            Exec(c, "CREATE INDEX IF NOT EXISTS idx_messages_expire ON messages (expire_le) WHERE expire_le IS NOT NULL");
            Exec(c, @"CREATE TABLE IF NOT EXISTS statuts (
                        id TEXT PRIMARY KEY, auteur_ip TEXT NOT NULL, date_creation TEXT NOT NULL,
                        expire_le TEXT NOT NULL, json TEXT NOT NULL)");

            Exec(c, @"CREATE TABLE IF NOT EXISTS identites (
                        ip TEXT PRIMARY KEY, empreinte TEXT NOT NULL, date_vue TEXT NOT NULL)");

            Exec(c, @"CREATE TABLE IF NOT EXISTS suivis (
                        id TEXT PRIMARY KEY, etat INTEGER NOT NULL, echeance TEXT NOT NULL, json TEXT NOT NULL)");

            Exec(c, @"CREATE TABLE IF NOT EXISTS souvenirs (
                        message_id TEXT PRIMARY KEY, ajoute TEXT NOT NULL)");

            Exec(c, @"CREATE TABLE IF NOT EXISTS together (
                        id TEXT PRIMARY KEY, espace TEXT NOT NULL, version INTEGER NOT NULL, modifie TEXT NOT NULL, json TEXT NOT NULL)");
            Exec(c, "CREATE INDEX IF NOT EXISTS idx_together_espace ON together (espace)");

            Exec(c, @"CREATE TABLE IF NOT EXISTS sortants (
                        id TEXT PRIMARY KEY, ip TEXT NOT NULL, cree TEXT NOT NULL, tentatives INTEGER NOT NULL DEFAULT 0, json TEXT NOT NULL)");
            Exec(c, "CREATE INDEX IF NOT EXISTS idx_sortants_ip ON sortants (ip)");

            Exec(c, "INSERT OR IGNORE INTO meta (cle, valeur) VALUES ('version_schema', $v)", ("$v", VersionSchema.ToString()));
        }
    }

    // =====================================================================
    //  PROFIL
    // =====================================================================

    public ProfilUtilisateur? ChargerProfil()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            var depuisBase = LireObjets<ProfilUtilisateur>(c, "SELECT json FROM profil WHERE id = 1").FirstOrDefault();
            if (depuisBase is not null)
                return depuisBase;
        }

        // Pas encore dans la base : le profil créé à l'inscription est dans profil.json, on l'importe
        var depuisFichier = LireJson<ProfilUtilisateur>("profil.json");
        if (depuisFichier is not null)
            SauvegarderProfil(depuisFichier);
        return depuisFichier;
    }

    public void SauvegarderProfil(ProfilUtilisateur profil)
    {
        Transaction(c => Exec(c,
            "INSERT INTO profil (id, json) VALUES (1, $j) ON CONFLICT(id) DO UPDATE SET json = excluded.json",
            ("$j", Json(profil))));

        // Copie de lecture pour le démarrage du programme (Program.cs lit profil.json)
        EcrireJson("profil.json", profil);
    }

    // =====================================================================
    //  CONTACTS
    // =====================================================================

    public List<Contact> ChargerContacts()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<Contact>(c, "SELECT json FROM contacts ORDER BY ordre");
        }
    }

    public void SauvegarderContacts(List<Contact> contacts) => Transaction(c => RemplacerContacts(c, contacts));

    private static void RemplacerContacts(SqliteConnection c, List<Contact> contacts)
    {
        // On supprime ceux qui ne sont plus dans la liste, puis on écrit/met à jour les autres (en gardant l'ordre)
        var gardes = contacts.Select(x => x.AdresseIp).ToHashSet();
        var presents = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT ip FROM contacts";
            using var r = cmd.ExecuteReader();
            while (r.Read()) presents.Add(r.GetString(0));
        }
        foreach (string ip in presents.Where(ip => !gardes.Contains(ip)))
            Exec(c, "DELETE FROM contacts WHERE ip = $ip", ("$ip", ip));

        for (int i = 0; i < contacts.Count; i++)
        {
            var k = contacts[i];
            Exec(c, @"INSERT INTO contacts (ip, nom, bloque, muet, ordre, json) VALUES ($ip, $nom, $b, $m, $o, $j)
                      ON CONFLICT(ip) DO UPDATE SET nom = excluded.nom, bloque = excluded.bloque, muet = excluded.muet,
                                                    ordre = excluded.ordre, json = excluded.json",
                ("$ip", k.AdresseIp), ("$nom", k.Nom), ("$b", k.EstBloque ? 1 : 0), ("$m", k.Muet ? 1 : 0),
                ("$o", i), ("$j", Json(k)));
        }
    }

    // =====================================================================
    //  GROUPES
    // =====================================================================

    public List<Groupe> ChargerGroupes()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<Groupe>(c, "SELECT json FROM groupes ORDER BY ordre");
        }
    }

    public void SauvegarderGroupes(List<Groupe> groupes) => Transaction(c => RemplacerGroupes(c, groupes));

    private static void RemplacerGroupes(SqliteConnection c, List<Groupe> groupes)
    {
        var gardes = groupes.Select(g => g.Id.ToString()).ToHashSet();
        var presents = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM groupes";
            using var r = cmd.ExecuteReader();
            while (r.Read()) presents.Add(r.GetString(0));
        }
        foreach (string id in presents.Where(id => !gardes.Contains(id)))
            Exec(c, "DELETE FROM groupes WHERE id = $id", ("$id", id));

        for (int i = 0; i < groupes.Count; i++)
        {
            var g = groupes[i];
            Exec(c, @"INSERT INTO groupes (id, nom, ordre, json) VALUES ($id, $nom, $o, $j)
                      ON CONFLICT(id) DO UPDATE SET nom = excluded.nom, ordre = excluded.ordre, json = excluded.json",
                ("$id", g.Id.ToString()), ("$nom", g.Nom), ("$o", i), ("$j", Json(g)));
        }
    }

    // =====================================================================
    //  MESSAGES
    // =====================================================================

    private const string SqlUpsertMessage = @"
        INSERT INTO messages (id, destinataire, expediteur_ip, est_groupe, type, date_envoi, expire_le, supprime, epingle, contenu, json)
        VALUES ($id, $dest, $exp, $grp, $type, $date, $expire, $sup, $epi, $contenu, $j)
        ON CONFLICT(id) DO UPDATE SET destinataire = excluded.destinataire, expediteur_ip = excluded.expediteur_ip,
            est_groupe = excluded.est_groupe, type = excluded.type, date_envoi = excluded.date_envoi,
            expire_le = excluded.expire_le, supprime = excluded.supprime, epingle = excluded.epingle,
            contenu = excluded.contenu, json = excluded.json";

    private const string SqlInsererMessageSiNouveau = @"
        INSERT INTO messages (id, destinataire, expediteur_ip, est_groupe, type, date_envoi, expire_le, supprime, epingle, contenu, json)
        VALUES ($id, $dest, $exp, $grp, $type, $date, $expire, $sup, $epi, $contenu, $j)
        ON CONFLICT(id) DO NOTHING";

    private static (string, object?)[] ParametresMessage(MessageChat m) => new (string, object?)[]
    {
        ("$id", m.Id.ToString()), ("$dest", m.Destinataire), ("$exp", m.AdresseIpExpediteur),
        ("$grp", m.EstGroupe ? 1 : 0), ("$type", (int)m.Type), ("$date", DateTexte(m.DateEnvoi)),
        ("$expire", m.ExpireLe is null ? null : DateTexte(m.ExpireLe.Value)),
        ("$sup", m.Supprime ? 1 : 0), ("$epi", m.Epingle ? 1 : 0), ("$contenu", m.Contenu), ("$j", Json(m))
    };

    // Tous les messages, dans l'ordre d'arrivée
    public List<MessageChat> ChargerMessages()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<MessageChat>(c, "SELECT json FROM messages ORDER BY rowid");
        }
    }

    public void AjouterMessage(MessageChat message)
        => Transaction(c => Exec(c, SqlUpsertMessage, ParametresMessage(message)));

    // Ajoute le message seulement s'il n'existe pas déjà (même Id) : évite les doublons
    // quand un envoi est réessayé. Renvoie faux si le message était déjà là.
    public bool AjouterMessageSiNouveau(MessageChat message)
    {
        bool ajoute = false;
        Transaction(c => ajoute = Exec(c, SqlInsererMessageSiNouveau, ParametresMessage(message)) > 0);
        return ajoute;
    }

    // Réécrit tout l'historique (supprime ce qui n'est plus dans la liste, met à jour le reste)
    public void SauvegarderMessages(List<MessageChat> messages)
    {
        Transaction(c =>
        {
            var gardes = messages.Select(m => m.Id.ToString()).ToHashSet();
            var presents = new List<string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM messages";
                using var r = cmd.ExecuteReader();
                while (r.Read()) presents.Add(r.GetString(0));
            }
            foreach (string id in presents.Where(id => !gardes.Contains(id)))
                Exec(c, "DELETE FROM messages WHERE id = $id", ("$id", id));

            foreach (var m in messages)
                Exec(c, SqlUpsertMessage, ParametresMessage(m));
        });
    }

    // Applique une action reçue (modification, réaction...) au message visé, puis l'enregistre
    public bool AppliquerAction(MessageChat action)
    {
        if (action.CibleId is null)
            return false;

        bool change = false;
        Transaction(c =>
        {
            // Une action ne touche qu'UN message : on ne lit que celui-là
            var liste = LireObjets<MessageChat>(c, "SELECT json FROM messages WHERE id = $id",
                                                ("$id", action.CibleId.Value.ToString()));
            if (liste.Count == 0)
                return;

            change = MessageActions.Appliquer(liste, action);
            if (!change)
                return;

            var cible = liste[0];

            // Message supprimé : on efface aussi son fichier du disque
            if (action.Type == TypeMessage.Suppression && cible.CheminLocal.Length > 0)
            {
                Fichiers.Supprimer(cible.CheminLocal);
                cible.CheminLocal = "";
            }

            Exec(c, SqlUpsertMessage, ParametresMessage(cible));
        });
        return change;
    }

    /// <summary>« Supprimer pour moi » : le message disparaît de CET ordinateur seulement (l'autre personne le garde).</summary>
    public void SupprimerPourMoi(Guid id)
    {
        string? chemin = null;
        Transaction(c =>
        {
            var l = LireObjets<MessageChat>(c, "SELECT json FROM messages WHERE id = $id", ("$id", id.ToString()));
            if (l.Count > 0) chemin = l[0].CheminLocal;
            Exec(c, "DELETE FROM messages WHERE id = $id", ("$id", id.ToString()));
        });
        if (!string.IsNullOrEmpty(chemin)) Fichiers.Supprimer(chemin);
    }

    // Remplace un message déjà enregistré (même Id) : utile après un « réessayer l'envoi »
    public void ModifierMessage(MessageChat message)
    {
        Transaction(c =>
        {
            if (Scalaire(c, "SELECT 1 FROM messages WHERE id = $id", ("$id", message.Id.ToString())) is not null)
                Exec(c, SqlUpsertMessage, ParametresMessage(message));
        });
    }

    // Tous les messages dont le destinataire est cette IP (ou cet identifiant de groupe)
    public List<MessageChat> MessagesAvec(string destinataire)
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<MessageChat>(c, "SELECT json FROM messages WHERE destinataire = $d ORDER BY rowid", ("$d", destinataire));
        }
    }

    // Supprime les messages temporaires périmés (et leurs fichiers) : atomique
    public int PurgerExpires()
    {
        int n = 0;
        Transaction(c =>
        {
            var perimes = LireObjets<MessageChat>(c,
                "SELECT json FROM messages WHERE expire_le IS NOT NULL AND expire_le <= $maintenant",
                ("$maintenant", DateTexte(DateTime.Now)));

            foreach (var m in perimes)
            {
                if (m.CheminLocal.Length > 0)
                    Fichiers.Supprimer(m.CheminLocal);
                n += Exec(c, "DELETE FROM messages WHERE id = $id", ("$id", m.Id.ToString()));
            }
        });
        return n;
    }

    // =====================================================================
    //  STATUTS
    // =====================================================================

    public List<Statut> ChargerStatuts()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<Statut>(c, "SELECT json FROM statuts ORDER BY rowid");
        }
    }

    public void SauvegarderStatuts(List<Statut> statuts) => Transaction(c => RemplacerStatuts(c, statuts));

    private static void RemplacerStatuts(SqliteConnection c, List<Statut> statuts)
    {
        var gardes = statuts.Select(s => s.Id.ToString()).ToHashSet();
        var presents = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM statuts";
            using var r = cmd.ExecuteReader();
            while (r.Read()) presents.Add(r.GetString(0));
        }
        foreach (string id in presents.Where(id => !gardes.Contains(id)))
            Exec(c, "DELETE FROM statuts WHERE id = $id", ("$id", id));

        foreach (var s in statuts)
            Exec(c, @"INSERT INTO statuts (id, auteur_ip, date_creation, expire_le, json) VALUES ($id, $a, $d, $e, $j)
                      ON CONFLICT(id) DO UPDATE SET auteur_ip = excluded.auteur_ip, date_creation = excluded.date_creation,
                                                    expire_le = excluded.expire_le, json = excluded.json",
                ("$id", s.Id.ToString()), ("$a", s.AuteurIp), ("$d", DateTexte(s.DateCreation)),
                ("$e", DateTexte(s.ExpireLe)), ("$j", Json(s)));
    }

    // Retire les statuts de plus de 24 h et efface leurs images. Renvoie combien ont disparu.
    public int PurgerStatuts(List<Statut> statuts)
    {
        var perimes = StatutActions.Purger(statuts, DateTime.Now);
        foreach (var s in perimes)
            if (s.CheminLocal.Length > 0)
                Fichiers.Supprimer(s.CheminLocal);

        if (perimes.Count > 0)
            SauvegarderStatuts(statuts);
        return perimes.Count;
    }

    // =====================================================================
    //  SUIVIS (Talkora Follow-up)
    // =====================================================================

    public List<Suivi> ChargerSuivis()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<Suivi>(c, "SELECT json FROM suivis ORDER BY echeance");
        }
    }

    /// <summary>Ajoute ou met à jour un suivi (clé = Id).</summary>
    public void SauvegarderSuivi(Suivi s) => Transaction(c =>
        Exec(c, @"INSERT INTO suivis (id, etat, echeance, json) VALUES ($id, $e, $d, $j)
                  ON CONFLICT(id) DO UPDATE SET etat = excluded.etat, echeance = excluded.echeance, json = excluded.json",
            ("$id", s.Id.ToString()), ("$e", (int)s.Etat), ("$d", DateTexte(s.Echeance)), ("$j", Json(s))));

    public void SupprimerSuivi(Guid id) => Transaction(c => Exec(c, "DELETE FROM suivis WHERE id = $id", ("$id", id.ToString())));

    /// <summary>Retire les suivis terminés ou ignorés depuis plus de « jours » jours. Renvoie combien.</summary>
    public int PurgerSuivis(int jours = 30)
    {
        int n = 0;
        Transaction(c => n = Exec(c, "DELETE FROM suivis WHERE etat <> 0 AND echeance < $d",
            ("$d", DateTexte(DateTime.Now.AddDays(-Math.Max(1, jours))))));
        return n;
    }

    // =====================================================================
    //  FILE D'ATTENTE DES ACTIONS (réactions, modifications… pas encore reçues par un contact)
    // =====================================================================

    public record Sortant(MessageChat Message, string Ip, int Tentatives, DateTime Cree);

    /// <summary>Met une action en attente pour un contact (une seule fois par action : même Id = ignorée).</summary>
    public void AjouterSortant(string ip, MessageChat message) => Transaction(c =>
        Exec(c, "INSERT OR IGNORE INTO sortants (id, ip, cree, tentatives, json) VALUES ($id, $ip, $d, 0, $j)",
            ("$id", message.Id.ToString() + "|" + ip), ("$ip", ip), ("$d", DateTexte(DateTime.Now)), ("$j", Json(message))));

    public List<Sortant> ChargerSortants(string ip)
    {
        lock (_verrou)
        {
            var res = new List<Sortant>();
            using var c = Ouvrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT json, tentatives, cree FROM sortants WHERE ip = $ip ORDER BY rowid";
            cmd.Parameters.AddWithValue("$ip", ip);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                try
                {
                    var m = JsonSerializer.Deserialize<MessageChat>(r.GetString(0));
                    if (m is not null)
                        res.Add(new Sortant(m, ip, int.Parse(r.GetString(1), CultureInfo.InvariantCulture), DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture)));
                }
                catch (JsonException) { }
            }
            return res;
        }
    }

    public void SupprimerSortant(Guid messageId, string ip) => Transaction(c =>
        Exec(c, "DELETE FROM sortants WHERE id = $id", ("$id", messageId.ToString() + "|" + ip)));

    public void CompterEssaiSortant(Guid messageId, string ip) => Transaction(c =>
        Exec(c, "UPDATE sortants SET tentatives = tentatives + 1 WHERE id = $id", ("$id", messageId.ToString() + "|" + ip)));

    /// <summary>Abandonne les actions trop vieilles ou trop essayées. Renvoie combien.</summary>
    public int PurgerSortants()
    {
        int n = 0;
        Transaction(c => n = Exec(c, "DELETE FROM sortants WHERE tentatives >= $t OR cree < $d",
            ("$t", ServiceRenvoi.TentativesMax), ("$d", DateTexte(DateTime.Now.AddDays(-ServiceRenvoi.JoursFile)))));
        return n;
    }

    public int CompterSortants()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return Convert.ToInt32(Scalaire(c, "SELECT COUNT(*) FROM sortants") ?? 0);
        }
    }

    // =====================================================================
    //  TOGETHER (espaces partagés)
    // =====================================================================

    public List<ItemTogether> ChargerTogether()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return LireObjets<ItemTogether>(c, "SELECT json FROM together ORDER BY rowid");
        }
    }

    /// <summary>Ajoute ou met à jour des éléments (clé = Id), en une seule transaction.</summary>
    public void SauvegarderTogether(IEnumerable<ItemTogether> items)
    {
        var l = items.ToList();
        if (l.Count == 0) return;
        Transaction(c =>
        {
            foreach (var it in l)
                Exec(c, @"INSERT INTO together (id, espace, version, modifie, json) VALUES ($id, $e, $v, $m, $j)
                          ON CONFLICT(id) DO UPDATE SET espace = excluded.espace, version = excluded.version,
                                                        modifie = excluded.modifie, json = excluded.json",
                    ("$id", it.Id.ToString()), ("$e", it.Espace), ("$v", it.Version), ("$m", DateTexte(it.Modifie)), ("$j", Json(it)));
        });
    }

    public void SupprimerTogether(IEnumerable<Guid> ids)
    {
        var l = ids.ToList();
        if (l.Count == 0) return;
        Transaction(c => { foreach (var id in l) Exec(c, "DELETE FROM together WHERE id = $id", ("$id", id.ToString())); });
    }

    // =====================================================================
    //  SOUVENIRS GARDÉS (⭐ Talkora Memories)
    // =====================================================================

    /// <summary>Les messages que j'ai gardés en souvenir, du plus récemment ajouté au plus ancien.</summary>
    public List<Guid> ChargerSouvenirsGardes()
    {
        lock (_verrou)
        {
            var res = new List<Guid>();
            using var c = Ouvrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT message_id FROM souvenirs ORDER BY ajoute DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (Guid.TryParse(r.GetString(0), out var id)) res.Add(id);
            return res;
        }
    }

    public void GarderSouvenir(Guid messageId) => Transaction(c =>
        Exec(c, "INSERT OR IGNORE INTO souvenirs (message_id, ajoute) VALUES ($id, $d)",
            ("$id", messageId.ToString()), ("$d", DateTexte(DateTime.Now))));

    public void RetirerSouvenir(Guid messageId) => Transaction(c =>
        Exec(c, "DELETE FROM souvenirs WHERE message_id = $id", ("$id", messageId.ToString())));

    // =====================================================================
    //  IDENTITÉS TLS (empreinte du certificat de chaque contact, vue la 1re fois)
    // =====================================================================

    public string? ObtenirEmpreinte(string ip)
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return Scalaire(c, "SELECT empreinte FROM identites WHERE ip = $ip", ("$ip", ip)) as string;
        }
    }

    public void EnregistrerEmpreinte(string ip, string empreinte)
        => Transaction(c => Exec(c,
            "INSERT INTO identites (ip, empreinte, date_vue) VALUES ($ip, $e, $d) ON CONFLICT(ip) DO UPDATE SET empreinte = excluded.empreinte, date_vue = excluded.date_vue",
            ("$ip", ip), ("$e", empreinte), ("$d", DateTexte(DateTime.Now))));

    public void OublierEmpreinte(string ip)
        => Transaction(c => Exec(c, "DELETE FROM identites WHERE ip = $ip", ("$ip", ip)));

    // =====================================================================
    //  MAINTENANCE : chiffres, sauvegarde, vérification
    // =====================================================================

    public (int Messages, int Contacts, int Groupes, int Statuts) Compter()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            int N(string table) => Convert.ToInt32(Scalaire(c, $"SELECT COUNT(*) FROM {table}") ?? 0);
            return (N("messages"), N("contacts"), N("groupes"), N("statuts"));
        }
    }

    /// <summary>Copie complète et cohérente de la base dans un nouveau fichier (le fichier ne doit pas exister).</summary>
    public void SauvegarderBase(string destination)
    {
        if (File.Exists(destination))
            throw new IOException("Ce fichier existe déjà : choisissez un autre nom.");

        lock (_verrou)
        {
            using var c = Ouvrir();
            Exec(c, "VACUUM INTO $f", ("$f", destination));
        }
    }

    /// <summary>true si la base est saine (contrôle d'intégrité de SQLite).</summary>
    public bool VerifierBase()
    {
        lock (_verrou)
        {
            using var c = Ouvrir();
            return string.Equals(Scalaire(c, "PRAGMA integrity_check") as string, "ok", StringComparison.OrdinalIgnoreCase);
        }
    }

    // =====================================================================
    //  IMPORT DES ANCIENS FICHIERS JSON (une seule fois)
    // =====================================================================

    private T? LireJson<T>(string fichier) where T : class
    {
        string chemin = Path.Combine(_dossier, fichier);
        if (!File.Exists(chemin))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(chemin));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;   // fichier abîmé : on l'ignore au lieu de planter
        }
    }

    private void EcrireJson<T>(string fichier, T donnees)
    {
        string chemin = Path.Combine(_dossier, fichier);
        string temporaire = chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(donnees, Options));
        File.Move(temporaire, chemin, overwrite: true);
    }

    private void MigrerDepuisJson()
    {
        lock (_verrou)
        {
            using (var c = Ouvrir())
                if (Scalaire(c, "SELECT valeur FROM meta WHERE cle = 'migration_json'") is not null)
                    return;
        }

        var contacts = LireJson<List<Contact>>("contacts.json");
        var groupes = LireJson<List<Groupe>>("groupes.json");
        var messages = LireJson<List<MessageChat>>("messages.json");
        var statuts = LireJson<List<Statut>>("statuts.json");

        Transaction(c =>
        {
            if (contacts is not null) RemplacerContacts(c, contacts);
            if (groupes is not null) RemplacerGroupes(c, groupes);
            if (statuts is not null) RemplacerStatuts(c, statuts);
            if (messages is not null)
                foreach (var m in messages)
                    Exec(c, SqlInsererMessageSiNouveau, ParametresMessage(m));

            Exec(c, "INSERT OR REPLACE INTO meta (cle, valeur) VALUES ('migration_json', $d)", ("$d", DateTexte(DateTime.Now)));
        });

        // Les anciens fichiers sont rangés (pas supprimés) : on peut toujours les récupérer
        try
        {
            string rangement = Path.Combine(_dossier, "Sauvegarde-JSON");
            foreach (string nom in new[] { "contacts.json", "groupes.json", "messages.json", "statuts.json" })
            {
                string source = Path.Combine(_dossier, nom);
                if (!File.Exists(source)) continue;
                Directory.CreateDirectory(rangement);
                File.Move(source, Path.Combine(rangement, nom), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

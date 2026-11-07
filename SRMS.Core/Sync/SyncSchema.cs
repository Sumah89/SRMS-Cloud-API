using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace SRMS.Desktop.Sync
{
    public static class SyncSchema
    {
        public static void Ensure(SqliteConnection conn)
        {
            int currentVersion = ReadSchemaVersion(conn);
            
            // We force the schema to run if it's not version 3
            if (currentVersion >= SyncConstants.SchemaVersion) return;

            Debug.WriteLine($"[SRMS.Sync] Migrating schema from v{currentVersion} to v{SyncConstants.SchemaVersion}");

            CreateMetadataTables(conn);
            CreateDomainTables(conn);
            EnsureSyncColumnsOnAllTables(conn);
            BackfillSyncColumns(conn);
            CreateSyncIndexes(conn);
            CreateChangeLogTriggers(conn);

            WriteSchemaVersion(conn, SyncConstants.SchemaVersion);
            Debug.WriteLine("[SRMS.Sync] Schema migration complete.");
        }

        // ─── Version tracking ─────────────────────────────────────────

        private static int ReadSchemaVersion(SqliteConnection conn)
        {
            try
            {
                using var cmd = new SqliteCommand(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = @Key", conn);
                cmd.Parameters.AddWithValue("@Key", SyncConstants.SchemaVersionKey);
                object v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return 0;
                return int.TryParse(v.ToString(), out int i) ? i : 0;
            }
            catch { return 0; }
        }

        private static void WriteSchemaVersion(SqliteConnection conn, int version)
        {
            using var cmd = new SqliteCommand(
                @"INSERT INTO AppSettings (SettingKey, SettingValue, CreatedDate, ModifiedDate)
                  VALUES (@Key, @Value, datetime('now'), datetime('now'))
                  ON CONFLICT(SettingKey) DO UPDATE SET
                    SettingValue = @Value,
                    ModifiedDate = datetime('now')", conn);
            cmd.Parameters.AddWithValue("@Key",   SyncConstants.SchemaVersionKey);
            cmd.Parameters.AddWithValue("@Value", version.ToString());
            cmd.ExecuteNonQuery();
        }

        // ─── Metadata tables ──────────────────────────────────────────

        private static void CreateMetadataTables(SqliteConnection conn)
        {
            Exec(conn, @"CREATE TABLE IF NOT EXISTS DeviceInfo (
                DeviceId      TEXT PRIMARY KEY,
                FriendlyName  TEXT NOT NULL,
                SchoolId      INTEGER,
                Platform      TEXT,
                CreatedAt     INTEGER NOT NULL,
                FirstSeenAt   INTEGER NOT NULL,
                LastSyncAt    INTEGER
            );");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS change_log (
                seq           INTEGER PRIMARY KEY AUTOINCREMENT,
                sync_uuid     TEXT    NOT NULL,
                table_name    TEXT    NOT NULL,
                op            TEXT    NOT NULL,
                payload       TEXT    NOT NULL,
                updated_at    INTEGER NOT NULL,
                updated_by    TEXT    NOT NULL,
                applied       INTEGER NOT NULL DEFAULT 0
            );");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS sync_peers (
                device_id         TEXT PRIMARY KEY,
                device_name       TEXT,
                school_id         INTEGER,
                last_seq_sent     INTEGER NOT NULL DEFAULT 0,
                last_seq_recv     INTEGER NOT NULL DEFAULT 0,
                last_seen_at      INTEGER
            );");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS conflict_log (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                sync_uuid      TEXT NOT NULL,
                table_name     TEXT NOT NULL,
                field          TEXT,
                local_value    TEXT,
                incoming_value TEXT,
                resolution     TEXT NOT NULL,
                resolved_at    INTEGER NOT NULL
            );");
        }

        // ─── Domain tables (Phase 3 additions) ────────────────────────

        private static void CreateDomainTables(SqliteConnection conn)
        {
            // Daily attendance — one row per student per day.
            Exec(conn, @"CREATE TABLE IF NOT EXISTS DailyAttendance (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId       INTEGER NOT NULL,
                ClassId         INTEGER NOT NULL,
                Date            TEXT NOT NULL,
                Status          TEXT NOT NULL,
                RecordedByUserId INTEGER NOT NULL,
                Remarks         TEXT,
                CreatedDate     DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate    DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StudentId, Date)
            );");

            // Access grants — who can see what.
            Exec(conn, @"CREATE TABLE IF NOT EXISTS AccessGrants (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                GranteeUserId   INTEGER NOT NULL,
                GranteeRole     TEXT NOT NULL,
                GrantType       TEXT NOT NULL,
                StudentId       INTEGER,
                SubjectId       INTEGER,
                ClassLevel      TEXT,
                ClassId         INTEGER,
                GrantedByUserId INTEGER NOT NULL,
                GrantedAt       INTEGER NOT NULL,
                ExpiresAt       INTEGER,
                RevokedAt       INTEGER,
                RevokedByUserId INTEGER,
                Notes           TEXT
            );");

            // Admin → teacher messages.
            Exec(conn, @"CREATE TABLE IF NOT EXISTS Messages (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId        INTEGER NOT NULL,
                SentByUserId    INTEGER NOT NULL,
                SentByName      TEXT,
                RecipientType   TEXT NOT NULL,
                RecipientUserId INTEGER,
                Title           TEXT NOT NULL,
                Body            TEXT NOT NULL,
                SentAt          INTEGER NOT NULL,
                ExpiresAt       INTEGER,
                CreatedDate     DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate    DATETIME DEFAULT CURRENT_TIMESTAMP
            );");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS MessageReplies (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                MessageId       INTEGER NOT NULL,
                RepliedByUserId INTEGER NOT NULL,
                RepliedByName   TEXT,
                Body            TEXT NOT NULL,
                RepliedAt       INTEGER NOT NULL,
                CreatedDate     DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate    DATETIME DEFAULT CURRENT_TIMESTAMP
            );");

            // Signed publication record.
            Exec(conn, @"CREATE TABLE IF NOT EXISTS ReportPublications (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId         INTEGER NOT NULL,
                AcademicYear      TEXT NOT NULL,
                Term              INTEGER NOT NULL,
                PublishedByUserId INTEGER NOT NULL,
                PublishedAt       INTEGER NOT NULL,
                PublicationHash   TEXT NOT NULL,
                CreatedDate       DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate      DATETIME DEFAULT CURRENT_TIMESTAMP
            );");

            // Device push tokens.
            Exec(conn, @"CREATE TABLE IF NOT EXISTS DeviceTokens (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId         INTEGER NOT NULL,
                DeviceId       TEXT NOT NULL,
                FcmToken       TEXT NOT NULL,
                Platform       TEXT,
                RegisteredAt   INTEGER NOT NULL,
                LastSeenAt     INTEGER,
                CreatedDate    DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate   DATETIME DEFAULT CURRENT_TIMESTAMP
            );");

            // Phase 3b — Teacher and Parent Access Codes (V2 to avoid legacy clashes)
            Exec(conn, @"CREATE TABLE IF NOT EXISTS TeacherCodesV2 (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId          INTEGER NOT NULL,
                SchoolId        INTEGER NOT NULL,
                PhoneE164       TEXT NOT NULL,
                Code            TEXT NOT NULL,
                IsActive        INTEGER NOT NULL DEFAULT 1,
                ExpiresAt       INTEGER,
                CreatedAt       INTEGER NOT NULL,
                LastUsedAt      INTEGER,
                sync_uuid       TEXT,
                updated_at      INTEGER,
                updated_by      TEXT
            );");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS ParentCodesV2 (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ParentUserId    INTEGER NOT NULL,
                SchoolId        INTEGER NOT NULL,
                StudentId       INTEGER NOT NULL,
                PhoneE164       TEXT NOT NULL,
                Code            TEXT NOT NULL,
                IsActive        INTEGER NOT NULL DEFAULT 1,
                ExpiresAt       INTEGER,
                CreatedAt       INTEGER NOT NULL,
                LastUsedAt      INTEGER,
                sync_uuid       TEXT,
                updated_at      INTEGER,
                updated_by      TEXT
            );");
        }

        // ─── Add sync columns ─────────────────────────────────────────

        private static void EnsureSyncColumnsOnAllTables(SqliteConnection conn)
        {
            foreach (string table in SyncConstants.SyncableTables)
            {
                if (!TableExists(conn, table))
                {
                    Debug.WriteLine($"[SRMS.Sync] Skipping {table} — does not exist.");
                    continue;
                }

                EnsureColumn(conn, table, SyncConstants.ColSyncUuid,  "TEXT");
                EnsureColumn(conn, table, SyncConstants.ColUpdatedBy, "TEXT");
                EnsureColumn(conn, table, SyncConstants.ColUpdatedAt, "INTEGER");
            }
        }

        private static void EnsureColumn(SqliteConnection conn, string table, string column, string columnDef)
        {
            if (ColumnExists(conn, table, column)) return;
            try
            {
                Exec(conn, $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {columnDef}");
                Debug.WriteLine($"[SRMS.Sync] Added {table}.{column}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.Sync] Failed to add {table}.{column}: {ex.Message}");
            }
        }

        // ─── Backfill ─────────────────────────────────────────────────

        private static void BackfillSyncColumns(SqliteConnection conn)
        {
            const string migrationSentinel = "migration";

            foreach (string table in SyncConstants.SyncableTables)
            {
                if (!TableExists(conn, table)) continue;

                try
                {
                    Exec(conn, $@"
                        UPDATE {Quote(table)}
                        SET sync_uuid = lower(hex(randomblob(16)))
                        WHERE sync_uuid IS NULL;");

                    Exec(conn, $@"
                        UPDATE {Quote(table)}
                        SET updated_by = '{migrationSentinel}'
                        WHERE updated_by IS NULL;");

                    Exec(conn, $@"
                        UPDATE {Quote(table)}
                        SET updated_at = CAST(strftime('%s','now') AS INTEGER) * 1000
                        WHERE updated_at IS NULL;");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SRMS.Sync] Backfill failed for {table}: {ex.Message}");
                }
            }
        }

        // ─── Indexes ──────────────────────────────────────────────────

        private static void CreateSyncIndexes(SqliteConnection conn)
        {
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_change_log_applied ON change_log(applied);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_change_log_uuid    ON change_log(sync_uuid);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_change_log_table   ON change_log(table_name);");

            foreach (string table in SyncConstants.SyncableTables)
            {
                if (!TableExists(conn, table)) continue;
                try
                {
                    Exec(conn, $"CREATE UNIQUE INDEX IF NOT EXISTS idx_{table}_sync_uuid ON {Quote(table)}(sync_uuid);");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SRMS.Sync] Could not create unique index on {table}.sync_uuid: {ex.Message}");
                }
            }
        }

        // ─── Triggers ─────────────────────────────────────────────────

        private static void CreateChangeLogTriggers(SqliteConnection conn)
        {
            foreach (string table in SyncConstants.SyncableTables)
            {
                if (!TableExists(conn, table)) continue;

                var columns = GetColumnNames(conn, table);
                if (columns.Count == 0) continue;

                var dataColumns = columns
                    .Where(c => !string.Equals(c, SyncConstants.ColSyncUuid,  StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(c, SyncConstants.ColUpdatedAt, StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(c, SyncConstants.ColUpdatedBy, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                CreateTrigger(conn, table, "AFTER INSERT", "insert", dataColumns, useNew: true);
                CreateTrigger(conn, table, "AFTER UPDATE", "update", dataColumns, useNew: true);
                CreateTrigger(conn, table, "AFTER DELETE", "delete", dataColumns, useNew: false);
            }
        }

        private static void CreateTrigger(
            SqliteConnection conn, string table, string timing, string op,
            List<string> dataColumns, bool useNew)
        {
            string triggerName = $"trg_sync_{table}_{op}";
            string src = useNew ? "NEW" : "OLD";

            var jsonPairs = new List<string>();
            foreach (string col in dataColumns)
                jsonPairs.Add($"'{EscapeSqlString(col)}', {src}.{Quote(col)}");

            string sql = $@"
                CREATE TRIGGER IF NOT EXISTS {Quote(triggerName)}
                {timing} ON {Quote(table)}
                FOR EACH ROW
                WHEN NOT ({src}.{Quote(SyncConstants.ColSyncUuid)} IS NULL)
                BEGIN
                    INSERT INTO change_log
                        (sync_uuid, table_name, op, payload, updated_at, updated_by, applied)
                    VALUES (
                        {src}.{Quote(SyncConstants.ColSyncUuid)},
                        '{EscapeSqlString(table)}',
                        '{EscapeSqlString(op)}',
                        json_object({string.Join(", ", jsonPairs)}),
                        COALESCE({src}.{Quote(SyncConstants.ColUpdatedAt)},
                                 CAST(strftime('%s','now') AS INTEGER) * 1000),
                        COALESCE({src}.{Quote(SyncConstants.ColUpdatedBy)}, 'unknown'),
                        0
                    );
                END;";

            if (table == "PromotionPaths")
            {
                sql = sql.Replace(
                    $"WHEN NOT ({src}.{Quote(SyncConstants.ColSyncUuid)} IS NULL)",
                    $"WHEN NOT ({src}.{Quote(SyncConstants.ColSyncUuid)} IS NULL) " +
                    $"AND ({src}.SchoolId IS NULL OR {src}.SchoolId <> {SyncConstants.SystemDefaultSchoolId})");
            }

            try
            {
                Exec(conn, $"DROP TRIGGER IF EXISTS {Quote(triggerName)};");
                Exec(conn, sql);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.Sync] Failed to create trigger {triggerName}: {ex.Message}");
            }
        }

        // ─── Introspection helpers ────────────────────────────────────

        private static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = new SqliteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@name", table);
            using var r = cmd.ExecuteReader();
            return r.Read();
        }

        private static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = new SqliteCommand($"PRAGMA table_info({Quote(table)})", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string name = r.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<string> GetColumnNames(SqliteConnection conn, string table)
        {
            var result = new List<string>();
            using var cmd = new SqliteCommand($"PRAGMA table_info({Quote(table)})", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(r.GetString(1));
            return result;
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
        private static string EscapeSqlString(string s) => s.Replace("'", "''");
    }
}
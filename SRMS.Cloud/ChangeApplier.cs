#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;
using SRMS.Desktop.Sync;

namespace SRMS.Cloud
{
    /// <summary>
    /// Applies a single sync change (insert/update/delete) to PostgreSQL.
    /// Idempotent per SyncUuid via the SyncChangeReceipts table.
    /// </summary>
    public sealed class ChangeApplier
    {
        private readonly PostgresDbContext _db;

        // Tables a teacher can push. Admin can push anything in SyncConstants.SyncableTables.
        private static readonly HashSet<string> TeacherWritableTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "AcademicPerformance",
            "DailyAttendance",
            "Attendance",
            "Notifications"
        };

        public ChangeApplier(PostgresDbContext db)
        {
            _db = db;
        }

        public async Task<bool> ApplyAsync(ChangeRow change, int schoolId, string role = "admin")
        {
            try
            {
                if (change == null
                    || string.IsNullOrWhiteSpace(change.TableName)
                    || string.IsNullOrWhiteSpace(change.SyncUuid)
                    || !SyncConstants.SyncableTables.Contains(change.TableName))
                    return false;

                if (string.Equals(role, "teacher", StringComparison.OrdinalIgnoreCase)
                    && !TeacherWritableTables.Contains(change.TableName))
                    return false;

                if (change.Payload == null || !PayloadBelongsToSchool(change.Payload, schoolId))
                    return false;

                var receipt = await ClaimReceiptAsync(change);
                if (receipt == ReceiptClaim.Replayed) return true;
                if (receipt == ReceiptClaim.Conflict || receipt == ReceiptClaim.InProgress)
                    return false;

                bool applied = (change.Op?.ToLowerInvariant()) switch
                {
                    "insert" => await ApplyInsertOrUpdateAsync(change),
                    "update" => await ApplyInsertOrUpdateAsync(change),
                    "delete" => await ApplyDeleteAsync(change),
                    _ => false
                };

                if (applied)
                {
                    await MarkReceiptAppliedAsync(change.SyncUuid);
                }
                else
                {
                    await ReleaseReceiptAsync(change.SyncUuid);
                }
                return applied;
            }
            catch (Exception ex)
            {
                if (change != null && !string.IsNullOrWhiteSpace(change.SyncUuid))
                    await ReleaseReceiptAsync(change.SyncUuid);
                Debug.WriteLine($"[ChangeApplier] {change?.TableName}/{change?.Op} {change?.SyncUuid} failed: {ex.Message}");
                return false;
            }
        }

        // ── receipts ────────────────────────────────────────────────

        private async Task<ReceiptClaim> ClaimReceiptAsync(ChangeRow change)
        {
            var existing = await _db.SyncChangeReceipts
                .FirstOrDefaultAsync(r => r.SyncUuid == change.SyncUuid);

            if (existing == null)
            {
                _db.SyncChangeReceipts.Add(new SyncChangeReceipt
                {
                    SyncUuid   = change.SyncUuid,
                    TableName  = change.TableName,
                    Op         = change.Op ?? "",
                    Payload    = change.Payload,
                    Applied    = false,
                    ReceivedAt = DateTime.UtcNow
                });
                try
                {
                    await _db.SaveChangesAsync();
                    return ReceiptClaim.Claimed;
                }
                catch (DbUpdateException)
                {
                    // Race — another request claimed it first
                    _db.Entry(_db.SyncChangeReceipts.Local.First(r => r.SyncUuid == change.SyncUuid)).State = EntityState.Detached;
                    return ReceiptClaim.InProgress;
                }
            }

            bool same = string.Equals(existing.TableName, change.TableName, StringComparison.Ordinal)
                     && string.Equals(existing.Op, change.Op ?? "", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(existing.Payload, change.Payload, StringComparison.Ordinal);
            if (!same) return ReceiptClaim.Conflict;
            if (existing.Applied) return ReceiptClaim.Replayed;
            return ReceiptClaim.InProgress;
        }

        private async Task MarkReceiptAppliedAsync(string syncUuid)
        {
            var r = await _db.SyncChangeReceipts.FirstOrDefaultAsync(x => x.SyncUuid == syncUuid);
            if (r == null) return;
            r.Applied = true;
            await _db.SaveChangesAsync();
        }

        private async Task ReleaseReceiptAsync(string syncUuid)
        {
            try
            {
                var r = await _db.SyncChangeReceipts.FirstOrDefaultAsync(x => x.SyncUuid == syncUuid && !x.Applied);
                if (r == null) return;
                _db.SyncChangeReceipts.Remove(r);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChangeApplier] Could not release sync receipt {syncUuid}: {ex.Message}");
            }
        }

        private enum ReceiptClaim { Claimed, Replayed, Conflict, InProgress }

        // ── apply ──────────────────────────────────────────────────

        private async Task<bool> ApplyInsertOrUpdateAsync(ChangeRow change)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Payload);
            if (dict == null || dict.Count == 0) return false;

            if (dict.Keys.Any(k => !IsSafeIdentifier(k))) return false;

            // Fetch the PK of the target table. Most are "Id", but sync tables
            // use "sync_uuid". We use information_schema to detect.
            string pk = await FindPrimaryKeyColumnAsync(change.TableName);
            bool hasId = pk != null && dict.ContainsKey(pk);

            var columns = new List<string>();
            var valueParams = new List<string>();
            var parameters = new List<object>();

            int i = 0;
            foreach (var kv in dict)
            {
                if (!IsSafeIdentifier(kv.Key)) return false;
                columns.Add($"\"{kv.Key}\"");
                valueParams.Add($"{{{i++}}}");
                parameters.Add(JsonElementToObject(kv.Value));
            }

            StringBuilder sql = new();
            sql.Append($"INSERT INTO \"{change.TableName}\" ({string.Join(", ", columns)}) ");
            sql.Append($"VALUES ({string.Join(", ", valueParams)}) ");

            // ON CONFLICT for Postgres — mirrors SQLite's INSERT OR REPLACE keyed on PK
            if (hasId && pk != null)
            {
                var setClauses = new List<string>();
                foreach (var col in dict.Keys.Where(k => !string.Equals(k, pk, StringComparison.OrdinalIgnoreCase)))
                {
                    setClauses.Add($"\"{col}\" = EXCLUDED.\"{col}\"");
                }
                if (setClauses.Count > 0)
                {
                    sql.Append($"ON CONFLICT (\"{pk}\") DO UPDATE SET {string.Join(", ", setClauses)}");
                }
                else
                {
                    sql.Append($"ON CONFLICT (\"{pk}\") DO NOTHING");
                }
            }

            await _db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());
            return true;
        }

        private async Task<bool> ApplyDeleteAsync(ChangeRow change)
        {
            string keyColumn = change.TableName switch
            {
                "Notifications"       => "SyncId",
                "PublishedReports"    => "SyncId",
                "PublishedReportData" => "SyncId",
                "ScoreSubmissions"    => "SyncId",
                _                     => "sync_uuid"
            };

            bool hasKeyColumn = await HasColumnAsync(change.TableName, keyColumn);
            if (!hasKeyColumn)
            {
                Debug.WriteLine($"[ChangeApplier] {change.TableName} has no '{keyColumn}' column; delete skipped.");
                return false;
            }

            await _db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{change.TableName}\" WHERE \"{keyColumn}\" = {{0}}",
                change.SyncUuid);
            return true;
        }

        // ── helpers ────────────────────────────────────────────────

        private async Task<string> FindPrimaryKeyColumnAsync(string tableName)
        {
            try
            {
                // Postgres: query information_schema for the PK column
                var result = await _db.Database
                    .SqlQueryRaw<string>(
                        @"SELECT kcu.column_name AS ""Value""
                          FROM information_schema.table_constraints tc
                          JOIN information_schema.key_column_usage kcu
                            ON tc.constraint_name = kcu.constraint_name
                           AND tc.table_schema    = kcu.table_schema
                          WHERE tc.constraint_type = 'PRIMARY KEY'
                            AND tc.table_name     = {0}
                          LIMIT 1",
                        tableName)
                    .FirstOrDefaultAsync();
                return string.IsNullOrEmpty(result) ? "Id" : result;
            }
            catch { return "Id"; }
        }

        private async Task<bool> HasColumnAsync(string tableName, string columnName)
        {
            try
            {
                var result = await _db.Database
                    .SqlQueryRaw<long>(
                        @"SELECT COUNT(*) AS ""Value""
                          FROM information_schema.columns
                          WHERE table_name = {0} AND column_name = {1}",
                        tableName, columnName)
                    .FirstOrDefaultAsync();
                return result > 0;
            }
            catch { return false; }
        }

        private static bool PayloadBelongsToSchool(string payload, int schoolId)
        {
            if (schoolId <= 0) return false;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);
            if (dict == null) return false;
            if (!dict.TryGetValue("SchoolId", out var value) && !dict.TryGetValue("schoolId", out value))
                return true;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var payloadSchoolId)
                && payloadSchoolId == schoolId;
        }

        private static bool IsSafeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (char c in value)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        private static object JsonElementToObject(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Null    => DBNull.Value,
                JsonValueKind.String  => el.GetString(),
                JsonValueKind.Number  => el.TryGetInt64(out var l) ? l :
                                         el.TryGetDecimal(out var d) ? d :
                                         el.GetDouble(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Object  => el.GetRawText(),
                JsonValueKind.Array   => el.GetRawText(),
                _ => DBNull.Value
            };
        }
    }
}
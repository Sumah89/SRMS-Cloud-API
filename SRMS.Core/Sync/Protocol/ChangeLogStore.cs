using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using SRMS.Desktop.Data;

namespace SRMS.Desktop.Sync.Protocol
{
    public enum ApplyOutcome
    {
        Inserted,
        Updated,
        Deleted,
        SkippedMissing,
        ConflictLocalWon,
        ConflictTieLocalWon,
        ConflictTieIncomingWon,
        UnknownOp
    }

    public sealed class ChangeLogStore
    {
        private readonly DatabaseHelper _db;
        public ChangeLogStore(DatabaseHelper db) { _db = db; }

        public List<SyncChangeRow> GetPendingForPeer(string peerDeviceId, int limit = 500)
        {
            var dt = _db.ExecuteQuery(@"
                SELECT seq, sync_uuid, table_name, op, payload, updated_at, updated_by
                FROM change_log
                WHERE applied = 0
                  AND seq > (SELECT COALESCE(last_seq_sent, 0) FROM sync_peers WHERE device_id = @peer)
                ORDER BY seq ASC
                LIMIT @limit",
                new Dictionary<string, object>
                {
                    { "@peer",  peerDeviceId },
                    { "@limit", limit }
                });

            var rows = new List<SyncChangeRow>();
            foreach (DataRow r in dt.Rows) rows.Add(MapChangeRow(r));
            return rows;
        }

        public List<SyncChangeRow> GetChangesAfter(long afterSeq, int limit = 500)
        {
            var dt = _db.ExecuteQuery(@"
                SELECT seq, sync_uuid, table_name, op, payload, updated_at, updated_by
                FROM change_log
                WHERE seq > @after
                ORDER BY seq ASC
                LIMIT @limit",
                new Dictionary<string, object>
                {
                    { "@after", afterSeq },
                    { "@limit", limit }
                });

            var rows = new List<SyncChangeRow>();
            foreach (DataRow r in dt.Rows) rows.Add(MapChangeRow(r));
            return rows;
        }

        public long GetMaxSeq()
        {
            object v = _db.ExecuteScalar("SELECT COALESCE(MAX(seq), 0) FROM change_log");
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt64(v);
        }

        public ApplyOutcome Apply(SyncChangeRow change)
        {
            long before = GetMaxSeq();
            ApplyOutcome outcome;
            try
            {
                outcome = ApplyCore(change);
            }
            finally
            {
                long after = GetMaxSeq();
                if (after > before)
                {
                    _db.ExecuteNonQuery(
                        "UPDATE change_log SET applied = 1 WHERE seq > @before AND seq <= @after",
                        new Dictionary<string, object>
                        {
                            { "@before", before },
                            { "@after",  after }
                        });
                }
            }
            return outcome;
        }

        private ApplyOutcome ApplyCore(SyncChangeRow change)
        {
            if (change.Op != "insert" && change.Op != "update" && change.Op != "delete")
                return ApplyOutcome.UnknownOp;

            var existing = _db.ExecuteQuery(
                $"SELECT Id, updated_at, updated_by FROM \"{change.TableName}\" WHERE sync_uuid = @uuid LIMIT 1",
                new Dictionary<string, object> { { "@uuid", change.SyncUuid } });

            bool haveLocal = existing.Rows.Count > 0;

            if (change.Op == "delete")
            {
                if (!haveLocal) return ApplyOutcome.SkippedMissing;
                _db.ExecuteNonQuery(
                    $"DELETE FROM \"{change.TableName}\" WHERE sync_uuid = @uuid",
                    new Dictionary<string, object> { { "@uuid", change.SyncUuid } });
                return ApplyOutcome.Deleted;
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Payload);
            if (payload == null) return ApplyOutcome.UnknownOp;

            if (!haveLocal)
            {
                var cols = new List<string>();
                var vals = new List<string>();
                var p = new Dictionary<string, object>();

                foreach (var kv in payload)
                {
                    if (string.Equals(kv.Key, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(kv.Key, SyncConstants.ColSyncUuid,  StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(kv.Key, SyncConstants.ColUpdatedAt, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(kv.Key, SyncConstants.ColUpdatedBy, StringComparison.OrdinalIgnoreCase)) continue;

                    cols.Add($"\"{kv.Key}\"");
                    vals.Add($"@{kv.Key}");
                    p[$"@{kv.Key}"] = JsonElementToObject(kv.Value);
                }

                cols.Add($"\"{SyncConstants.ColSyncUuid}\"");
                vals.Add("@sync_uuid");
                p["@sync_uuid"] = change.SyncUuid;

                cols.Add($"\"{SyncConstants.ColUpdatedAt}\"");
                vals.Add("@updated_at");
                p["@updated_at"] = change.UpdatedAt;

                cols.Add($"\"{SyncConstants.ColUpdatedBy}\"");
                vals.Add("@updated_by");
                p["@updated_by"] = change.UpdatedBy;

                _db.ExecuteNonQuery(
                    $"INSERT INTO \"{change.TableName}\" ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})",
                    p);
                return ApplyOutcome.Inserted;
            }

            long localAt = Convert.ToInt64(existing.Rows[0]["updated_at"] ?? 0L);
            string localBy = existing.Rows[0]["updated_by"]?.ToString() ?? "";

            bool incomingWins;
            ApplyOutcome outcome;

            if (change.UpdatedAt > localAt)
            {
                incomingWins = true;
                outcome = ApplyOutcome.Updated;
            }
            else if (change.UpdatedAt < localAt)
            {
                incomingWins = false;
                outcome = ApplyOutcome.ConflictLocalWon;
            }
            else
            {
                bool incomingWinsTie = string.CompareOrdinal(change.UpdatedBy, localBy) < 0;
                incomingWins = incomingWinsTie;
                outcome = incomingWinsTie ? ApplyOutcome.ConflictTieIncomingWon : ApplyOutcome.ConflictTieLocalWon;
            }

            if (!incomingWins)
            {
                _db.ExecuteNonQuery(
                    @"INSERT INTO conflict_log
                        (sync_uuid, table_name, field, local_value, incoming_value, resolution, resolved_at)
                      VALUES (@uuid, @tbl, NULL, @local, @incoming, 'local_won', @now)",
                    new Dictionary<string, object>
                    {
                        { "@uuid",     change.SyncUuid },
                        { "@tbl",      change.TableName },
                        { "@local",    $"updated_at={localAt}" },
                        { "@incoming", $"updated_at={change.UpdatedAt}" },
                        { "@now",      SyncColumns.NowMs() }
                    });
                return outcome;
            }

            var sets = new List<string>();
            var up = new Dictionary<string, object>();

            foreach (var kv in payload)
            {
                if (string.Equals(kv.Key, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(kv.Key, SyncConstants.ColSyncUuid,  StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(kv.Key, SyncConstants.ColUpdatedAt, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(kv.Key, SyncConstants.ColUpdatedBy, StringComparison.OrdinalIgnoreCase)) continue;

                sets.Add($"\"{kv.Key}\" = @{kv.Key}");
                up[$"@{kv.Key}"] = JsonElementToObject(kv.Value);
            }

            sets.Add($"\"{SyncConstants.ColUpdatedAt}\" = @updated_at");
            up["@updated_at"] = change.UpdatedAt;

            sets.Add($"\"{SyncConstants.ColUpdatedBy}\" = @updated_by");
            up["@updated_by"] = change.UpdatedBy;

            up["@uuid"] = change.SyncUuid;

            _db.ExecuteNonQuery(
                $"UPDATE \"{change.TableName}\" SET {string.Join(", ", sets)} WHERE sync_uuid = @uuid",
                up);
            return outcome;
        }

        private static SyncChangeRow MapChangeRow(DataRow r)
        {
            return new SyncChangeRow
            {
                Seq       = Convert.ToInt64(r["seq"]),
                SyncUuid  = r["sync_uuid"].ToString(),
                TableName = r["table_name"].ToString(),
                Op        = r["op"].ToString(),
                Payload   = r["payload"].ToString(),
                UpdatedAt = Convert.ToInt64(r["updated_at"]),
                UpdatedBy = r["updated_by"].ToString()
            };
        }

        private static object JsonElementToObject(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Null:   return DBNull.Value;
                case JsonValueKind.String: return el.GetString();
                case JsonValueKind.Number:
                    if (el.TryGetInt64(out long l))    return l;
                    if (el.TryGetDouble(out double d)) return d;
                    return el.GetRawText();
                case JsonValueKind.True:   return 1L;
                case JsonValueKind.False:  return 0L;
                default:                   return el.GetRawText();
            }
        }
    }
}
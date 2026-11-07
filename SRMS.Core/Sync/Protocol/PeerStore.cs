using System;
using System.Collections.Generic;
using System.Data;
using SRMS.Desktop.Data;

namespace SRMS.Desktop.Sync.Protocol
{
    public sealed class PeerState
    {
        public string DeviceId      { get; set; }
        public string DeviceName    { get; set; }
        public int?   SchoolId      { get; set; }
        public long   LastSeqSent   { get; set; }
        public long   LastSeqRecv   { get; set; }
        public long?  LastSeenAt    { get; set; }
    }

    public sealed class PeerStore
    {
        private readonly DatabaseHelper _db;
        public PeerStore(DatabaseHelper db) { _db = db; }

        public PeerState GetOrCreate(string deviceId)
        {
            var dt = _db.ExecuteQuery(
                "SELECT * FROM sync_peers WHERE device_id = @id",
                new Dictionary<string, object> { { "@id", deviceId } });

            if (dt.Rows.Count == 0)
            {
                var p = new PeerState { DeviceId = deviceId };
                _db.ExecuteNonQuery(
                    @"INSERT INTO sync_peers
                        (device_id, device_name, school_id, last_seq_sent, last_seq_recv, last_seen_at)
                      VALUES (@id, NULL, NULL, 0, 0, NULL)",
                    new Dictionary<string, object> { { "@id", deviceId } });
                return p;
            }

            var row = dt.Rows[0];
            return new PeerState
            {
                DeviceId    = row["device_id"].ToString(),
                DeviceName  = row["device_name"] == DBNull.Value ? null : row["device_name"].ToString(),
                SchoolId    = row["school_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["school_id"]),
                LastSeqSent = Convert.ToInt64(row["last_seq_sent"]),
                LastSeqRecv = Convert.ToInt64(row["last_seq_recv"]),
                LastSeenAt  = row["last_seen_at"] == DBNull.Value ? (long?)null : Convert.ToInt64(row["last_seen_at"])
            };
        }

        public void UpdateAfterHandshake(PeerState peer)
        {
            _db.ExecuteNonQuery(
                @"UPDATE sync_peers SET
                    device_name = @name, school_id = @sid,
                    last_seen_at = @now
                  WHERE device_id = @id",
                new Dictionary<string, object>
                {
                    { "@id",   peer.DeviceId },
                    { "@name", peer.DeviceName ?? (object)DBNull.Value },
                    { "@sid",  peer.SchoolId ?? (object)DBNull.Value },
                    { "@now",  SyncColumns.NowMs() }
                });
        }

        public void SetLastSeqSent(string deviceId, long seq)
        {
            _db.ExecuteNonQuery(
                "UPDATE sync_peers SET last_seq_sent = @seq WHERE device_id = @id",
                new Dictionary<string, object> { { "@id", deviceId }, { "@seq", seq } });
        }

        public void SetLastSeqRecv(string deviceId, long seq)
        {
            _db.ExecuteNonQuery(
                "UPDATE sync_peers SET last_seq_recv = @seq WHERE device_id = @id",
                new Dictionary<string, object> { { "@id", deviceId }, { "@seq", seq } });
        }
    }
}
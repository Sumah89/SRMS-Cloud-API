using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using SRMS.Desktop.Data;

namespace SRMS.Desktop.Sync
{
    public sealed class DeviceInfo
    {
        public string DeviceId     { get; set; }
        public string FriendlyName { get; set; }
        public int?   SchoolId     { get; set; }
        public string Platform     { get; set; }
        public long   CreatedAt    { get; set; }
        public long   FirstSeenAt  { get; set; }
        public long?  LastSyncAt   { get; set; }
    }

    public static class DeviceInfoStore
    {
        public static DeviceInfo Current { get; private set; }

        public static DeviceInfo Ensure(DatabaseHelper db)
        {
            if (Current != null) return Current;

            DataTable dt = db.ExecuteQuery(
                "SELECT DeviceId, FriendlyName, SchoolId, Platform, CreatedAt, FirstSeenAt, LastSyncAt " +
                "FROM DeviceInfo LIMIT 1");

            if (dt.Rows.Count == 0)
            {
                string id       = Guid.NewGuid().ToString("N");
                string platform = DetectPlatform();
                string name     = DefaultFriendlyName(platform);
                long   now      = SyncColumns.NowMs();

                db.ExecuteNonQuery(
                    "INSERT INTO DeviceInfo (DeviceId, FriendlyName, SchoolId, Platform, CreatedAt, FirstSeenAt, LastSyncAt) " +
                    "VALUES (@Id, @Name, NULL, @Platform, @Now, @Now, NULL)",
                    new Dictionary<string, object>
                    {
                        { "@Id",       id       },
                        { "@Name",     name     },
                        { "@Platform", platform },
                        { "@Now",      now      },
                    });

                Current = new DeviceInfo
                {
                    DeviceId     = id,
                    FriendlyName = name,
                    SchoolId     = null,
                    Platform     = platform,
                    CreatedAt    = now,
                    FirstSeenAt  = now,
                    LastSyncAt   = null,
                };
            }
            else
            {
                var row = dt.Rows[0];
                Current = new DeviceInfo
                {
                    DeviceId     = row["DeviceId"].ToString(),
                    FriendlyName = row["FriendlyName"].ToString(),
                    SchoolId     = row["SchoolId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["SchoolId"]),
                    Platform     = row["Platform"].ToString(),
                    CreatedAt    = Convert.ToInt64(row["CreatedAt"]),
                    FirstSeenAt  = Convert.ToInt64(row["FirstSeenAt"]),
                    LastSyncAt   = row["LastSyncAt"] == DBNull.Value ? (long?)null : Convert.ToInt64(row["LastSyncAt"]),
                };
            }

            SyncColumns.LocalDeviceId = Current.DeviceId;
            return Current;
        }

        public static void SetFriendlyName(DatabaseHelper db, string name)
        {
            if (Current == null) throw new InvalidOperationException("DeviceInfoStore not initialized.");
            if (string.IsNullOrWhiteSpace(name)) return;

            db.ExecuteNonQuery(
                "UPDATE DeviceInfo SET FriendlyName = @Name WHERE DeviceId = @Id",
                new Dictionary<string, object>
                {
                    { "@Name", name.Trim() },
                    { "@Id",   Current.DeviceId },
                });

            Current.FriendlyName = name.Trim();
        }

        public static void SetSchoolId(DatabaseHelper db, int schoolId)
        {
            if (Current == null) throw new InvalidOperationException("DeviceInfoStore not initialized.");

            db.ExecuteNonQuery(
                "UPDATE DeviceInfo SET SchoolId = @Sid WHERE DeviceId = @Id",
                new Dictionary<string, object>
                {
                    { "@Sid", schoolId },
                    { "@Id",  Current.DeviceId },
                });

            Current.SchoolId = schoolId;
        }

        public static void TouchLastSync(DatabaseHelper db)
        {
            if (Current == null) return;
            long now = SyncColumns.NowMs();
            db.ExecuteNonQuery(
                "UPDATE DeviceInfo SET LastSyncAt = @Now WHERE DeviceId = @Id",
                new Dictionary<string, object>
                {
                    { "@Now", now },
                    { "@Id",  Current.DeviceId },
                });
            Current.LastSyncAt = now;
        }

        private static string DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return "macos";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return "linux";
            return "unknown";
        }

        private static string DefaultFriendlyName(string platform)
        {
            try
            {
                string machine = Environment.MachineName;
                if (!string.IsNullOrWhiteSpace(machine))
                    return $"{platform.ToUpperInvariant()}-{machine}";
            }
            catch { }

            return $"{platform.ToUpperInvariant()}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }
    }
}
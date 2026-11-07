using System;
using System.Collections.Generic;

namespace SRMS.Desktop.Sync
{
    /// <summary>
    /// Helpers for the three sync columns and for the local device identity.
    /// LocalDeviceId is set once at startup by DeviceInfoStore.Ensure().
    /// </summary>
    public static class SyncColumns
    {
        public static string NewUuid() => Guid.NewGuid().ToString("N");

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static string LocalDeviceId { get; internal set; }

        public static Dictionary<string, object> NewInsertColumns()
        {
            if (string.IsNullOrEmpty(LocalDeviceId))
                throw new InvalidOperationException(
                    "SyncColumns.LocalDeviceId is not set. " +
                    "Call DeviceInfoStore.Ensure() at startup before any write.");

            return new Dictionary<string, object>
            {
                { "@sync_uuid",  NewUuid() },
                { "@updated_at", NowMs() },
                { "@updated_by", LocalDeviceId },
            };
        }

        public static Dictionary<string, object> NewUpdateColumns()
        {
            if (string.IsNullOrEmpty(LocalDeviceId))
                throw new InvalidOperationException(
                    "SyncColumns.LocalDeviceId is not set.");

            return new Dictionary<string, object>
            {
                { "@updated_at", NowMs() },
                { "@updated_by", LocalDeviceId },
            };
        }
    }
}
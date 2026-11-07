using System;
using System.Diagnostics;
using SRMS.Desktop.Sync;

namespace SRMS.Desktop.Data
{
    public partial class DatabaseHelper
    {
        public void EnsureSyncSchemaPublic()
        {
            var conn = GetConnection();
            try { SyncSchema.Ensure(conn); }
            catch (Exception ex) { Debug.WriteLine($"EnsureSyncSchemaPublic: {ex.Message}"); throw; }
            finally { CloseConnection(conn); }
        }
    }
}
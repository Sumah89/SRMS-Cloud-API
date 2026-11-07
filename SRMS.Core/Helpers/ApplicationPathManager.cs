using System;
using System.Diagnostics;
using System.IO;

namespace SRMS.Desktop.Helpers
{
    public static class ApplicationPathManager
    {
        private static string _applicationDataFolder;
        private static string _databasePath;

        public static string ApplicationDataFolder
        {
            get
            {
                EnsureInitialized();
                return _applicationDataFolder;
            }
        }

        public static string DatabasePath
        {
            get
            {
                EnsureInitialized();
                return _databasePath;
            }
        }

        public static void EnsureInitialized()
        {
            if (!string.IsNullOrEmpty(_databasePath)) return;

            try
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
                _applicationDataFolder = Path.Combine(basePath, "AppData");
                Directory.CreateDirectory(_applicationDataFolder);
                _databasePath = Path.Combine(_applicationDataFolder, "SRMS.Desktop.db");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Application path initialization failed: {ex.Message}");
                _applicationDataFolder = Path.GetTempPath();
                _databasePath = Path.Combine(_applicationDataFolder, "SRMS.Desktop.db");
            }
        }
    }
}
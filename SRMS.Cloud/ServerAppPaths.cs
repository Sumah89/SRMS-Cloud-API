#nullable disable
using System;
using System.IO;
using SRMS.Desktop.Abstractions;

namespace SRMS.Cloud
{
    public sealed class ServerAppPaths : IAppPaths
    {
        private readonly string _root;

        public ServerAppPaths(string root)
        {
            _root = root;
            if (!Directory.Exists(_root))
                Directory.CreateDirectory(_root);
        }

        public string AppDataFolder => _root;
        public string DatabaseFolder => _root;
        public string DatabaseFilePath => Path.Combine(_root, "srms.db");
        public string LogsFolder => EnsureFolder(Path.Combine(_root, "logs"));
        public string StudentPhotosFolder => EnsureFolder(Path.Combine(_root, "photos"));
        public string BackupsFolder => EnsureFolder(Path.Combine(_root, "backups"));
        public string TempFolder => EnsureFolder(Path.Combine(_root, "temp"));

        public string EnsureFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return null;
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        public string CombineAndEnsure(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return _root;
            string combined = Path.Combine(_root, relativePath);
            EnsureFolder(combined);
            return combined;
        }
    }
}
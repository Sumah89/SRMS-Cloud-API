using System;

namespace SRMS.Desktop.Abstractions
{
    /// <summary>
    /// Platform-agnostic file system path provider.
    ///
    /// Desktop implementation: uses AppDomain.CurrentDomain.BaseDirectory
    /// and the app installation folder.
    ///
    /// Mobile implementation: uses MAUI's FileSystem.AppDataDirectory which
    /// is a private sandboxed folder on Android.
    ///
    /// Every place in the codebase that currently reads Global.AppDataPath,
    /// ApplicationPathManager.DatabasePath, or builds a file path with
    /// Path.Combine(Global.AppDataPath, ...) MUST go through this interface.
    /// </summary>
    public interface IAppPaths
    {
        /// <summary>
        /// Root folder for all app data (database, logs, backups, photos).
        /// Desktop: install folder. Mobile: sandbox app data folder.
        /// Guaranteed to exist and be writable.
        /// </summary>
        string AppDataFolder { get; }

        /// <summary>Folder containing the SQLite database file.</summary>
        string DatabaseFolder { get; }

        /// <summary>Full path to the SQLite database file.</summary>
        string DatabaseFilePath { get; }

        /// <summary>Folder for rotating log files.</summary>
        string LogsFolder { get; }

        /// <summary>Folder for student photographs.</summary>
        string StudentPhotosFolder { get; }

        /// <summary>Folder for database backups.</summary>
        string BackupsFolder { get; }

        /// <summary>Folder for temporary work files (safe to delete).</summary>
        string TempFolder { get; }

        /// <summary>
        /// Ensures the folder exists, creating it if necessary.
        /// Returns the canonical path. Never returns null.
        /// </summary>
        string EnsureFolder(string folderPath);

        /// <summary>
        /// Combines AppDataFolder with a relative path safely, ensuring
        /// the parent folder exists. Use this instead of Path.Combine
        /// so mobile and desktop produce consistent results.
        /// </summary>
        string CombineAndEnsure(string relativePath);
    }
}
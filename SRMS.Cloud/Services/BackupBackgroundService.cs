using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRMS.Desktop.Data;

namespace SRMS.Cloud.Services
{
    public class BackupBackgroundService : BackgroundService
    {
        private readonly ILogger<BackupBackgroundService> _logger;
        private readonly string _backupFolder;

        public BackupBackgroundService(ILogger<BackupBackgroundService> logger)
        {
            _logger = logger;
            // Save backups in a 'backups' folder next to the database
            _backupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(_backupFolder)) Directory.CreateDirectory(_backupFolder);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Cloud Automated Backup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Run every 24 hours
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

                    PerformBackup();
                }
                catch (TaskCanceledException)
                {
                    // App is shutting down
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during automated database backup.");
                }
            }
        }

        private void PerformBackup()
        {
            try
            {
                string dbPath = DatabaseHelper.DatabasePath;
                if (!File.Exists(dbPath)) return;

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(_backupFolder, $"srms_backup_{timestamp}.db");

                // SQLite WAL mode requires us to checkpoint before copying to avoid corruption
                using (var db = new DatabaseHelper())
                {
                    db.ExecuteNonQuery("PRAGMA wal_checkpoint(FULL);");
                }
                
                DatabaseHelper.CloseAllConnections(); // Close to release file lock
                
                File.Copy(dbPath, backupPath, true);
                
                _logger.LogInformation($"Automated backup created successfully: {backupPath}");

                // Cleanup: Keep only the last 7 backups to save space
                var files = Directory.GetFiles(_backupFolder, "srms_backup_*.db");
                if (files.Length > 7)
                {
                    Array.Sort(files); // Sort by date (filename)
                    for (int i = 0; i < files.Length - 7; i++)
                    {
                        File.Delete(files[i]);
                        _logger.LogInformation($"Deleted old backup: {files[i]}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Backup failed: {ex.Message}");
            }
        }
    }
}
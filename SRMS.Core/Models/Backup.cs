using System;

namespace SRMS.Desktop.Models
{
    public class Backup
    {
        public int Id { get; set; }
        public string BackupName { get; set; }
        public DateTime BackupDate { get; set; }
        public long FileSize { get; set; }
        public string BackupPath { get; set; }
        public string BackupType { get; set; }
        public string BackupBy { get; set; }
    }
}
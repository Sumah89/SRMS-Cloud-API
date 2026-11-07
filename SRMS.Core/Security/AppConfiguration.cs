using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SRMS.Desktop.Security
{
    public static class AppConfiguration
    {
        private static string _configPath;
        private static readonly object _lockObject = new object();

        // Default values
        public static bool EnableThreatMonitoring { get; set; } = true;
        public static bool EnableAuditLogging { get; set; } = true;
        public static bool EnableKeyboardShortcuts { get; set; } = true;
        public static bool RequireStrongPasswords { get; set; } = true;
        public static int PasswordMinLength { get; set; } = 6;
        public static int SessionTimeoutMinutes { get; set; } = 30;
        public static bool EnableAutoBackup { get; set; } = true;
        public static int AutoBackupIntervalDays { get; set; } = 7;
        public static bool EnableDatabaseEncryption { get; set; } = false;
        public static bool EnableIPWhitelist { get; set; } = false;
        public static string[] AllowedIPs { get; set; } = new string[0];

        // Additional settings
        public static bool EnableAutoSave { get; set; } = true;
        public static int AutoSaveIntervalSeconds { get; set; } = 30;
        public static int MaxLoginAttempts { get; set; } = 5;
        public static int LockoutMinutes { get; set; } = 15;
        public static int MaxBackupFiles { get; set; } = 10;
        public static bool EnableDashboardAnalytics { get; set; } = true;
        public static string Theme { get; set; } = "Light";
        public static int LoadingOverlayTimeoutSeconds { get; set; } = 30;
        public static bool AutoGenerateUserManual { get; set; } = true;
        public static string ReportPaperOrientation { get; set; } = "Portrait";
        public static bool ShowBehaviourSection { get; set; } = true;
        public static bool ShowPsychomotorSection { get; set; } = true;
        public static bool ShowAttendanceSection { get; set; } = true;

        static AppConfiguration()
        {
            LoadConfiguration();
        }

        private static void LoadConfiguration()
        {
            try
            {
                string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SRMS.Desktop");
                if (!Directory.Exists(appDataPath))
                    Directory.CreateDirectory(appDataPath);

                _configPath = Path.Combine(appDataPath, "appsettings.cfg");

                if (File.Exists(_configPath))
                {
                    string[] lines = File.ReadAllLines(_configPath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                            continue;

                        int equalsIndex = line.IndexOf('=');
                        if (equalsIndex <= 0) continue;

                        string key = line.Substring(0, equalsIndex).Trim();
                        string value = line.Substring(equalsIndex + 1).Trim();

                        switch (key.ToLower())
                        {
                            case "enablethreatmonitoring":
                                bool.TryParse(value, out bool etm);
                                EnableThreatMonitoring = etm;
                                break;
                            case "enableauditlogging":
                                bool.TryParse(value, out bool eal);
                                EnableAuditLogging = eal;
                                break;
                            case "enablekeyboardshortcuts":
                                bool.TryParse(value, out bool eks);
                                EnableKeyboardShortcuts = eks;
                                break;
                            case "requirestrongpasswords":
                                bool.TryParse(value, out bool rsp);
                                RequireStrongPasswords = rsp;
                                break;
                            case "passwordminlength":
                                int.TryParse(value, out int pml);
                                if (pml > 0) PasswordMinLength = pml;
                                break;
                            case "sessiontimeoutminutes":
                                int.TryParse(value, out int stm);
                                if (stm > 0) SessionTimeoutMinutes = stm;
                                break;
                            case "enableautobackup":
                                bool.TryParse(value, out bool eab);
                                EnableAutoBackup = eab;
                                break;
                            case "autobackupintervaldays":
                                int.TryParse(value, out int abid);
                                if (abid > 0) AutoBackupIntervalDays = abid;
                                break;
                            case "enabledatabaseencryption":
                                bool.TryParse(value, out bool ede);
                                EnableDatabaseEncryption = ede;
                                break;
                            case "enableipwhitelist":
                                bool.TryParse(value, out bool eiw);
                                EnableIPWhitelist = eiw;
                                break;
                            case "allowedips":
                                if (!string.IsNullOrEmpty(value))
                                    AllowedIPs = value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                break;
                            case "enableautosave":
                                bool.TryParse(value, out bool eas);
                                EnableAutoSave = eas;
                                break;
                            case "autosaveintervalseconds":
                                int.TryParse(value, out int asi);
                                if (asi > 0) AutoSaveIntervalSeconds = asi;
                                break;
                            case "maxloginattempts":
                                int.TryParse(value, out int mla);
                                if (mla > 0) MaxLoginAttempts = mla;
                                break;
                            case "lockoutminutes":
                                int.TryParse(value, out int lm);
                                if (lm > 0) LockoutMinutes = lm;
                                break;
                            case "maxbackupfiles":
                                int.TryParse(value, out int mbf);
                                if (mbf > 0) MaxBackupFiles = mbf;
                                break;
                            case "theme":
                                Theme = value;
                                break;
                            case "reportpaperorientation":
                                ReportPaperOrientation = value;
                                break;
                        }
                    }
                }
                else
                {
                    SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading configuration: {ex.Message}");
            }
        }

        public static void SaveConfiguration()
        {
            try
            {
                lock (_lockObject)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("# School Result System Configuration File");
                    sb.AppendLine("# ======================================");
                    sb.AppendLine($"# Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== SECURITY SETTINGS ==========");
                    sb.AppendLine($"EnableThreatMonitoring={EnableThreatMonitoring}");
                    sb.AppendLine($"EnableAuditLogging={EnableAuditLogging}");
                    sb.AppendLine($"EnableKeyboardShortcuts={EnableKeyboardShortcuts}");
                    sb.AppendLine($"RequireStrongPasswords={RequireStrongPasswords}");
                    sb.AppendLine($"PasswordMinLength={PasswordMinLength}");
                    sb.AppendLine($"SessionTimeoutMinutes={SessionTimeoutMinutes}");
                    sb.AppendLine($"MaxLoginAttempts={MaxLoginAttempts}");
                    sb.AppendLine($"LockoutMinutes={LockoutMinutes}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== BACKUP SETTINGS ==========");
                    sb.AppendLine($"EnableAutoBackup={EnableAutoBackup}");
                    sb.AppendLine($"AutoBackupIntervalDays={AutoBackupIntervalDays}");
                    sb.AppendLine($"MaxBackupFiles={MaxBackupFiles}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== DATABASE SECURITY ==========");
                    sb.AppendLine($"EnableDatabaseEncryption={EnableDatabaseEncryption}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== IP WHITELIST ==========");
                    sb.AppendLine($"EnableIPWhitelist={EnableIPWhitelist}");
                    sb.AppendLine($"AllowedIPs={string.Join(",", AllowedIPs)}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== UI SETTINGS ==========");
                    sb.AppendLine($"EnableAutoSave={EnableAutoSave}");
                    sb.AppendLine($"AutoSaveIntervalSeconds={AutoSaveIntervalSeconds}");
                    sb.AppendLine($"Theme={Theme}");
                    sb.AppendLine($"EnableDashboardAnalytics={EnableDashboardAnalytics}");
                    sb.AppendLine($"LoadingOverlayTimeoutSeconds={LoadingOverlayTimeoutSeconds}");
                    sb.AppendLine();
                    sb.AppendLine("# ========== REPORT SETTINGS ==========");
                    sb.AppendLine($"ReportPaperOrientation={ReportPaperOrientation}");
                    sb.AppendLine($"ShowBehaviourSection={ShowBehaviourSection}");
                    sb.AppendLine($"ShowPsychomotorSection={ShowPsychomotorSection}");
                    sb.AppendLine($"ShowAttendanceSection={ShowAttendanceSection}");
                    sb.AppendLine($"AutoGenerateUserManual={AutoGenerateUserManual}");

                    File.WriteAllText(_configPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        public static string GetConfigurationSummary()
        {
            return $"Threat Monitoring: {EnableThreatMonitoring}\n" +
                   $"Audit Logging: {EnableAuditLogging}\n" +
                   $"Keyboard Shortcuts: {EnableKeyboardShortcuts}\n" +
                   $"Strong Passwords: {RequireStrongPasswords}\n" +
                   $"Password Min Length: {PasswordMinLength}\n" +
                   $"Session Timeout: {SessionTimeoutMinutes} minutes\n" +
                   $"Max Login Attempts: {MaxLoginAttempts}\n" +
                   $"Lockout Duration: {LockoutMinutes} minutes\n" +
                   $"Auto Backup: {(EnableAutoBackup ? $"{AutoBackupIntervalDays} days" : "Disabled")}\n" +
                   $"Max Backup Files: {MaxBackupFiles}\n" +
                   $"DB Encryption: {EnableDatabaseEncryption}\n" +
                   $"IP Whitelist: {(EnableIPWhitelist ? $"{AllowedIPs.Length} IPs" : "Disabled")}\n" +
                   $"Auto Save: {(EnableAutoSave ? $"{AutoSaveIntervalSeconds} sec" : "Disabled")}\n" +
                   $"Theme: {Theme}\n" +
                   $"Paper Orientation: {ReportPaperOrientation}";
        }

        public static bool IsIPAllowed(string ipAddress)
        {
            if (!EnableIPWhitelist || AllowedIPs == null || AllowedIPs.Length == 0)
                return true;

            if (AllowedIPs.Contains("*"))
                return true;

            return AllowedIPs.Contains(ipAddress);
        }

        public static void AddAllowedIP(string ipAddress)
        {
            var list = new List<string>(AllowedIPs);
            if (!list.Contains(ipAddress))
            {
                list.Add(ipAddress);
                AllowedIPs = list.ToArray();
                SaveConfiguration();
            }
        }

        public static void RemoveAllowedIP(string ipAddress)
        {
            var list = new List<string>(AllowedIPs);
            if (list.Contains(ipAddress))
            {
                list.Remove(ipAddress);
                AllowedIPs = list.ToArray();
                SaveConfiguration();
            }
        }

        public static void ResetToDefaults()
        {
            EnableThreatMonitoring = true;
            EnableAuditLogging = true;
            EnableKeyboardShortcuts = true;
            RequireStrongPasswords = true;
            PasswordMinLength = 6;
            SessionTimeoutMinutes = 30;
            MaxLoginAttempts = 5;
            LockoutMinutes = 15;
            EnableAutoBackup = true;
            AutoBackupIntervalDays = 7;
            MaxBackupFiles = 10;
            EnableDatabaseEncryption = false;
            EnableIPWhitelist = false;
            AllowedIPs = new string[0];
            EnableAutoSave = true;
            AutoSaveIntervalSeconds = 30;
            Theme = "Light";
            EnableDashboardAnalytics = true;
            ReportPaperOrientation = "Portrait";
            ShowBehaviourSection = true;
            ShowPsychomotorSection = true;
            ShowAttendanceSection = true;
            AutoGenerateUserManual = true;
            SaveConfiguration();
        }
    }
}
using System;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Models;
using SRMS.Desktop.Security;

namespace SRMS.Desktop
{
    /// <summary>
    /// Portable replacement for the desktop Global class.
    ///
    /// Differences from the desktop version:
    ///   - AppDataPath / DocumentsPath / LogsPath / TemplatesPath now delegate
    ///     to an injected IAppPaths implementation. The desktop provides a
    ///     folder-based implementation; mobile provides a MAUI sandbox path.
    ///   - No direct AppDomain.CurrentDomain.BaseDirectory usage.
    ///
    /// The rest of the API is preserved so existing callers compile unchanged.
    /// </summary>
    public static class Global
    {
        private static UserSession _currentUser;
        private static readonly object _lock = new object();

        /// <summary>
        /// Injected by the platform at startup (Program.cs on desktop,
        /// MauiProgram.cs on mobile). Must never be null after startup.
        /// </summary>
        public static IAppPaths Paths { get; set; }

        /// <summary>
        /// Optional lifecycle hook. Logger uses it to pause flushing when
        /// the app is backgrounded on mobile. Null-safe on desktop.
        /// </summary>
        public static IAppLifecycle Lifecycle { get; set; }

        public static UserSession CurrentUser
        {
            get => _currentUser;
            set
            {
                lock (_lock)
                {
                    _currentUser = value;
                }
            }
        }

        public static string AppVersion => "3.0.0";

        public static string CurrentAcademicYear
        {
            get
            {
                int year = DateTime.Now.Year;
                int nextYear = year + 1;
                return $"{year}/{nextYear}";
            }
        }

        public static string AppDataPath
        {
            get
            {
                EnsurePaths();
                return Paths.AppDataFolder;
            }
        }

        public static string DocumentsPath
        {
            get
            {
                EnsurePaths();
                return Paths.CombineAndEnsure("Documents");
            }
        }

        public static string LogsPath
        {
            get
            {
                EnsurePaths();
                return Paths.LogsFolder;
            }
        }

        public static string TemplatesPath
        {
            get
            {
                EnsurePaths();
                return Paths.CombineAndEnsure("Templates");
            }
        }

        // ========== SUBSCRIPTION HELPERS ==========

        public static bool IsTrialActive() => false;

        public static bool IsTrialExpired() => false;

        public static int GetTrialDaysRemaining() => 0;

        public static bool IsLicensed() => CurrentUser?.SchoolId.HasValue == true;

        public static bool CanAddMoreStudents(int currentStudentCount)
        {
            return IsLicensed();
        }

        public static string GetLicenseStatusMessage()
        {
            return IsLicensed()
                ? "Subscription status is available offline and synchronized with the cloud when connected."
                : "No school subscription is available.";
        }

        /// <summary>
        /// Report-card template lookup. Portable version — resolves
        /// the templates folder via IAppPaths. The desktop version
        /// also checked AppDomain.BaseDirectory; that is now provided
        /// by the IAppPaths implementation itself.
        /// </summary>
        public static string GetReportCardTemplatePath(int schoolId)
        {
            try
            {
                using (var dbHelper = new Data.DatabaseHelper())
                {
                    var school = dbHelper.GetSchoolById(schoolId);
                    string resultType = school?.ResultType ?? "FiveTests";
                    string templateName = resultType == "SixTests"
                        ? "SixTestReportCardTemplate.xlsx"
                        : "FiveTestsReportCardTemplate.xlsx";
                    string templatePath = System.IO.Path.Combine(TemplatesPath, templateName);

                    if (System.IO.File.Exists(templatePath))
                        return templatePath;

                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static string EnsureTemplateExists()
        {
            string resultType = CurrentUser?.SchoolResultType ?? "FiveTests";
            string templateName = resultType == "SixTests"
                ? "SixTestReportCardTemplate.xlsx"
                : "FiveTestsReportCardTemplate.xlsx";
            string templatePath = System.IO.Path.Combine(TemplatesPath, templateName);

            if (System.IO.File.Exists(templatePath))
                return templatePath;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string resourceName = resultType == "SixTests"
                    ? "SRMS.Desktop.Resources.SixTestReportCardTemplate.xlsx"
                    : "SRMS.Desktop.Resources.FiveTestsReportCardTemplate.xlsx";

                using (System.IO.Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resourceStream != null)
                    {
                        if (!System.IO.Directory.Exists(TemplatesPath))
                            System.IO.Directory.CreateDirectory(TemplatesPath);

                        using (System.IO.FileStream fileStream = new System.IO.FileStream(
                            templatePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                        {
                            resourceStream.CopyTo(fileStream);
                        }
                        return templatePath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract template: {ex.Message}");
            }

            return null;
        }

        public static void InvalidateCache()
        {
            // No cached paths to invalidate — IAppPaths owns that.
        }

        public static string GetDisplaySchoolInfo()
        {
            try
            {
                string name = null;

                var gu = CurrentUser;
                if (string.IsNullOrEmpty(name) && gu != null && !string.IsNullOrEmpty(gu.SchoolName))
                    name = gu.SchoolName.Trim();

                string district = gu?.District?.Trim();
                string town = gu?.Town?.Trim();

                if (string.IsNullOrEmpty(name)) return null;

                string result = name;
                if (!string.IsNullOrEmpty(district) || !string.IsNullOrEmpty(town))
                {
                    result += " - ";
                    if (!string.IsNullOrEmpty(district)) result += district;
                    if (!string.IsNullOrEmpty(town)) result += (string.IsNullOrEmpty(district) ? "" : ", ") + town;
                }
                return result;
            }
            catch { return null; }
        }

        private static void EnsurePaths()
        {
            if (Paths == null)
                throw new InvalidOperationException(
                    "Global.Paths was not initialized. " +
                    "Desktop: set it in Program.Main() before using Global.AppDataPath. " +
                    "Mobile: set it in MauiProgram.CreateMauiApp().");
        }
    }

    /// <summary>
    /// Preserved for compatibility — same shape as desktop version.
    /// </summary>
    public class UserSession
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public int? SchoolId { get; set; }
        public string SchoolName { get; set; }
        public string District { get; set; }
        public string Town { get; set; }
        public string Chiefdom { get; set; }
        public DateTime SubscriptionExpiryDate { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string PrincipalName { get; set; }
        public string SchoolResultType { get; set; } = "FiveTests";
        public bool IsTrial => false;
        public bool IsLicensed => SchoolId.HasValue;

        public static UserSession FromUser(User user, School school)
        {
            return new UserSession
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role,
                SchoolId = user.SchoolId,
                SchoolName = school?.SchoolName,
                District = school?.District,
                Town = school?.Town,
                Chiefdom = school?.Chiefdom,
                SubscriptionExpiryDate = school?.SubscriptionExpiryDate ?? DateTime.MaxValue,
                Phone = user.Phone,
                Email = user.Email,
                PrincipalName = school?.PrincipalName,
                SchoolResultType = school?.ResultType ?? "FiveTests"
            };
        }
    }
}
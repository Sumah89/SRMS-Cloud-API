using System;
using SRMS.Desktop.Data;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Single place that answers "which ResultType does this class use?"
    /// Resolution order:
    ///   1. Class-level ResultTypeOverride (if set on the class)
    ///   2. School-wide ResultType
    ///   3. "FiveTests" (ultimate fallback)
    /// </summary>
    public static class ResultTypeResolver
    {
        private static DatabaseHelper _db;

        static ResultTypeResolver()
        {
            _db = new DatabaseHelper();
        }

        public const string FiveTests = "FiveTests";
        public const string SixTests = "SixTests";
        public const string SixTests15 = "SixTests15";

        /// <summary>
        /// Resolves the effective ResultType for a class.
        /// </summary>
        public static string Resolve(int schoolId, int? classId)
        {
            // 1. Try class-level override
            if (classId.HasValue && classId.Value > 0)
            {
                try
                {
                    var classObj = _db.GetClassById(classId.Value);
                    if (classObj != null && !string.IsNullOrWhiteSpace(classObj.ResultTypeOverride))
                        return Normalize(classObj.ResultTypeOverride);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ResultTypeResolver class lookup error: {ex.Message}");
                }
            }

            // 2. School-wide
            if (schoolId > 0)
            {
                try
                {
                    var school = _db.GetSchoolById(schoolId);
                    if (school != null && !string.IsNullOrWhiteSpace(school.ResultType))
                        return Normalize(school.ResultType);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ResultTypeResolver school lookup error: {ex.Message}");
                }
            }

            // 3. Default
            return FiveTests;
        }

        /// <summary>
        /// Convenience: resolve by class object (skips one DB lookup when the
        /// caller already has the Class loaded).
        /// </summary>
        public static string Resolve(Models.Class classObj, int schoolId)
        {
            if (classObj != null && !string.IsNullOrWhiteSpace(classObj.ResultTypeOverride))
                return Normalize(classObj.ResultTypeOverride);

            if (schoolId > 0)
            {
                try
                {
                    var school = _db.GetSchoolById(schoolId);
                    if (school != null && !string.IsNullOrWhiteSpace(school.ResultType))
                        return Normalize(school.ResultType);
                }
                catch { }
            }

            return FiveTests;
        }

        /// <summary>
        /// Falls back to Global.CurrentUser's school when only a class is known.
        /// </summary>
        public static string ResolveForCurrentSchool(int? classId)
        {
            int schoolId = Global.CurrentUser?.SchoolId ?? 0;
            return Resolve(schoolId, classId);
        }

        /// <summary>True if the effective type is SixTests.</summary>
        public static bool IsSixTests(int schoolId, int? classId)
            => string.Equals(Resolve(schoolId, classId), SixTests, StringComparison.OrdinalIgnoreCase);

        /// <summary>True if the effective type is FiveTests.</summary>
        public static bool IsFiveTests(int schoolId, int? classId)
            => string.Equals(Resolve(schoolId, classId), FiveTests, StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return FiveTests;
            string v = value.Trim();
            if (v.Equals("SixTests", StringComparison.OrdinalIgnoreCase)) return SixTests;
            if (v.Equals("SixTests15", StringComparison.OrdinalIgnoreCase)) return SixTests15;
            if (v.Equals("FiveTests", StringComparison.OrdinalIgnoreCase)) return FiveTests;
            return FiveTests; // unknown → default
        }
    }
}
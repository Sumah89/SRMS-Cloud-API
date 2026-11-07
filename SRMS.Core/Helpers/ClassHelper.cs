using System;
using SRMS.Desktop.Services;

namespace SRMS.Desktop.Helpers
{
    public static class ClassHelper
    {
        /// </summary>
        public static string GetBaseClass(string fullClassName)
        {
            if (string.IsNullOrEmpty(fullClassName)) return fullClassName;

            string trimmed = fullClassName.Trim();
            string upper = trimmed.ToUpper();

            if (upper.Contains("GRADUATED"))
                return "GRADUATED";

            if (upper.Contains("JSS III")) return "JSS III";
            if (upper.Contains("JSS II")) return "JSS II";
            if (upper.Contains("JSS I")) return "JSS I";

            if (upper.Contains("SSS III")) return "SSS III";
            if (upper.Contains("SSS II")) return "SSS II";
            if (upper.Contains("SSS I")) return "SSS I";

            return trimmed;
        }

        /// <summary>
        /// Gets the next class name for promotion.
        /// Consults the PromotionPaths table first (via PromotionPathService),
        /// falling back to the hardcoded defaults.
        /// Stream (A, B, Sci, Arts, etc.) is dropped on promotion by design.
        /// </summary>
        public static string GetNextClass(string fullClassName)
        {
            int schoolId = Global.CurrentUser?.SchoolId ?? 0;
            return GetNextClass(schoolId, fullClassName);
        }

        /// <summary>
        /// Gets the next class name for a specific school's promotion paths.
        /// </summary>
        public static string GetNextClass(int schoolId, string fullClassName)
        {
            if (string.IsNullOrEmpty(fullClassName)) return fullClassName;

            string baseClass = GetBaseClass(fullClassName);

            try
            {
                // PromotionPathService handles caching internally.
                return PromotionPathService.GetNextBaseClass(schoolId, baseClass);
            }
            catch
            {
                // If the service is unavailable for any reason, use hardcoded defaults.
                return GetNextBaseClassHardcoded(baseClass);
            }
        }

        /// <summary>
        /// Gets the next base class name (without stream).
        /// Hardcoded fallback. Promotion flow: JSS I → JSS II → JSS III → SSS I → SSS II → SSS III → GRADUATED
        /// </summary>
        private static string GetNextBaseClassHardcoded(string baseClass)
        {
            if (string.IsNullOrEmpty(baseClass)) return baseClass;

            string normalized = baseClass.Trim().ToUpper();
            if (normalized == "GRADUATED") return "GRADUATED";

            switch (normalized)
            {
                case "JSS I": return "JSS II";
                case "JSS II": return "JSS III";
                case "JSS III": return "SSS I";
                case "SSS I": return "SSS II";
                case "SSS II": return "SSS III";
                case "SSS III": return "GRADUATED";
                default: return baseClass;
            }
        }

        public static int GetClassLevelNumber(string className)
        {
            if (string.IsNullOrEmpty(className)) return 0;

            string baseClass = GetBaseClass(className);
            string normalized = baseClass.Trim().ToUpper();

            switch (normalized)
            {
                case "JSS I": return 1;
                case "JSS II": return 2;
                case "JSS III": return 3;
                case "SSS I": return 4;
                case "SSS II": return 5;
                case "SSS III": return 6;
                case "GRADUATED": return 7;
                default: return 0;
            }
        }

        public static int CompareClassLevel(string classA, string classB)
        {
            int levelA = GetClassLevelNumber(classA);
            int levelB = GetClassLevelNumber(classB);
            return levelA.CompareTo(levelB);
        }

        public static bool IsHigherClass(string classA, string classB)
            => GetClassLevelNumber(classA) > GetClassLevelNumber(classB);

        public static bool IsLowerClass(string classA, string classB)
            => GetClassLevelNumber(classA) < GetClassLevelNumber(classB);

        public static string[] GetAllStandardClasses()
            => new[] { "JSS I", "JSS II", "JSS III", "SSS I", "SSS II", "SSS III" };

        public static string[] GetAllClassesWithGraduated()
            => new[] { "JSS I", "JSS II", "JSS III", "SSS I", "SSS II", "SSS III", "GRADUATED" };

        // ── Detection helpers (unchanged) ──

        public static bool IsSSSIClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "SSS I";
        }

        public static bool IsSSS2Class(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "SSS II";
        }

        public static bool IsSSS3Class(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "SSS III";
        }

        public static bool IsJSS1Class(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "JSS I";
        }

        public static bool IsJSS2Class(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "JSS II";
        }

        public static bool IsJSS3Class(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            return GetBaseClass(className) == "JSS III";
        }

        public static bool IsSSSClass(string className)
            => IsSSSIClass(className) || IsSSS2Class(className) || IsSSS3Class(className);

        public static bool IsJSSClass(string className)
            => IsJSS1Class(className) || IsJSS2Class(className) || IsJSS3Class(className);
    }
}
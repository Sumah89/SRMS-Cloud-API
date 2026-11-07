using System;
using System.Collections.Generic;
using System.Linq;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// The single source of truth for "what class does a student in X promote to?"
    /// Reads from the PromotionPaths table with an in-memory cache. Never returns
    /// null for a valid class — always falls back to the system defaults.
    /// </summary>
    public static class PromotionPathService
    {
        private static DatabaseHelper _db;
        private static readonly object _lock = new object();

        // Cache: schoolId → (FromClassName → ToClassName)
        private static Dictionary<int, Dictionary<string, string>> _cache
            = new Dictionary<int, Dictionary<string, string>>();
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        static PromotionPathService()
        {
            _db = new DatabaseHelper();
        }

        // ─────────────────────────────────────────────────────────────
        //  READ
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next base class for a given base class, honouring any
        /// school-specific override. Falls back to system defaults.
        /// </summary>
        public static string GetNextBaseClass(int schoolId, string fromBaseClassName)
        {
            if (string.IsNullOrWhiteSpace(fromBaseClassName)) return fromBaseClassName;

            string normalized = Helpers.ClassHelper.GetBaseClass(fromBaseClassName);
            if (string.IsNullOrEmpty(normalized)) return fromBaseClassName;

            var map = GetMapForSchool(schoolId);

            if (map.TryGetValue(normalized, out string next))
                return next;

            // Fall back to system defaults
            var defaults = GetSystemDefaults();
            return defaults.TryGetValue(normalized, out string defaultNext)
                ? defaultNext
                : normalized;
        }

        /// <summary>
        /// Returns all active promotion paths for a school, including the
        /// system defaults that the school hasn't overridden.
        /// </summary>
        public static List<PromotionPath> GetAllPathsForSchool(int schoolId)
        {
            try
            {
                var result = new List<PromotionPath>();
                var defaults = _db.GetPromotionPaths(0);           // system defaults
                var overrides = schoolId > 0 ? _db.GetPromotionPaths(schoolId) : new List<PromotionPath>();

                var overrideMap = overrides
                    .Where(p => p.IsActive)
                    .ToDictionary(p => p.FromClassName, StringComparer.OrdinalIgnoreCase);

                foreach (var d in defaults.Where(p => p.IsActive))
                {
                    if (overrideMap.ContainsKey(d.FromClassName))
                    {
                        var ovr = overrideMap[d.FromClassName];
                        ovr.SchoolId = schoolId;
                        result.Add(ovr);
                        overrideMap.Remove(d.FromClassName);
                    }
                    else
                    {
                        result.Add(d);
                    }
                }

                // Any overrides left over (school added paths not in defaults)
                result.AddRange(overrideMap.Values);
                return result.OrderBy(p => p.DisplayOrder).ThenBy(p => p.FromClassName).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllPathsForSchool error: {ex.Message}");
                return new List<PromotionPath>();
            }
        }

        /// <summary>
        /// Returns true if the class is terminal (no further promotion).
        /// </summary>
        public static bool IsTerminalClass(int schoolId, string baseClassName)
        {
            if (string.IsNullOrWhiteSpace(baseClassName)) return false;

            string normalized = Helpers.ClassHelper.GetBaseClass(baseClassName);
            string next = GetNextBaseClass(schoolId, normalized);
            return string.Equals(next, "GRADUATED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(next, normalized, StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────
        //  WRITE
        // ─────────────────────────────────────────────────────────────

        public static bool SavePath(PromotionPath path)
        {
            if (path == null) return false;
            bool ok = _db.SavePromotionPath(path);
            if (ok) InvalidateCache();
            return ok;
        }

        public static bool DeletePath(int schoolId, string fromClassName)
        {
            bool ok = _db.DeletePromotionPath(schoolId, fromClassName);
            if (ok) InvalidateCache();
            return ok;
        }

        /// <summary>
        /// Writes default paths for a new school, copying the system defaults.
        /// Call this once when a school is created (or on demand for existing schools).
        /// </summary>
        public static int SeedSchoolDefaults(int schoolId)
        {
            if (schoolId <= 0) return 0;

            var defaults = GetSystemDefaults();
            int seeded = 0;

            foreach (var kvp in defaults)
            {
                var path = new PromotionPath
                {
                    SchoolId = schoolId,
                    FromClassName = kvp.Key,
                    ToClassName = kvp.Value,
                    IsTerminal = string.Equals(kvp.Value, "GRADUATED", StringComparison.OrdinalIgnoreCase),
                    IsActive = true,
                    DisplayOrder = GetDefaultDisplayOrder(kvp.Key)
                };

                if (SavePath(path)) seeded++;
            }

            return seeded;
        }

        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cache.Clear();
                _cacheTime = DateTime.MinValue;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  INTERNALS
        // ─────────────────────────────────────────────────────────────

        private static Dictionary<string, string> GetMapForSchool(int schoolId)
        {
            lock (_lock)
            {
                if ((DateTime.Now - _cacheTime) < CacheDuration
                    && _cache.TryGetValue(schoolId, out var cached))
                {
                    return cached;
                }
            }

            var map = LoadMapForSchool(schoolId);

            lock (_lock)
            {
                _cache[schoolId] = map;
                _cacheTime = DateTime.Now;
            }

            return map;
        }

        private static Dictionary<string, string> LoadMapForSchool(int schoolId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // System defaults first
                var defaults = _db.GetPromotionPaths(0);
                foreach (var p in defaults.Where(x => x.IsActive))
                    result[p.FromClassName] = p.ToClassName;

                // School overrides on top
                if (schoolId > 0)
                {
                    var overrides = _db.GetPromotionPaths(schoolId);
                    foreach (var p in overrides.Where(x => x.IsActive))
                        result[p.FromClassName] = p.ToClassName;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadMapForSchool error: {ex.Message}");
            }

            return result;
        }

        private static Dictionary<string, string> GetSystemDefaults()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "JSS I",   "JSS II"   },
                { "JSS II",  "JSS III"  },
                { "JSS III", "SSS I"    },
                { "SSS I",   "SSS II"   },
                { "SSS II",  "SSS III"  },
                { "SSS III", "GRADUATED"},
            };
        }

        private static int GetDefaultDisplayOrder(string fromClassName)
        {
            switch (fromClassName)
            {
                case "JSS I": return 1;
                case "JSS II": return 2;
                case "JSS III": return 3;
                case "SSS I": return 4;
                case "SSS II": return 5;
                case "SSS III": return 6;
                default: return 99;
            }
        }
    }
}
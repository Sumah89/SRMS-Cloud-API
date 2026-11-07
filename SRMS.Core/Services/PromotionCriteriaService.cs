using System;
using System.Collections.Generic;
using System.Linq;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public static class PromotionCriteriaService
    {
        private static SchoolPromotionCriteria _cachedSchoolCriteria;
        private static Dictionary<int, SchoolPromotionCriteria> _cachedClassCriteria = new Dictionary<int, SchoolPromotionCriteria>();
        private static DateTime _cacheTime;
        private static readonly object _lock = new object();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
        private static DatabaseHelper _dbHelper;

        public static event EventHandler<CriteriaChangedEventArgs> CriteriaChanged;

        static PromotionCriteriaService()
        {
            _dbHelper = new DatabaseHelper();
        }

        public static SchoolPromotionCriteria GetCurrentCriteria(int? classId = null)
        {
            int schoolId = Global.CurrentUser?.SchoolId ?? 0;
            if (schoolId == 0) return null;

            lock (_lock)
            {
                if (classId.HasValue && _cachedClassCriteria.TryGetValue(classId.Value, out var classCriteria) &&
                    (DateTime.Now - _cacheTime) < CacheDuration)
                    return classCriteria;

                if (!classId.HasValue && _cachedSchoolCriteria != null && (DateTime.Now - _cacheTime) < CacheDuration)
                    return _cachedSchoolCriteria;

                LoadCriteriaFromDatabase(schoolId);
                return classId.HasValue && _cachedClassCriteria.TryGetValue(classId.Value, out var result)
                    ? result : _cachedSchoolCriteria;
            }
        }

        public static SchoolPromotionCriteria GetCriteriaByGroupName(int schoolId, string groupName)
        {
            if (schoolId == 0 || string.IsNullOrEmpty(groupName)) return null;

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);

                var matchingClass = _dbHelper.GetClasses(schoolId)
                    .FirstOrDefault(c => c.ClassName.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                if (matchingClass != null && _cachedClassCriteria.TryGetValue(matchingClass.Id, out var criteria))
                    return criteria;

                return _cachedSchoolCriteria;
            }
        }

        public static SchoolPromotionCriteria GetCriteria(int schoolId, int? classId = null)
        {
            if (schoolId == 0) return null;
            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                return classId.HasValue && _cachedClassCriteria.TryGetValue(classId.Value, out var result)
                    ? result : _cachedSchoolCriteria;
            }
        }

        public static List<SchoolPromotionCriteria> GetAllCriteriaForSchool(int schoolId)
        {
            if (schoolId == 0) return new List<SchoolPromotionCriteria>();

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                var result = new List<SchoolPromotionCriteria>();
                if (_cachedSchoolCriteria != null) result.Add(_cachedSchoolCriteria);
                result.AddRange(_cachedClassCriteria.Values);
                return result;
            }
        }

        private static void LoadCriteriaFromDatabase(int schoolId)
        {
            try
            {
                var allCriteria = _dbHelper.GetAllSchoolPromotionCriteria(schoolId);
                _cachedClassCriteria.Clear();
                _cachedSchoolCriteria = null;

                foreach (var criteria in allCriteria)
                {
                    if (criteria.IsSchoolWide)
                    {
                        _cachedSchoolCriteria = criteria;
                    }
                    else if (criteria.ClassId.HasValue)
                    {
                        var classObj = _dbHelper.GetClassById(criteria.ClassId.Value);
                        criteria.ClassName = classObj?.ClassName ?? $"Class ID: {criteria.ClassId}";
                        _cachedClassCriteria[criteria.ClassId.Value] = criteria;
                    }
                }

                _cacheTime = DateTime.Now;

                if (_cachedSchoolCriteria == null)
                {
                    _cachedSchoolCriteria = new SchoolPromotionCriteria
                    {
                        SchoolId = schoolId,
                        ClassId = null,
                        CriteriaType = "Average",
                        MinAveragePercentage = 50,
                        MinCoreSubjectsPassed = 3,
                        MinOptionalSubjectsPassed = 3,
                        MinTotalSubjectsPassed = 6,
                        CoreSubjectPassMark = 50,
                        OptionalSubjectPassMark = 50,
                        TotalPassMark = 50,
                        RequireEnglishOrMaths = false,
                        EnglishMathsPassMark = 50,
                        IncludeAttendance = false,
                        MinAttendancePercentage = 75,
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now
                    };
                    _dbHelper.SaveSchoolPromotionCriteria(_cachedSchoolCriteria);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PromotionCriteriaService] Error loading: {ex.Message}");
                _cachedSchoolCriteria = null;
                _cachedClassCriteria.Clear();
            }
        }

        public static void RefreshCriteria()
        {
            int schoolId = Global.CurrentUser?.SchoolId ?? 0;
            if (schoolId == 0) return;

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                _cacheTime = DateTime.Now;
            }

            CriteriaChanged?.Invoke(null, new CriteriaChangedEventArgs
            {
                NewCriteria = _cachedSchoolCriteria,
                ClassCriteria = _cachedClassCriteria.Values.ToList(),
                RefreshTime = DateTime.Now,
                WasSaved = false
            });
        }

        public static bool SaveCriteria(SchoolPromotionCriteria criteria)
        {
            if (criteria == null) return false;

            try
            {
                bool result = _dbHelper.SaveSchoolPromotionCriteria(criteria);

                if (result)
                {
                    lock (_lock)
                    {
                        LoadCriteriaFromDatabase(criteria.SchoolId);
                        _cacheTime = DateTime.Now;
                    }

                    CriteriaChanged?.Invoke(null, new CriteriaChangedEventArgs
                    {
                        NewCriteria = _cachedSchoolCriteria,
                        ClassCriteria = _cachedClassCriteria.Values.ToList(),
                        RefreshTime = DateTime.Now,
                        WasSaved = true
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PromotionCriteriaService] Save error: {ex.Message}");
                return false;
            }
        }

        public static bool DeleteCriteria(int schoolId, int? classId = null)
        {
            try
            {
                bool result = _dbHelper.DeleteSchoolPromotionCriteria(schoolId, classId);
                if (result) RefreshCriteria();
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PromotionCriteriaService] Delete error: {ex.Message}");
                return false;
            }
        }

        public static bool DeleteCriteriaByGroupName(int schoolId, string groupName)
        {
            if (schoolId == 0 || string.IsNullOrEmpty(groupName)) return false;

            try
            {
                var classObj = _dbHelper.GetClasses(schoolId)
                    .FirstOrDefault(c => c.ClassName.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                if (classObj == null) return false;
                return DeleteCriteria(schoolId, classObj.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PromotionCriteriaService] DeleteCriteriaByGroupName error: {ex.Message}");
                return false;
            }
        }

        public static bool HasValidCriteria(int? classId = null)
        {
            var criteria = GetCurrentCriteria(classId);
            return criteria != null && criteria.Id > 0 && !string.IsNullOrEmpty(criteria.CriteriaType);
        }

        public static bool HasValidCriteriaTypes(SchoolPromotionCriteria criteria)
        {
            if (criteria == null || string.IsNullOrEmpty(criteria.CriteriaType)) return false;
            return criteria.CriteriaType.Contains("Average") ||
                   criteria.CriteriaType.Contains("CoreSubjects") ||
                   criteria.CriteriaType.Contains("TotalSubjects");
        }

        public static string GetCriteriaDescription(int? classId = null)
        {
            var criteria = GetCurrentCriteria(classId);
            return GetCriteriaDescription(criteria);
        }

        public static string GetCriteriaDescription(SchoolPromotionCriteria criteria)
        {
            if (criteria == null || string.IsNullOrEmpty(criteria.CriteriaType))
                return "NOT CONFIGURED - Please set up promotion criteria";
            return criteria.GetDescription();
        }

        public static string GetCriteriaDisplayText(int? classId = null)
        {
            var criteria = GetCurrentCriteria(classId);
            if (criteria == null || criteria.Id == 0 || string.IsNullOrEmpty(criteria.CriteriaType))
                return "NO PROMOTION CRITERIA CONFIGURED";

            string classInfo = criteria.IsSchoolWide ? "(All Classes)" : $"(Class: {criteria.ClassName})";
            return $"{classInfo} Active: {criteria.GetDescription()}";
        }

        public static List<SchoolPromotionCriteria> GetAllCriteriaForSchool()
        {
            int schoolId = Global.CurrentUser?.SchoolId ?? 0;
            if (schoolId == 0) return new List<SchoolPromotionCriteria>();

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                var result = new List<SchoolPromotionCriteria>();
                if (_cachedSchoolCriteria != null) result.Add(_cachedSchoolCriteria);
                result.AddRange(_cachedClassCriteria.Values);
                return result;
            }
        }

        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedSchoolCriteria = null;
                _cachedClassCriteria.Clear();
                _cacheTime = DateTime.MinValue;
            }
        }

        public static void SetCurrentCriteria(SchoolPromotionCriteria criteria)
        {
            if (criteria == null) return;

            lock (_lock)
            {
                if (criteria.IsSchoolWide) _cachedSchoolCriteria = criteria;
                else if (criteria.ClassId.HasValue) _cachedClassCriteria[criteria.ClassId.Value] = criteria;
                _cacheTime = DateTime.Now;
            }

            CriteriaChanged?.Invoke(null, new CriteriaChangedEventArgs
            {
                NewCriteria = criteria,
                ClassCriteria = _cachedClassCriteria.Values.ToList(),
                RefreshTime = DateTime.Now,
                WasSaved = true
            });
        }

        public static SchoolPromotionCriteria GetCriteriaForClass(int schoolId, int classId)
        {
            if (schoolId == 0 || classId == 0) return null;

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                return _cachedClassCriteria.TryGetValue(classId, out var criteria) ? criteria : _cachedSchoolCriteria;
            }
        }

        public static SchoolPromotionCriteria GetCriteriaForClass(Class classObj)
        {
            if (classObj == null) return null;
            return GetCriteriaForClass(classObj.SchoolId, classObj.Id);
        }

        public static bool HasClassSpecificCriteria(int schoolId, int classId)
        {
            if (schoolId == 0 || classId == 0) return false;

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                return _cachedClassCriteria.ContainsKey(classId);
            }
        }

        public static SchoolPromotionCriteria GetOrCreateDefaultCriteria(int schoolId)
        {
            if (schoolId == 0) return null;

            lock (_lock)
            {
                LoadCriteriaFromDatabase(schoolId);
                return _cachedSchoolCriteria;
            }
        }
    }

    public class CriteriaChangedEventArgs : EventArgs
    {
        public SchoolPromotionCriteria NewCriteria { get; set; }
        public List<SchoolPromotionCriteria> ClassCriteria { get; set; } = new List<SchoolPromotionCriteria>();
        public DateTime RefreshTime { get; set; }
        public bool WasSaved { get; set; } = false;
    }
}
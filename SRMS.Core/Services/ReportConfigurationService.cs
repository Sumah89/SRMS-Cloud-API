using System;
using System.Collections.Generic;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;
using System.Diagnostics;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Manages report configuration and coordinates between SchoolReportConfigForm, 
    /// ReportGeneratorFactory, ReportCardForm, and BatchReportCardGenerator.
    /// Ensures all components use consistent result type system and settings.
    /// </summary>
    public class ReportConfigurationService
    {
        private readonly DatabaseHelper _dbHelper;
        private Dictionary<int, SchoolReportConfig> _configCache;
        private readonly object _cacheLock = new object();

        public ReportConfigurationService()
        {
            _dbHelper = new DatabaseHelper();
            _configCache = new Dictionary<int, SchoolReportConfig>();
        }

        /// <summary>
        /// Gets the report configuration for a school, with caching for performance.
        /// </summary>
        public SchoolReportConfig GetSchoolReportConfig(int schoolId)
        {
            lock (_cacheLock)
            {
                // Check cache first
                if (_configCache.ContainsKey(schoolId))
                {
                    return _configCache[schoolId];
                }
            }

            try
            {
                // Try to load from database
                string query = "SELECT * FROM SchoolReportConfig WHERE SchoolId = @SchoolId";
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
                var result = _dbHelper.ExecuteQuery(query, parameters);

                SchoolReportConfig config = null;

                if (result.Rows.Count > 0)
                {
                    var row = result.Rows[0];
                    config = new SchoolReportConfig
                    {
                        Id = Convert.ToInt32(row["Id"]),
                        SchoolId = schoolId,
                        ResultType = row["ResultType"]?.ToString() ?? "FiveTests",
                        NumberOfTerms = Convert.ToInt32(row["NumberOfTerms"]),
                        PaperOrientation = row["PaperOrientation"]?.ToString() ?? "Portrait",
                        ShowBehaviourSection = Convert.ToBoolean(row["ShowBehaviourSection"]),
                        ShowPsychomotorSection = Convert.ToBoolean(row["ShowPsychomotorSection"]),
                        ShowAttendanceSection = Convert.ToBoolean(row["ShowAttendanceSection"]),
                        ShowSubjectAnalysis = Convert.ToBoolean(row["ShowSubjectAnalysis"]),
                        HeaderLogoPath = row["HeaderLogoPath"]?.ToString(),
                        FooterText = row["FooterText"]?.ToString()
                    };
                }
                else
                {
                    // Create default config if not found
                    config = CreateDefaultConfig(schoolId);
                }

                // Cache it
                lock (_cacheLock)
                {
                    _configCache[schoolId] = config;
                }

                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading SchoolReportConfig: {ex.Message}");
                return CreateDefaultConfig(schoolId);
            }
        }

        /// <summary>
        /// Gets the result type system for a school.
        /// This is the primary method used by ReportGeneratorFactory to determine which generator to use.
        /// </summary>
        public string GetSchoolResultType(int schoolId)
        {
            try
            {
                var config = GetSchoolReportConfig(schoolId);
                return config?.ResultType ?? "FiveTests";
            }
            catch
            {
                return "FiveTests";
            }
        }

        /// <summary>
        /// Saves report configuration and synchronizes it across all storage locations.
        /// </summary>
        public bool SaveReportConfiguration(SchoolReportConfig config)
        {
            try
            {
                if (config == null || config.SchoolId <= 0)
                {
                    throw new ArgumentException("Invalid school configuration");
                }

                config.ModifiedDate = DateTime.Now;

                // Step 1: Check if configuration exists, if not INSERT, otherwise UPDATE
                string checkQuery = "SELECT COUNT(*) FROM SchoolReportConfig WHERE SchoolId = @SchoolId";
                var checkParams = new Dictionary<string, object> { { "@SchoolId", config.SchoolId } };
                var result = _dbHelper.ExecuteQuery(checkQuery, checkParams);
                bool configExists = result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0][0]) > 0;

                if (configExists)
                {
                    // UPDATE existing configuration
                    string updateConfigQuery = @"UPDATE SchoolReportConfig SET ResultType = @ResultType, NumberOfTerms = @NumberOfTerms, 
                        PaperOrientation = @PaperOrientation, ShowBehaviourSection = @ShowBehaviourSection, 
                        ShowPsychomotorSection = @ShowPsychomotorSection, ShowAttendanceSection = @ShowAttendanceSection, 
                        ShowSubjectAnalysis = @ShowSubjectAnalysis, HeaderLogoPath = @HeaderLogoPath, FooterText = @FooterText, 
                        ModifiedDate = @ModifiedDate WHERE SchoolId = @SchoolId";

                    var configParams = new Dictionary<string, object>
                    {
                        { "@SchoolId", config.SchoolId },
                        { "@ResultType", config.ResultType ?? "FiveTests" },
                        { "@NumberOfTerms", config.NumberOfTerms },
                        { "@PaperOrientation", config.PaperOrientation ?? "Portrait" },
                        { "@ShowBehaviourSection", config.ShowBehaviourSection },
                        { "@ShowPsychomotorSection", config.ShowPsychomotorSection },
                        { "@ShowAttendanceSection", config.ShowAttendanceSection },
                        { "@ShowSubjectAnalysis", config.ShowSubjectAnalysis },
                        { "@HeaderLogoPath", config.HeaderLogoPath ?? "" },
                        { "@FooterText", config.FooterText ?? "" },
                        { "@ModifiedDate", config.ModifiedDate }
                    };

                    _dbHelper.ExecuteNonQuery(updateConfigQuery, configParams);
                    Debug.WriteLine($"Updated existing configuration for school {config.SchoolId}");
                }
                else
                {
                    // INSERT new configuration
                    string insertConfigQuery = @"INSERT INTO SchoolReportConfig 
                        (SchoolId, ResultType, NumberOfTerms, PaperOrientation, ShowBehaviourSection, 
                         ShowPsychomotorSection, ShowAttendanceSection, ShowSubjectAnalysis, HeaderLogoPath, 
                         FooterText, CreatedDate, ModifiedDate) 
                        VALUES (@SchoolId, @ResultType, @NumberOfTerms, @PaperOrientation, @ShowBehaviourSection, 
                                @ShowPsychomotorSection, @ShowAttendanceSection, @ShowSubjectAnalysis, @HeaderLogoPath, 
                                @FooterText, @CreatedDate, @ModifiedDate)";

                    var configParams = new Dictionary<string, object>
                    {
                        { "@SchoolId", config.SchoolId },
                        { "@ResultType", config.ResultType ?? "FiveTests" },
                        { "@NumberOfTerms", config.NumberOfTerms },
                        { "@PaperOrientation", config.PaperOrientation ?? "Portrait" },
                        { "@ShowBehaviourSection", config.ShowBehaviourSection },
                        { "@ShowPsychomotorSection", config.ShowPsychomotorSection },
                        { "@ShowAttendanceSection", config.ShowAttendanceSection },
                        { "@ShowSubjectAnalysis", config.ShowSubjectAnalysis },
                        { "@HeaderLogoPath", config.HeaderLogoPath ?? "" },
                        { "@FooterText", config.FooterText ?? "" },
                        { "@CreatedDate", DateTime.Now },
                        { "@ModifiedDate", config.ModifiedDate }
                    };

                    _dbHelper.ExecuteNonQuery(insertConfigQuery, configParams);
                    Debug.WriteLine($"Inserted new configuration for school {config.SchoolId}");
                }

                // Step 2: Synchronize ResultType to Schools table
                // This is critical so ReportGeneratorFactory.GetGeneratorBySchoolId() gets the correct result type
                string updateSchoolQuery = "UPDATE Schools SET ResultType = @ResultType, ModifiedDate = @ModifiedDate WHERE Id = @SchoolId";
                var schoolParams = new Dictionary<string, object>
                {
                    { "@SchoolId", config.SchoolId },
                    { "@ResultType", config.ResultType ?? "FiveTests" },
                    { "@ModifiedDate", DateTime.Now }
                };

                _dbHelper.ExecuteNonQuery(updateSchoolQuery, schoolParams);

                // Step 3: Update Global.CurrentUser if applicable
                if (Global.CurrentUser != null && Global.CurrentUser.SchoolId == config.SchoolId)
                {
                    Global.CurrentUser.SchoolResultType = config.ResultType ?? "FiveTests";
                }

                // Step 4: Invalidate cache so next call gets fresh data
                lock (_cacheLock)
                {
                    _configCache.Remove(config.SchoolId);
                }

                // Step 5: Notify ReportGeneratorFactory to refresh its cached generators if needed
                // (The factory will use GetSchoolById on next call which will have updated ResultType)

                Debug.WriteLine($"Report configuration saved for school {config.SchoolId}: ResultType={config.ResultType}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving report configuration: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets report configuration and ensures the result type is set on both 
        /// SchoolReportConfig and School tables for consistency.
        /// </summary>
        public SchoolReportConfig EnsureConfigurationConsistency(int schoolId)
        {
            try
            {
                var config = GetSchoolReportConfig(schoolId);

                // Verify that Schools table has the same ResultType
                var school = _dbHelper.GetSchoolById(schoolId);
                if (school != null && school.ResultType != config.ResultType)
                {
                    // Sync the School table with the SchoolReportConfig
                    string updateQuery = "UPDATE Schools SET ResultType = @ResultType WHERE Id = @SchoolId";
                    var parameters = new Dictionary<string, object>
                    {
                        { "@SchoolId", schoolId },
                        { "@ResultType", config.ResultType }
                    };
                    _dbHelper.ExecuteNonQuery(updateQuery, parameters);

                    Debug.WriteLine($"Synchronized ResultType for school {schoolId}: {config.ResultType}");
                }

                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring configuration consistency: {ex.Message}");
                return GetSchoolReportConfig(schoolId);
            }
        }

        /// <summary>
        /// Clears the configuration cache. Call this after making changes that might affect 
        /// multiple forms/generators in the same session.
        /// </summary>
        public void ClearCache(int? schoolId = null)
        {
            lock (_cacheLock)
            {
                if (schoolId.HasValue)
                {
                    _configCache.Remove(schoolId.Value);
                }
                else
                {
                    _configCache.Clear();
                }
            }
        }

        private SchoolReportConfig CreateDefaultConfig(int schoolId)
        {
            return new SchoolReportConfig
            {
                Id = 0,
                SchoolId = schoolId,
                ResultType = "FiveTests",
                NumberOfTerms = 3,
                PaperOrientation = "Portrait",
                ShowBehaviourSection = true,
                ShowPsychomotorSection = true,
                ShowAttendanceSection = true,
                ShowSubjectAnalysis = true,
                FooterText = "Thank you for your cooperation",
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now
            };
        }
    }
}

using SRMS.Desktop.Models;
using SRMS.Desktop.Security;
using SRMS.Desktop.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SRMS.Desktop.Data
{
    public partial class DatabaseHelper : IDisposable
    {
        #region Private Fields

        private static string _connectionString;
        private static readonly object _lockObject = new object();
        private static SqliteConnection _sharedConnection;
        private static int _connectionCount = 0;
        private bool _disposed = false;
        private static bool _initialized = false;
        private static bool? _hasRoleIdColumn = null;

        public static event EventHandler<SchoolResultTypeChangedEventArgs> SchoolResultTypeChanged;
        #endregion

        #region Constructor & Initialization

        public DatabaseHelper()
        {
            if (_initialized) return;

            try
            {
                ApplicationPathManager.EnsureInitialized();

                string dbPath = ApplicationPathManager.DatabasePath;

                _connectionString = $"Data Source={dbPath};Pooling=True;Cache=Shared;";

                InitializeDatabase();
                if (File.Exists(dbPath)) File.SetAttributes(dbPath, FileAttributes.Normal);
                _initialized = true;

                Debug.WriteLine($"Database initialized at: {dbPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Database initialization failed: {ex.Message}");
                throw;
            }
        }
        public int ExecuteNonQueryWithId(string query, Dictionary<string, object> parameters)
        {
            int result = ExecuteNonQuery(query, parameters);
            if (result > 0)
            {
                var id = ExecuteScalar("SELECT last_insert_rowid()", null);
                if (id != null)
                    return Convert.ToInt32(id);
            }
            return 0;
        }
        public bool SoftDeleteRecord(string tableName, int id, string deletedBy = "System")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tableName) || id <= 0) return false;
                if (!Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$")) return false;

                string query = $"DELETE FROM {tableName} WHERE Id = @Id";
                var parameters = new Dictionary<string, object>
                {
                    { "@Id", id }
                };
                return ExecuteNonQuery(query, parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hard delete (SoftDeleteRecord) error: {ex.Message}");
                return false;
            }
        }

        private void InitializeDatabase()
        {
            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();
                CreateAllTables(connection);
                EnsurePromotionRecordsTable(connection);
                EnsureScoreAuditLogTable(connection);
                EnsureScoreEntryProvenanceColumns(connection);
                EnsureNotificationsTable(connection);
                EnsureAuditTrailTable(connection);
                EnsureSettingsTables(connection);
                EnsurePublishedReportsTable(connection);
                EnsureParentSubscriptionsTable(connection);
                EnsureSyncQueueTable(connection);
                EnsureSubscriptionPlansTable(connection);

                EnsureClassesResultTypeOverrideColumn(connection);
                EnsurePromotionPathsTable(connection);
                EnsureUsersPasswordResetColumn(connection);

                EnsureStudentEnrollmentsTable(connection);
                EnsurePromotionBatchesTable(connection);
                EnsurePromotionCriteriaColumns(connection);
                EnsureSchoolsLicenseColumns(connection);
                EnsureTeacherAccessTable(connection);

                EnsureEMISSeedData(connection);
                CreateIndexes(connection);
                AddSoftDeleteColumns(connection);
                UpgradeTablesForSixTests(connection);

                BackfillStudentEnrollments(connection);
                BackfillStudentEnrollments(connection);
                InitializeMasterAdmin();
                EnsurePhotoPathColumnExists(connection);
                EnsureStudentSpecialCaseTable(connection);
                EnsureStudentSpecialCaseColumns(connection);

                EnsureStreamColumnExists(connection);
                EnsureSchoolReportConfigResultTypeColumn(connection);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeDatabase Error: {ex.Message}");
                throw;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }

        private HashSet<string> GetTableColumnNames(SqliteConnection connection, string tableName)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.IsNullOrWhiteSpace(tableName) ||
                    !Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$"))
                    return names;

                bool openedHere = false;
                if (connection == null)
                {
                    connection = GetConnection();
                    openedHere = true;
                }

                try
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $"PRAGMA table_info('{tableName}')";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (!reader.IsDBNull(1))
                                    names.Add(reader.GetString(1));
                            }
                        }
                    }
                }
                finally
                {
                    if (openedHere && connection != null) CloseConnection(connection);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTableColumnNames({tableName}) error: {ex.Message}");
            }

            return names;
        }

        private void EnsureAuditTrailTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS AuditTrail (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TableName TEXT NOT NULL,
                        RecordId INTEGER NOT NULL,
                        Action TEXT NOT NULL,
                        OldValue TEXT,
                        NewValue TEXT,
                        ChangedBy TEXT NOT NULL,
                        ChangedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        IPAddress TEXT,
                        UserRole TEXT
                    )", null, connection);
                ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_audittrail_date ON AuditTrail(ChangedDate DESC)", null, connection);
                ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_audittrail_action ON AuditTrail(Action)", null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureAuditTrailTable error: {ex.Message}");
            }
        }

        public void LogSystemAudit(string action, string details, string changedBy = null, string userRole = null)
        {
            try
            {
                ExecuteNonQuery(@"INSERT INTO AuditTrail
                    (TableName, RecordId, Action, OldValue, NewValue, ChangedBy, UserRole)
                    VALUES (@TableName, 0, @Action, '', @Details, @ChangedBy, @UserRole)",
                    new Dictionary<string, object>
                    {
                        { "@TableName", "System" },
                        { "@Action", action ?? "SYSTEM" },
                        { "@Details", details ?? "" },
                        { "@ChangedBy", changedBy ?? Global.CurrentUser?.Username ?? "System" },
                        { "@UserRole", userRole ?? Global.CurrentUser?.Role ?? "Unknown" }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogSystemAudit error: {ex.Message}");
            }
        }
        private void EnsureScoreAuditLogTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS ScoreAuditLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                SubjectId INTEGER NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                FieldName TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                OperationType TEXT NOT NULL,
                Source TEXT,
                UserId INTEGER,
                UserName TEXT,
                UserRole TEXT,
                ChangedAt DATETIME NOT NULL,
                Remarks TEXT
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_scoreaudit_student ON ScoreAuditLog(StudentId, AcademicYear);",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_scoreaudit_subject ON ScoreAuditLog(SubjectId, Term, AcademicYear);",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_scoreaudit_changedat ON ScoreAuditLog(ChangedAt DESC);",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_scoreaudit_user ON ScoreAuditLog(UserId, ChangedAt DESC);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureScoreAuditLogTable error: {ex.Message}");
            }
        }

        private void EnsureScoreEntryProvenanceColumns(SqliteConnection connection)
        {
            try
            {
                TryAddColumn(connection, "AcademicPerformance", "EnteredByUserId", "INTEGER");
                TryAddColumn(connection, "AcademicPerformance", "EnteredByRole", "TEXT");
                TryAddColumn(connection, "AcademicPerformance", "EnteredDate", "DATETIME");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureScoreEntryProvenanceColumns error: {ex.Message}");
            }
        }
        private void EnsureClassesResultTypeOverrideColumn(SqliteConnection connection)
        {
            try
            {
                var names = GetTableColumnNames(connection, "Classes");

                if (!names.Contains("ResultTypeOverride"))
                {
                    ExecuteNonQuery(
                        "ALTER TABLE Classes ADD COLUMN ResultTypeOverride TEXT",
                        null, connection);
                    Debug.WriteLine("Added ResultTypeOverride column to Classes table");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureClassesResultTypeOverrideColumn error: {ex.Message}");
                throw;
            }
        }

        private void EnsureSchoolsLicenseColumns(SqliteConnection connection)
        {
            try
            {
                var cols = GetTableColumnNames(connection, "Schools");
                if (!cols.Contains("LicenseKey"))
                {
                    try { ExecuteNonQuery("ALTER TABLE Schools ADD COLUMN LicenseKey TEXT", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add LicenseKey column error: {ex.Message}"); }
                }
                if (!cols.Contains("LicenseExpiryDate"))
                {
                    try { ExecuteNonQuery("ALTER TABLE Schools ADD COLUMN LicenseExpiryDate DATETIME", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add LicenseExpiryDate column error: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSchoolsLicenseColumns error: {ex.Message}");
            }
        }

        private void EnsureTeacherAccessTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS TeacherAccess (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TeacherId INTEGER NOT NULL,
                ClassId INTEGER,
                SubjectId INTEGER,
                AccessLevel TEXT DEFAULT 'Limited',
                AssignedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                IsActive BOOLEAN DEFAULT 1,
                FOREIGN KEY (TeacherId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE SET NULL,
                FOREIGN KEY (SubjectId) REFERENCES Subjects(Id) ON DELETE SET NULL
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_teacheraccess_teacher ON TeacherAccess(TeacherId, IsActive);",
                    null, connection);
                
                // Mobile access flags for teachers / staff
                TryAddColumn(connection, "Users", "HasMobileAccess", "INTEGER DEFAULT 1");
                TryAddColumn(connection, "Users", "HasLoginAccount", "INTEGER DEFAULT 1");
                TryAddColumn(connection, "TeacherAccess", "AssignmentType", "TEXT DEFAULT 'SubjectTeacher'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureTeacherAccessTable error: {ex.Message}");
            }
        }

        private void EnsurePromotionPathsTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS PromotionPaths (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL DEFAULT 0,
                FromClassName TEXT NOT NULL,
                ToClassName TEXT NOT NULL,
                IsTerminal BOOLEAN DEFAULT 0,
                DisplayOrder INTEGER DEFAULT 0,
                IsActive BOOLEAN DEFAULT 1,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(SchoolId, FromClassName)
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_promotionpaths_school ON PromotionPaths(SchoolId, IsActive);",
                    null, connection);

                object existing = ExecuteScalar(
                    "SELECT COUNT(*) FROM PromotionPaths WHERE SchoolId = 0", null, connection);
                int count = existing == null || existing == DBNull.Value ? 0 : Convert.ToInt32(existing);

                if (count == 0)
                {
                    var defaults = new[]
                    {
                ("JSS I",   "JSS II",   0, 1),
                ("JSS II",  "JSS III",  0, 2),
                ("JSS III", "SSS I",    0, 3),
                ("SSS I",   "SSS II",   0, 4),
                ("SSS II",  "SSS III",  0, 5),
                ("SSS III", "GRADUATED",1, 6),
            };

                    foreach (var (from, to, terminal, order) in defaults)
                    {
                        ExecuteNonQuery(
                            @"INSERT OR IGNORE INTO PromotionPaths
                        (SchoolId, FromClassName, ToClassName, IsTerminal, DisplayOrder, IsActive, CreatedDate, ModifiedDate)
                      VALUES (0, @From, @To, @Terminal, @Order, 1, @Now, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@From", from },
                        { "@To", to },
                        { "@Terminal", terminal },
                        { "@Order", order },
                        { "@Now", DateTime.Now }
                            }, connection);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsurePromotionPathsTable error: {ex.Message}");
            }
        }
        private void EnsureSettingsTables(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS StudentSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                SettingKey TEXT NOT NULL,
                SettingValue TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(StudentId, SettingKey)
            )", null, connection);

                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS SchoolSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                SettingKey TEXT NOT NULL,
                SettingValue TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(SchoolId, SettingKey)
            )", null, connection);

                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SettingKey TEXT NOT NULL UNIQUE,
                SettingValue TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_studentsettings_student ON StudentSettings(StudentId);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_schoolsettings_school ON SchoolSettings(SchoolId);",
                    null, connection);

                object migratedFlag = ExecuteScalar(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = '__legacy_settings_migrated' LIMIT 1",
                    null, connection);

                if (migratedFlag == null || migratedFlag == DBNull.Value)
                {
                    MigrateLegacySettings(connection);

                    ExecuteNonQuery(
                        @"INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue, CreatedDate, ModifiedDate)
                  VALUES ('__legacy_settings_migrated', @Val, @Now, @Now)",
                        new Dictionary<string, object>
                        {
                    { "@Val", DateTime.UtcNow.ToString("O") },
                    { "@Now", DateTime.Now }
                        }, connection);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSettingsTables error: {ex.Message}");
            }
        }

        private void MigrateLegacySettings(SqliteConnection connection)
        {
            try
            {
                object tableExists = ExecuteScalar(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='Settings' LIMIT 1",
                    null, connection);

                if (tableExists == null || tableExists == DBNull.Value)
                    return;

                DataTable legacyRows = ExecuteQuery("SELECT * FROM Settings", null, connection);
                int migratedStudent = 0, migratedSchool = 0, migratedApp = 0;

                foreach (DataRow row in legacyRows.Rows)
                {
                    int? studentId = row.Table.Columns.Contains("StudentId") && row["StudentId"] != DBNull.Value
                        ? (int?)Convert.ToInt32(row["StudentId"]) : null;

                    int? schoolId = row.Table.Columns.Contains("SchoolId") && row["SchoolId"] != DBNull.Value
                        ? (int?)Convert.ToInt32(row["SchoolId"]) : null;

                    string key = row["SettingKey"]?.ToString() ?? "";
                    string value = row["SettingValue"] == DBNull.Value ? "" : row["SettingValue"].ToString();

                    if (string.IsNullOrEmpty(key)) continue;

                    if (studentId.HasValue && studentId.Value > 0)
                    {
                        ExecuteNonQuery(
                            @"INSERT OR REPLACE INTO StudentSettings (StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
                      VALUES (@Sid, @Key, @Val, @Now, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@Sid", studentId.Value },
                        { "@Key", key },
                        { "@Val", value },
                        { "@Now", DateTime.Now }
                            }, connection);
                        migratedStudent++;
                    }
                    else if (schoolId.HasValue && schoolId.Value > 0)
                    {
                        ExecuteNonQuery(
                            @"INSERT OR REPLACE INTO SchoolSettings (SchoolId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
                      VALUES (@Sch, @Key, @Val, @Now, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@Sch", schoolId.Value },
                        { "@Key", key },
                        { "@Val", value },
                        { "@Now", DateTime.Now }
                            }, connection);
                        migratedSchool++;
                    }
                    else
                    {
                        ExecuteNonQuery(
                            @"INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue, CreatedDate, ModifiedDate)
                      VALUES (@Key, @Val, @Now, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@Key", key },
                        { "@Val", value },
                        { "@Now", DateTime.Now }
                            }, connection);
                        migratedApp++;
                    }
                }

                Debug.WriteLine($"Legacy Settings migration: {migratedStudent} student, {migratedSchool} school, {migratedApp} app");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MigrateLegacySettings error: {ex.Message}");
            }
        }
        private void EnsurePromotionCriteriaColumns(SqliteConnection connection)
        {
            try
            {
                var names = GetTableColumnNames(connection, "SchoolPromotionCriteria");

                if (!names.Contains("RequireEnglishOrMaths"))
                {
                    try { ExecuteNonQuery("ALTER TABLE SchoolPromotionCriteria ADD COLUMN RequireEnglishOrMaths BOOLEAN DEFAULT 0", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add RequireEnglishOrMaths column error: {ex.Message}"); }
                }

                if (!names.Contains("EnglishMathsPassMark"))
                {
                    try { ExecuteNonQuery("ALTER TABLE SchoolPromotionCriteria ADD COLUMN EnglishMathsPassMark DECIMAL(5,2) DEFAULT 50", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add EnglishMathsPassMark column error: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsurePromotionCriteriaColumns error: {ex.Message}");
            }
        }
        private void EnsureUsersPasswordResetColumn(SqliteConnection connection)
        {
            try
            {
                var columnNames = GetTableColumnNames(connection, "Users");
                if (!columnNames.Contains("PasswordResetRequired"))
                {
                    try { ExecuteNonQuery("ALTER TABLE Users ADD COLUMN PasswordResetRequired BOOLEAN DEFAULT 0", null, connection); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureUsersPasswordResetColumn error: {ex.Message}");
            }
        }

        private void EnsureEMISSeedData(SqliteConnection connection)
        {
            try
            {
                object cnt = ExecuteScalar("SELECT COUNT(1) FROM Staff", null, connection);
                int count = cnt == null ? 0 : Convert.ToInt32(cnt);
                if (count == 0)
                {
                    try
                    {
                        ExecuteNonQuery(@"INSERT INTO Staff (SchoolId, StaffNumber, FirstName, LastName, Position, SubjectSpecialization, IsActive, CreatedDate, ModifiedDate) VALUES (1, 'T001', 'John', 'Doe', 'Teacher', 'Mathematics', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)", null, connection);
                        ExecuteNonQuery(@"INSERT INTO Staff (SchoolId, StaffNumber, FirstName, LastName, Position, SubjectSpecialization, IsActive, CreatedDate, ModifiedDate) VALUES (1, 'T002', 'Mary', 'Adu', 'Teacher', 'English', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)", null, connection);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureEMISSeedData error: {ex.Message}");
            }
        }

        private void CreateBECECertificatesTable(SqliteConnection connection)
        {
            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECECertificates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                SchoolId INTEGER NOT NULL,
                StudentName TEXT NOT NULL,
                Gender TEXT NOT NULL,
                SchoolWAECCode TEXT NOT NULL,
                StudentSerialNumber TEXT NOT NULL,
                IndexNumber TEXT NOT NULL UNIQUE,
                ExamMonth TEXT NOT NULL,
                ExamYear INTEGER NOT NULL,
                SchoolName TEXT NOT NULL,
                SchoolLocation TEXT NOT NULL,
                SchoolMotto TEXT,
                SchoolCrestPath TEXT,
                StudentPhotoPath TEXT,
                CARTeacherName TEXT NOT NULL,
                PrincipalName TEXT NOT NULL,
                Aggregate INTEGER NOT NULL,
                CoreSubjectsPassed INTEGER NOT NULL,
                OptionalSubjectsPassed INTEGER NOT NULL,
                TotalSubjectsPassed INTEGER NOT NULL,
                PdfPath TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);
        }

        private void CreateAllTables(SqliteConnection connection)
        {
            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Schools (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolName TEXT NOT NULL,
                District TEXT NOT NULL,
                Town TEXT NOT NULL,
                Chiefdom TEXT,
                Address TEXT,
                Phone TEXT,
                Email TEXT,
                PrincipalName TEXT,
                LogoPath TEXT,
                LicenseKey TEXT,
                LicenseExpiryDate DATETIME,
                IsActive BOOLEAN DEFAULT 1,
                ResultType TEXT DEFAULT 'FiveTests',
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Subscriptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL UNIQUE,
                PlanCode TEXT NOT NULL DEFAULT 'standard',
                Status TEXT NOT NULL DEFAULT 'Active',
                StartedUtc DATETIME NOT NULL,
                ExpiresUtc DATETIME,
                GraceUntilUtc DATETIME,
                Source TEXT NOT NULL DEFAULT 'admin',
                LegacyLicenseKey TEXT,
                UpdatedUtc DATETIME NOT NULL,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RoleName TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Classes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                ClassName TEXT NOT NULL,
                ClassLevel TEXT NOT NULL,
                Stream TEXT,
                AcademicYear TEXT NOT NULL,
                ClassTeacherName TEXT,
                IsActive BOOLEAN DEFAULT 1,
                ResultTypeOverride TEXT,
                IsDeleted BOOLEAN DEFAULT 0,
                DeletedDate DATETIME,
                DeletedBy TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Subjects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                SubjectCode TEXT,
                SubjectName TEXT NOT NULL,
                IsCore BOOLEAN DEFAULT 0,
                ClassId INTEGER,
                AcademicYear TEXT,
                PassMark DECIMAL(5,2) DEFAULT 50,
                IsActive BOOLEAN DEFAULT 1,
                IsDeleted BOOLEAN DEFAULT 0,
                DeletedDate DATETIME,
                DeletedBy TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Staff (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SchoolId INTEGER NOT NULL,
            StaffNumber TEXT,
            FirstName TEXT,
            LastName TEXT,
            MiddleName TEXT,
            DateOfBirth DATETIME,
            Gender TEXT,
            MaritalStatus TEXT,
            Nationality TEXT,
            Address TEXT,
            Phone TEXT,
            Email TEXT,
            EmploymentDate DATETIME,
            EmploymentType TEXT,
            Department TEXT,
            Position TEXT,
            SubjectSpecialization TEXT,
            Qualification TEXT,
            YearsOfExperience INTEGER,
            IsActive BOOLEAN DEFAULT 1,
            IsDeleted BOOLEAN DEFAULT 0,
            PhotoPath TEXT,
            LinkedUserId INTEGER,
            CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
            ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
            FOREIGN KEY (LinkedUserId) REFERENCES Users(Id) ON DELETE SET NULL)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS ClassSchedules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                ClassId INTEGER NOT NULL,
                SubjectId INTEGER NOT NULL,
                TeacherId INTEGER,
                DayOfWeek TEXT,
                StartTime TEXT,
                EndTime TEXT,
                Room TEXT,
                Term INTEGER,
                AcademicYear TEXT,
                IsActive BOOLEAN DEFAULT 1,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE CASCADE,
                FOREIGN KEY (SubjectId) REFERENCES Subjects(Id) ON DELETE CASCADE,
                FOREIGN KEY (TeacherId) REFERENCES Staff(Id) ON DELETE SET NULL)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS TeacherSubjectLoad (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                TeacherId INTEGER NOT NULL,
                SubjectId INTEGER NOT NULL,
                ClassId INTEGER,
                PeriodsPerWeek INTEGER DEFAULT 0,
                Term INTEGER,
                AcademicYear TEXT,
                IsActive BOOLEAN DEFAULT 1,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (TeacherId) REFERENCES Staff(Id) ON DELETE CASCADE,
                FOREIGN KEY (SubjectId) REFERENCES Subjects(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE SET NULL)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Students (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                ClassId INTEGER NOT NULL,
                AdmissionNumber TEXT NOT NULL UNIQUE,
                FullName TEXT NOT NULL,
                DateOfBirth DATETIME NOT NULL,
                Gender TEXT NOT NULL,
                Address TEXT,
                ParentName TEXT,
                ParentPhone TEXT,
                HouseColour TEXT,
                Stream TEXT,
                PhotoPath TEXT,
                IsActive BOOLEAN DEFAULT 1,
                IsDeleted BOOLEAN DEFAULT 0,
                DeletedDate DATETIME,
                DeletedBy TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE CASCADE)", null, connection);

            try { ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_staff_school ON Staff(SchoolId);", null, connection); } catch { }
            try { ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_classschedules_class ON ClassSchedules(ClassId, Term, AcademicYear);", null, connection); } catch { }
            try { ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_teachersubjectload_teacher ON TeacherSubjectLoad(TeacherId, SchoolId);", null, connection); } catch { }

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS ParentCodes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                StudentId INTEGER NOT NULL,
                AccessCode TEXT NOT NULL UNIQUE,
                ParentName TEXT,
                ParentPhone TEXT,
                ParentEmail TEXT,
                CreatedDate DATETIME NOT NULL,
                ExpiryDate DATETIME,
                SubscriptionExpiryDate DATETIME,
                UsageCount INTEGER DEFAULT 0,
                MaxUsage INTEGER DEFAULT 5,
                IsActive BOOLEAN DEFAULT 1,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_parentcodes_school ON ParentCodes(SchoolId, IsActive);", null, connection);
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_parentcodes_student ON ParentCodes(StudentId);", null, connection);
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_parentcodes_code ON ParentCodes(AccessCode);", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS AcademicPerformance (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                SubjectId INTEGER NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                T1 DECIMAL(5,2),
                T2 DECIMAL(5,2),
                T3 DECIMAL(5,2),
                T4 DECIMAL(5,2),
                T5 DECIMAL(5,2),
                T6 DECIMAL(5,2),
                Exam DECIMAL(5,2),
                FinalScore DECIMAL(5,2),
                Grade TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                FOREIGN KEY (SubjectId) REFERENCES Subjects(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS AffectiveScores (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term1ClassParticipationT1 INTEGER DEFAULT 3,
                Term1ClassParticipationT2 INTEGER DEFAULT 3,
                Term1HonestyT1 INTEGER DEFAULT 3,
                Term1HonestyT2 INTEGER DEFAULT 3,
                Term1EffortT1 INTEGER DEFAULT 3,
                Term1EffortT2 INTEGER DEFAULT 3,
                Term1SelfDisciplineT1 INTEGER DEFAULT 3,
                Term1SelfDisciplineT2 INTEGER DEFAULT 3,
                Term1PunctualityT1 INTEGER DEFAULT 3,
                Term1PunctualityT2 INTEGER DEFAULT 3,
                Term1SchoolInvolvementT1 INTEGER DEFAULT 3,
                Term1SchoolInvolvementT2 INTEGER DEFAULT 3,
                Term1NeatnessT1 INTEGER DEFAULT 3,
                Term1NeatnessT2 INTEGER DEFAULT 3,
                Term2ClassParticipationT1 INTEGER DEFAULT 3,
                Term2ClassParticipationT2 INTEGER DEFAULT 3,
                Term2HonestyT1 INTEGER DEFAULT 3,
                Term2HonestyT2 INTEGER DEFAULT 3,
                Term2EffortT1 INTEGER DEFAULT 3,
                Term2EffortT2 INTEGER DEFAULT 3,
                Term2SelfDisciplineT1 INTEGER DEFAULT 3,
                Term2SelfDisciplineT2 INTEGER DEFAULT 3,
                Term2PunctualityT1 INTEGER DEFAULT 3,
                Term2PunctualityT2 INTEGER DEFAULT 3,
                Term2SchoolInvolvementT1 INTEGER DEFAULT 3,
                Term2SchoolInvolvementT2 INTEGER DEFAULT 3,
                Term2NeatnessT1 INTEGER DEFAULT 3,
                Term2NeatnessT2 INTEGER DEFAULT 3,
                Term3ClassParticipationT1 INTEGER DEFAULT 3,
                Term3ClassParticipationT2 INTEGER DEFAULT 3,
                Term3HonestyT1 INTEGER DEFAULT 3,
                Term3HonestyT2 INTEGER DEFAULT 3,
                Term3EffortT1 INTEGER DEFAULT 3,
                Term3EffortT2 INTEGER DEFAULT 3,
                Term3SelfDisciplineT1 INTEGER DEFAULT 3,
                Term3SelfDisciplineT2 INTEGER DEFAULT 3,
                Term3PunctualityT1 INTEGER DEFAULT 3,
                Term3PunctualityT2 INTEGER DEFAULT 3,
                Term3SchoolInvolvementT1 INTEGER DEFAULT 3,
                Term3SchoolInvolvementT2 INTEGER DEFAULT 3,
                Term3NeatnessT1 INTEGER DEFAULT 3,
                Term3NeatnessT2 INTEGER DEFAULT 3,
                Term1Average DECIMAL(5,2),
                Term2Average DECIMAL(5,2),
                Term3Average DECIMAL(5,2),
                FinalAverage DECIMAL(5,2),
                FinalGrade TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS PsychomotorScores (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term1IndividualSportsT1 INTEGER DEFAULT 3,
                Term1IndividualSportsT2 INTEGER DEFAULT 3,
                Term1TeamPlayerT1 INTEGER DEFAULT 3,
                Term1TeamPlayerT2 INTEGER DEFAULT 3,
                Term1AthleticsT1 INTEGER DEFAULT 3,
                Term1AthleticsT2 INTEGER DEFAULT 3,
                Term1DrawingT1 INTEGER DEFAULT 3,
                Term1DrawingT2 INTEGER DEFAULT 3,
                Term1HandwritingT1 INTEGER DEFAULT 3,
                Term1HandwritingT2 INTEGER DEFAULT 3,
                Term2IndividualSportsT1 INTEGER DEFAULT 3,
                Term2IndividualSportsT2 INTEGER DEFAULT 3,
                Term2TeamPlayerT1 INTEGER DEFAULT 3,
                Term2TeamPlayerT2 INTEGER DEFAULT 3,
                Term2AthleticsT1 INTEGER DEFAULT 3,
                Term2AthleticsT2 INTEGER DEFAULT 3,
                Term2DrawingT1 INTEGER DEFAULT 3,
                Term2DrawingT2 INTEGER DEFAULT 3,
                Term2HandwritingT1 INTEGER DEFAULT 3,
                Term2HandwritingT2 INTEGER DEFAULT 3,
                Term3IndividualSportsT1 INTEGER DEFAULT 3,
                Term3IndividualSportsT2 INTEGER DEFAULT 3,
                Term3TeamPlayerT1 INTEGER DEFAULT 3,
                Term3TeamPlayerT2 INTEGER DEFAULT 3,
                Term3AthleticsT1 INTEGER DEFAULT 3,
                Term3AthleticsT2 INTEGER DEFAULT 3,
                Term3DrawingT1 INTEGER DEFAULT 3,
                Term3DrawingT2 INTEGER DEFAULT 3,
                Term3HandwritingT1 INTEGER DEFAULT 3,
                Term3HandwritingT2 INTEGER DEFAULT 3,
                Term1Average DECIMAL(5,2),
                Term2Average DECIMAL(5,2),
                Term3Average DECIMAL(5,2),
                FinalAverage DECIMAL(5,2),
                FinalGrade TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Attendance (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                DaysPresent INTEGER DEFAULT 0,
                DaysAbsent INTEGER DEFAULT 0,
                DaysLate INTEGER DEFAULT 0,
                SchoolDays INTEGER DEFAULT 180,
                Percentage DECIMAL(5,2),
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS DailyAttendance (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                Date DATETIME NOT NULL,
                Status TEXT NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term INTEGER,
                Remarks TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                UNIQUE(StudentId, Date, AcademicYear))", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Role TEXT NOT NULL,
                Phone TEXT,
                Email TEXT,
                IsActive BOOLEAN DEFAULT 1,
                LastLoginDate DATETIME,
                IsDeleted BOOLEAN DEFAULT 0,
                DeletedDate DATETIME,
                DeletedBy TEXT,
                PasswordResetRequired BOOLEAN DEFAULT 0,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS ExamSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                SessionName TEXT NOT NULL,
                Term INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                StartDate DATETIME NOT NULL,
                EndDate DATETIME NOT NULL,
                IsActive BOOLEAN DEFAULT 0,
                AllowEditing BOOLEAN DEFAULT 1,
                Status TEXT DEFAULT 'Upcoming',
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER,
                SchoolId INTEGER,
                SettingKey TEXT NOT NULL,
                SettingValue TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Backups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BackupName TEXT NOT NULL,
                BackupDate DATETIME NOT NULL,
                FileSize INTEGER NOT NULL,
                BackupPath TEXT NOT NULL,
                BackupType TEXT NOT NULL,
                BackupBy TEXT NOT NULL,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS LicenseAudit (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LicenseKey TEXT NOT NULL,
                Action TEXT NOT NULL,
                PerformedBy TEXT NOT NULL,
                PerformedByRole TEXT,
                IPAddress TEXT,
                Details TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS StudentMoveAudit (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                FromClassId INTEGER,
                ToClassId INTEGER NOT NULL,
                PerformedByUserId INTEGER,
                PerformedByName TEXT,
                PerformedByRole TEXT,
                AcademicYear TEXT,
                Details TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS TrialAnalytics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                InstallationId TEXT,
                TrialStartDate DATETIME,
                TrialEndDate DATETIME,
                StudentsCreatedDuringTrial INTEGER DEFAULT 0,
                ClassesCreatedDuringTrial INTEGER DEFAULT 0,
                UsersCreatedDuringTrial INTEGER DEFAULT 0,
                ConvertedToPaid BOOLEAN DEFAULT 0,
                ConversionDate DATETIME,
                LicenseKeyUsed TEXT,
                DeviceId TEXT,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS SchoolReportConfig (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL UNIQUE,
                ResultType TEXT DEFAULT 'FiveTests',
                NumberOfTerms INTEGER DEFAULT 3,
                PaperOrientation TEXT DEFAULT 'Portrait',
                ShowBehaviourSection BOOLEAN DEFAULT 1,
                ShowPsychomotorSection BOOLEAN DEFAULT 1,
                ShowAttendanceSection BOOLEAN DEFAULT 1,
                ShowSubjectAnalysis BOOLEAN DEFAULT 1,
                HeaderLogoPath TEXT,
                FooterText TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS SchoolPromotionCriteria (
               Id INTEGER PRIMARY KEY AUTOINCREMENT,
               SchoolId INTEGER NOT NULL,
               ClassId INTEGER,
               CriteriaType TEXT NOT NULL DEFAULT 'Average',
               MinAveragePercentage DECIMAL(5,2),
               MinCoreSubjectsPassed INTEGER,
               MinOptionalSubjectsPassed INTEGER,
               MinTotalSubjectsPassed INTEGER,
               CoreSubjectPassMark DECIMAL(5,2) DEFAULT 50,
               OptionalSubjectPassMark DECIMAL(5,2) DEFAULT 50,
               TotalPassMark DECIMAL(5,2) DEFAULT 50,
               RequireEnglishOrMaths BOOLEAN DEFAULT 0,
               EnglishMathsPassMark DECIMAL(5,2) DEFAULT 50,
               IncludeAttendance BOOLEAN DEFAULT 0,
               MinAttendancePercentage DECIMAL(5,2) DEFAULT 75,
               CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
               ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
               FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS PaymentTransactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ReceiptNumber TEXT NOT NULL UNIQUE,
                StudentId INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term INTEGER,
                Amount DECIMAL(15,2) NOT NULL,
                PaymentDate DATETIME NOT NULL,
                PaymentMethod TEXT NOT NULL,
                TransactionReference TEXT,
                ReceivedByUserId INTEGER,
                Notes TEXT,
                IsVoid BOOLEAN DEFAULT 0,
                VoidDate DATETIME,
                VoidReason TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                FOREIGN KEY (ReceivedByUserId) REFERENCES Users(Id) ON DELETE SET NULL)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS PaymentDetails (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TransactionId INTEGER NOT NULL,
                FeeAssignmentId INTEGER NOT NULL,
                AmountPaid DECIMAL(15,2) NOT NULL,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (TransactionId) REFERENCES PaymentTransactions(Id) ON DELETE CASCADE,
                FOREIGN KEY (FeeAssignmentId) REFERENCES FeeAssignments(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS FeeStructures (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                FeeName TEXT NOT NULL,
                FeeCode TEXT,
                Description TEXT,
                Amount DECIMAL(15,2) NOT NULL,
                Term INTEGER,
                ClassId INTEGER,
                AcademicYear TEXT,
                DueDate DATETIME NOT NULL,
                IsMandatory BOOLEAN DEFAULT 1,
                IsActive BOOLEAN DEFAULT 1,
                Frequency TEXT DEFAULT 'Termly',
                Category TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId) REFERENCES Classes(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS FeeAssignments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                FeeStructureId INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term INTEGER NOT NULL,
                Amount DECIMAL(15,2) NOT NULL,
                AmountPaid DECIMAL(15,2) DEFAULT 0,
                DueDate DATETIME NOT NULL,
                Status TEXT DEFAULT 'Unpaid',
                Remarks TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                FOREIGN KEY (FeeStructureId) REFERENCES FeeStructures(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Expenses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                ExpenseNumber TEXT NOT NULL UNIQUE,
                ExpenseName TEXT NOT NULL,
                Amount DECIMAL(15,2) NOT NULL,
                Category TEXT NOT NULL,
                PaymentMethod TEXT NOT NULL,
                ExpenseDate DATETIME NOT NULL,
                Vendor TEXT,
                InvoiceNumber TEXT,
                Description TEXT,
                Status TEXT DEFAULT 'Approved',
                ReceiptAttached BOOLEAN DEFAULT 0,
                ReceiptPath TEXT,
                EnteredByUserId INTEGER,
                ApprovedBy TEXT,
                ApprovalDate DATETIME,
                Notes TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE,
                FOREIGN KEY (EnteredByUserId) REFERENCES Users(Id) ON DELETE SET NULL)", null, connection);

            EnsureStudentSpecialCaseTable(connection);

            CreateBECECertificatesTable(connection);

            CreateBECETables(connection);
            CreateBECEIndexes(connection);
        }

        private void EnsureStreamColumnExists(SqliteConnection connection)
        {
            try
            {
                string checkColumn = "SELECT COUNT(*) FROM pragma_table_info('Students') WHERE name='Stream'";
                int exists = Convert.ToInt32(ExecuteScalar(checkColumn, null, connection));
                if (exists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE Students ADD COLUMN Stream TEXT", null, connection);
                    Debug.WriteLine("Added Stream column to Students table");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureStreamColumnExists error: {ex.Message}");
            }
        }

        private void EnsureSchoolReportConfigResultTypeColumn(SqliteConnection connection)
        {
            try
            {
                string checkColumn = "SELECT COUNT(*) FROM pragma_table_info('SchoolReportConfig') WHERE name='ResultType'";
                int exists = Convert.ToInt32(ExecuteScalar(checkColumn, null, connection));
                if (exists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE SchoolReportConfig ADD COLUMN ResultType TEXT DEFAULT 'FiveTests'", null, connection);
                    Debug.WriteLine("Added ResultType column to SchoolReportConfig table");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSchoolReportConfigResultTypeColumn error: {ex.Message}");
            }
        }

        private void CreateIndexes(SqliteConnection connection)
        {
            string[] indexes = {
                "CREATE INDEX IF NOT EXISTS idx_students_admission ON Students(AdmissionNumber);",
                "CREATE INDEX IF NOT EXISTS idx_students_class ON Students(ClassId, IsActive);",
                "CREATE INDEX IF NOT EXISTS idx_scores_lookup ON AcademicPerformance(StudentId, Term, AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_users_login ON Users(Username, SchoolId, IsActive);",
                "CREATE INDEX IF NOT EXISTS idx_classes_school ON Classes(SchoolId, AcademicYear, IsActive);",
                "CREATE INDEX IF NOT EXISTS idx_affective_student ON AffectiveScores(StudentId, Term, AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_psychomotor_student ON PsychomotorScores(StudentId, Term, AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_settings_student ON Settings(StudentId, SettingKey);",
                "CREATE INDEX IF NOT EXISTS idx_dailyattendance_student ON DailyAttendance(StudentId, AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_dailyattendance_date ON DailyAttendance(Date);",
                "CREATE INDEX IF NOT EXISTS idx_payment_student ON PaymentTransactions(StudentId, PaymentDate);",
                "CREATE INDEX IF NOT EXISTS idx_fee_structure_school ON FeeStructures(SchoolId, IsActive);",
                "CREATE INDEX IF NOT EXISTS idx_fee_assignment_student ON FeeAssignments(StudentId, AcademicYear, Term);",
                "CREATE INDEX IF NOT EXISTS idx_expenses_school ON Expenses(SchoolId, ExpenseDate);",
                "CREATE INDEX IF NOT EXISTS idx_studentspecialcase_lookup ON StudentSpecialCase(StudentId, AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_bece_certificates_student ON BECECertificates(StudentId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_certificates_school ON BECECertificates(SchoolId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_certificates_index ON BECECertificates(IndexNumber);",
                "CREATE INDEX IF NOT EXISTS idx_bece_certificates_date ON BECECertificates(CreatedDate);",
                "CREATE INDEX IF NOT EXISTS idx_bece_schools_waec ON BECESchools(WAECCode);",
                "CREATE INDEX IF NOT EXISTS idx_bece_schools_academic ON BECESchools(AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_bece_students_school ON BECEStudents(BECESchoolId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_students_index ON BECEStudents(IndexNumber);",
                "CREATE INDEX IF NOT EXISTS idx_bece_scores_student ON BECEScores(BECEStudentId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_results_student ON BECEResults(BECEStudentId);"
            };
            foreach (string indexQuery in indexes)
            {
                try { ExecuteNonQuery(indexQuery, null, connection); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Index creation warning: {ex.Message}");
                }
            }
        }

        private void AddSoftDeleteColumns(SqliteConnection connection)
        {
            string[] softDeleteTables = { "Students", "Classes", "Subjects", "Users" };
            foreach (string table in softDeleteTables)
            {
                try
                {
                    string checkColumn = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='IsDeleted'";
                    int columnExists = Convert.ToInt32(ExecuteScalar(checkColumn, null, connection));
                    if (columnExists == 0)
                    {
                        ExecuteNonQuery($"ALTER TABLE {table} ADD COLUMN IsDeleted BOOLEAN DEFAULT 0", null, connection);
                        ExecuteNonQuery($"ALTER TABLE {table} ADD COLUMN DeletedDate DATETIME", null, connection);
                        ExecuteNonQuery($"ALTER TABLE {table} ADD COLUMN DeletedBy TEXT", null, connection);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AddSoftDeleteColumns({table}) error: {ex.Message}");
                }
            }
        }

        private void UpgradeTablesForSixTests(SqliteConnection connection)
        {
            try
            {
                string checkT5 = "SELECT COUNT(*) FROM pragma_table_info('AcademicPerformance') WHERE name='T5'";
                int t5Exists = Convert.ToInt32(ExecuteScalar(checkT5, null, connection));
                if (t5Exists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE AcademicPerformance ADD COLUMN T5 DECIMAL(5,2)", null, connection);
                    ExecuteNonQuery("ALTER TABLE AcademicPerformance ADD COLUMN T6 DECIMAL(5,2)", null, connection);
                }

                try
                {
                    string checkFinal = "SELECT COUNT(*) FROM pragma_table_info('AcademicPerformance') WHERE name='FinalScore'";
                    int finalExists = Convert.ToInt32(ExecuteScalar(checkFinal, null, connection));
                    if (finalExists == 0)
                    {
                        ExecuteNonQuery("ALTER TABLE AcademicPerformance ADD COLUMN FinalScore DECIMAL(5,2)", null, connection);
                    }
                }
                catch { }

                try
                {
                    string checkGrade = "SELECT COUNT(*) FROM pragma_table_info('AcademicPerformance') WHERE name='Grade'";
                    int gradeExists = Convert.ToInt32(ExecuteScalar(checkGrade, null, connection));
                    if (gradeExists == 0)
                    {
                        ExecuteNonQuery("ALTER TABLE AcademicPerformance ADD COLUMN Grade TEXT", null, connection);
                    }
                }
                catch { }

                string checkResultType = "SELECT COUNT(*) FROM pragma_table_info('Schools') WHERE name='ResultType'";
                int resultTypeExists = Convert.ToInt32(ExecuteScalar(checkResultType, null, connection));
                if (resultTypeExists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE Schools ADD COLUMN ResultType TEXT DEFAULT 'FiveTests'", null, connection);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpgradeTablesForSixTests Error: {ex.Message}");
            }
        }
        public void EnsureAttendanceLateColumn()
        {
            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();

                string checkColumn = "SELECT COUNT(*) FROM pragma_table_info('Attendance') WHERE name='DaysLate'";
                int exists = Convert.ToInt32(ExecuteScalar(checkColumn, null, connection));

                if (exists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE Attendance ADD COLUMN DaysLate INTEGER DEFAULT 0", null, connection);
                    Debug.WriteLine("Added DaysLate column to Attendance table");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureAttendanceLateColumn error: {ex.Message}");
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }
        private void EnsurePhotoPathColumnExists(SqliteConnection connection)
        {
            try
            {
                string checkColumn = "SELECT COUNT(*) FROM pragma_table_info('Students') WHERE name='PhotoPath'";
                int exists = Convert.ToInt32(ExecuteScalar(checkColumn, null, connection));
                if (exists == 0)
                {
                    ExecuteNonQuery("ALTER TABLE Students ADD COLUMN PhotoPath TEXT", null, connection);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding PhotoPath column: {ex.Message}");
            }
        }

        public void EnsurePhotoPathColumnExistsPublic()
        {
            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();
                EnsurePhotoPathColumnExists(connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring PhotoPath column: {ex.Message}");
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }

        private void EnsureStudentSpecialCaseTable(SqliteConnection connection)
        {
            try
            {
                string checkTable = "SELECT name FROM sqlite_master WHERE type='table' AND name='StudentSpecialCase'";
                var result = ExecuteScalar(checkTable, null, connection);
                if (result != null && result.ToString() == "StudentSpecialCase") return;

                ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS StudentSpecialCase (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentId INTEGER NOT NULL,
                    AcademicYear TEXT NOT NULL,
                    SelectedTests TEXT NOT NULL,
                    Term1Formula TEXT,
                    Term2Formula TEXT,
                    Term3Formula TEXT,
                    Percent60Formula TEXT,
                    AnnualMeanFormula TEXT,
                    FinalScoreFormula TEXT,
                    IsActive BOOLEAN DEFAULT 1,
                    IsSSSI BOOLEAN DEFAULT 0,
                    ResultType TEXT DEFAULT 'FiveTests',
                    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                    UNIQUE(StudentId, AcademicYear))", null, connection);

                ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_studentspecialcase_lookup ON StudentSpecialCase(StudentId, AcademicYear)", null, connection);

                Debug.WriteLine("StudentSpecialCase table created successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureStudentSpecialCaseTable error: {ex.Message}");
            }
        }

        private void EnsureStudentSpecialCaseColumns(SqliteConnection connection)
        {
            try
            {
                var columnNames = GetTableColumnNames(connection, "StudentSpecialCase");

                if (!columnNames.Contains("AnnualMeanFormula"))
                {
                    try { ExecuteNonQuery("ALTER TABLE StudentSpecialCase ADD COLUMN AnnualMeanFormula TEXT", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add AnnualMeanFormula column error: {ex.Message}"); }
                }
                if (!columnNames.Contains("IsSSSI"))
                {
                    try { ExecuteNonQuery("ALTER TABLE StudentSpecialCase ADD COLUMN IsSSSI BOOLEAN DEFAULT 0", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add IsSSSI column error: {ex.Message}"); }
                }

                if (!columnNames.Contains("ResultType"))
                {
                    try { ExecuteNonQuery("ALTER TABLE StudentSpecialCase ADD COLUMN ResultType TEXT DEFAULT 'FiveTests'", null, connection); }
                    catch (Exception ex) { Debug.WriteLine($"Add ResultType column error: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureStudentSpecialCaseColumns error: {ex.Message}");
            }
        }

        private void CreateBECETables(SqliteConnection connection)
        {
            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECESchools (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        SchoolName TEXT NOT NULL,
        SchoolLocation TEXT NOT NULL,
        SchoolMotto TEXT,
        WAECCode TEXT NOT NULL UNIQUE,
        CentreNumber TEXT,
        AcademicYear TEXT NOT NULL,
        SchoolCrestPath TEXT,
        PrincipalName TEXT,
        CARTeacherName TEXT,
        SchoolContact TEXT,
        RegistrationDate DATETIME NOT NULL,
        IsActive BOOLEAN DEFAULT 1,
        DeletedDate DATETIME,
        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECEStudents (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        BECESchoolId INTEGER NOT NULL,
        StudentName TEXT NOT NULL,
        Gender TEXT NOT NULL,
        StudentSerialNumber TEXT NOT NULL,
        IndexNumber TEXT NOT NULL UNIQUE,
        StudentPhotoPath TEXT,
        RegistrationDate DATETIME NOT NULL,
        IsActive BOOLEAN DEFAULT 1,
        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY (BECESchoolId) REFERENCES BECESchools(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECEScores (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        BECEStudentId INTEGER NOT NULL,
        SubjectCategory TEXT NOT NULL,
        SubjectName TEXT NOT NULL,
        SubjectCode TEXT,
        Score INTEGER NOT NULL,
        GradeWord TEXT,
        Interpretation TEXT,
        EntryDate DATETIME DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY (BECEStudentId) REFERENCES BECEStudents(Id) ON DELETE CASCADE)", null, connection);

            ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECEResults (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        BECEStudentId INTEGER NOT NULL,
        Aggregate INTEGER NOT NULL,
        CoreSubjectsPassed INTEGER NOT NULL,
        OptionalSubjectsPassed INTEGER NOT NULL,
        TotalSubjectsPassed INTEGER NOT NULL,
        Credits INTEGER DEFAULT 0,
        Passes INTEGER DEFAULT 0,
        OverallPerformance TEXT,
        CertificatePath TEXT,
        GeneratedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY (BECEStudentId) REFERENCES BECEStudents(Id) ON DELETE CASCADE)", null, connection);
        }

        private void CreateBECEIndexes(SqliteConnection connection)
        {
            string[] indexes = {
                "CREATE INDEX IF NOT EXISTS idx_bece_schools_waec ON BECESchools(WAECCode);",
                "CREATE INDEX IF NOT EXISTS idx_bece_schools_academic ON BECESchools(AcademicYear);",
                "CREATE INDEX IF NOT EXISTS idx_bece_students_school ON BECEStudents(BECESchoolId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_students_index ON BECEStudents(IndexNumber);",
                "CREATE INDEX IF NOT EXISTS idx_bece_scores_student ON BECEScores(BECEStudentId);",
                "CREATE INDEX IF NOT EXISTS idx_bece_results_student ON BECEResults(BECEStudentId);"
            };

            foreach (string index in indexes)
            {
                try { ExecuteNonQuery(index, null, connection); }
                catch (Exception ex) { Debug.WriteLine($"Index creation error: {ex.Message}"); }
            }
        }

        public bool DeleteBECESchool(int beceSchoolId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@Id", beceSchoolId } };
                return ExecuteNonQuery("DELETE FROM BECESchools WHERE Id = @Id", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteBECESchool error: {ex.Message}");
                return false;
            }
        }

        public List<BECESchool> GetBECESchools(bool includeDeleted = false)
        {
            var schools = new List<BECESchool>();
            string query = includeDeleted ? "SELECT * FROM BECESchools ORDER BY CreatedDate DESC"
                                          : "SELECT * FROM BECESchools WHERE (DeletedDate IS NULL OR DeletedDate = '') ORDER BY CreatedDate DESC";

            var dt = ExecuteQuery(query);
            foreach (DataRow row in dt.Rows)
            {
                schools.Add(MapToBECESchool(row));
            }
            return schools;
        }

        public BECESchool GetBECESchoolById(int id)
        {
            var dt = ExecuteQuery("SELECT * FROM BECESchools WHERE Id = @Id",
                new Dictionary<string, object> { { "@Id", id } });

            if (dt.Rows.Count == 0) return null;
            return MapToBECESchool(dt.Rows[0]);
        }

        public int InsertBECESchool(BECESchool school)
        {
            string query = @"INSERT INTO BECESchools 
        (SchoolName, SchoolLocation, SchoolMotto, WAECCode, CentreNumber, AcademicYear, 
         SchoolCrestPath, PrincipalName, CARTeacherName, SchoolContact, RegistrationDate, IsActive, CreatedDate)
        VALUES 
        (@SchoolName, @SchoolLocation, @SchoolMotto, @WAECCode, @CentreNumber, @AcademicYear,
         @SchoolCrestPath, @PrincipalName, @CARTeacherName, @SchoolContact, @RegistrationDate, @IsActive, @CreatedDate);
        SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
    {
        { "@SchoolName", school.SchoolName },
        { "@SchoolLocation", school.SchoolLocation },
        { "@SchoolMotto", school.SchoolMotto ?? "" },
        { "@WAECCode", school.WAECCode },
        { "@CentreNumber", school.CentreNumber ?? "" },
        { "@AcademicYear", school.AcademicYear },
        { "@SchoolCrestPath", school.SchoolCrestPath ?? "" },
        { "@PrincipalName", school.PrincipalName ?? "" },
        { "@CARTeacherName", school.CARTeacherName ?? "" },
        { "@SchoolContact", school.SchoolContact ?? "" },
        { "@RegistrationDate", school.RegistrationDate },
        { "@IsActive", school.IsActive },
        { "@CreatedDate", DateTime.Now }
    };

            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool UpdateBECESchool(BECESchool school)
        {
            string query = @"UPDATE BECESchools SET 
        SchoolName = @SchoolName, SchoolLocation = @SchoolLocation, SchoolMotto = @SchoolMotto,
        WAECCode = @WAECCode, CentreNumber = @CentreNumber, AcademicYear = @AcademicYear,
        SchoolCrestPath = @SchoolCrestPath, PrincipalName = @PrincipalName, CARTeacherName = @CARTeacherName,
        SchoolContact = @SchoolContact, IsActive = @IsActive
        WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
    {
        { "@Id", school.Id },
        { "@SchoolName", school.SchoolName },
        { "@SchoolLocation", school.SchoolLocation },
        { "@SchoolMotto", school.SchoolMotto ?? "" },
        { "@WAECCode", school.WAECCode },
        { "@CentreNumber", school.CentreNumber ?? "" },
        { "@AcademicYear", school.AcademicYear },
        { "@SchoolCrestPath", school.SchoolCrestPath ?? "" },
        { "@PrincipalName", school.PrincipalName ?? "" },
        { "@CARTeacherName", school.CARTeacherName ?? "" },
        { "@SchoolContact", school.SchoolContact ?? "" },
        { "@IsActive", school.IsActive }
    };

            return ExecuteNonQuery(query, parameters) > 0;
        }

        public List<BECEStudent> GetBECEStudents(int beceSchoolId)
        {
            var students = new List<BECEStudent>();
            var dt = ExecuteQuery("SELECT * FROM BECEStudents WHERE BECESchoolId = @SchoolId ORDER BY StudentName",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });

            foreach (DataRow row in dt.Rows)
            {
                students.Add(MapToBECEStudent(row));
            }
            return students;
        }

        public BECEStudent GetBECEStudentById(int id)
        {
            var dt = ExecuteQuery("SELECT * FROM BECEStudents WHERE Id = @Id",
                new Dictionary<string, object> { { "@Id", id } });

            if (dt.Rows.Count == 0) return null;
            return MapToBECEStudent(dt.Rows[0]);
        }

        public int InsertBECEStudent(BECEStudent student)
        {
            string query = @"INSERT INTO BECEStudents 
                (BECESchoolId, StudentName, Gender, StudentSerialNumber, IndexNumber, StudentPhotoPath, RegistrationDate, IsActive, CreatedDate)
                VALUES 
                (@BECESchoolId, @StudentName, @Gender, @StudentSerialNumber, @IndexNumber, @StudentPhotoPath, @RegistrationDate, @IsActive, @CreatedDate);
                SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
            {
                { "@BECESchoolId", student.BECESchoolId },
                { "@StudentName", student.StudentName },
                { "@Gender", student.Gender },
                { "@StudentSerialNumber", student.StudentSerialNumber },
                { "@IndexNumber", student.IndexNumber },
                { "@StudentPhotoPath", student.StudentPhotoPath ?? "" },
                { "@RegistrationDate", student.RegistrationDate },
                { "@IsActive", student.IsActive },
                { "@CreatedDate", DateTime.Now }
            };

            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool UpdateBECEStudent(BECEStudent student)
        {
            string query = @"UPDATE BECEStudents SET 
                StudentName = @StudentName, Gender = @Gender, StudentSerialNumber = @StudentSerialNumber,
                IndexNumber = @IndexNumber, StudentPhotoPath = @StudentPhotoPath, IsActive = @IsActive
                WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
            {
                { "@Id", student.Id },
                { "@StudentName", student.StudentName },
                { "@Gender", student.Gender },
                { "@StudentSerialNumber", student.StudentSerialNumber },
                { "@IndexNumber", student.IndexNumber },
                { "@StudentPhotoPath", student.StudentPhotoPath ?? "" },
                { "@IsActive", student.IsActive }
            };

            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool DeleteBECEStudent(int studentId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", studentId } };
            return ExecuteNonQuery("DELETE FROM BECEStudents WHERE Id = @Id", parameters) > 0;
        }

        public List<BECESubjectScore> GetBECEScores(int beceStudentId)
        {
            var scores = new List<BECESubjectScore>();
            var dt = ExecuteQuery("SELECT * FROM BECEScores WHERE BECEStudentId = @StudentId ORDER BY SubjectCategory, SubjectName",
                new Dictionary<string, object> { { "@StudentId", beceStudentId } });

            foreach (DataRow row in dt.Rows)
            {
                scores.Add(MapToBECEScore(row));
            }
            return scores;
        }

        public bool SaveBECEScore(BECESubjectScore score)
        {
            var checkParams = new Dictionary<string, object>
            {
                { "@BECEStudentId", score.BECEStudentId },
                { "@SubjectName", score.SubjectName }
            };

            int exists = Convert.ToInt32(ExecuteScalar(
                "SELECT COUNT(*) FROM BECEScores WHERE BECEStudentId = @BECEStudentId AND SubjectName = @SubjectName",
                checkParams));

            if (exists > 0)
            {
                var updateParams = new Dictionary<string, object>
                {
                    { "@BECEStudentId", score.BECEStudentId },
                    { "@SubjectName", score.SubjectName },
                    { "@SubjectCategory", score.SubjectCategory },
                    { "@SubjectCode", score.SubjectCode ?? "" },
                    { "@Score", score.Score },
                    { "@GradeWord", score.GradeWord ?? "" },
                    { "@Interpretation", score.Interpretation ?? "" }
                };
                return ExecuteNonQuery(@"UPDATE BECEScores SET 
                    SubjectCategory = @SubjectCategory, SubjectCode = @SubjectCode, Score = @Score,
                    GradeWord = @GradeWord, Interpretation = @Interpretation, EntryDate = CURRENT_TIMESTAMP
                    WHERE BECEStudentId = @BECEStudentId AND SubjectName = @SubjectName", updateParams) > 0;
            }
            else
            {
                var insertParams = new Dictionary<string, object>
                {
                    { "@BECEStudentId", score.BECEStudentId },
                    { "@SubjectCategory", score.SubjectCategory },
                    { "@SubjectName", score.SubjectName },
                    { "@SubjectCode", score.SubjectCode ?? "" },
                    { "@Score", score.Score },
                    { "@GradeWord", score.GradeWord ?? "" },
                    { "@Interpretation", score.Interpretation ?? "" }
                };
                return ExecuteNonQuery(@"INSERT INTO BECEScores 
                    (BECEStudentId, SubjectCategory, SubjectName, SubjectCode, Score, GradeWord, Interpretation, EntryDate)
                    VALUES 
                    (@BECEStudentId, @SubjectCategory, @SubjectName, @SubjectCode, @Score, @GradeWord, @Interpretation, CURRENT_TIMESTAMP)",
                    insertParams) > 0;
            }
        }

        public BECEResult GetBECEResult(int beceStudentId)
        {
            var dt = ExecuteQuery("SELECT * FROM BECEResults WHERE BECEStudentId = @StudentId ORDER BY GeneratedDate DESC LIMIT 1",
                new Dictionary<string, object> { { "@StudentId", beceStudentId } });

            if (dt.Rows.Count == 0) return null;
            return MapToBECEResult(dt.Rows[0]);
        }

        public bool SaveBECEResult(BECEResult result)
        {
            var checkParams = new Dictionary<string, object> { { "@BECEStudentId", result.BECEStudentId } };
            int exists = Convert.ToInt32(ExecuteScalar(
                "SELECT COUNT(*) FROM BECEResults WHERE BECEStudentId = @BECEStudentId", checkParams));

            if (exists > 0)
            {
                var updateParams = new Dictionary<string, object>
        {
            { "@BECEStudentId", result.BECEStudentId },
            { "@Aggregate", result.Aggregate },
            { "@CoreSubjectsPassed", result.CoreSubjectsPassed },
            { "@OptionalSubjectsPassed", result.OptionalSubjectsPassed },
            { "@TotalSubjectsPassed", result.TotalSubjectsPassed },
            { "@Credits", result.Credits },
            { "@Passes", result.Passes },
            { "@OverallPerformance", result.OverallPerformance ?? "" },
            { "@CertificatePath", result.CertificatePath ?? "" }
        };
                return ExecuteNonQuery(@"UPDATE BECEResults SET 
            Aggregate = @Aggregate, CoreSubjectsPassed = @CoreSubjectsPassed,
            OptionalSubjectsPassed = @OptionalSubjectsPassed, TotalSubjectsPassed = @TotalSubjectsPassed,
            Credits = @Credits, Passes = @Passes, OverallPerformance = @OverallPerformance,
            CertificatePath = @CertificatePath, GeneratedDate = CURRENT_TIMESTAMP
            WHERE BECEStudentId = @BECEStudentId", updateParams) > 0;
            }
            else
            {
                var insertParams = new Dictionary<string, object>
        {
            { "@BECEStudentId", result.BECEStudentId },
            { "@Aggregate", result.Aggregate },
            { "@CoreSubjectsPassed", result.CoreSubjectsPassed },
            { "@OptionalSubjectsPassed", result.OptionalSubjectsPassed },
            { "@TotalSubjectsPassed", result.TotalSubjectsPassed },
            { "@Credits", result.Credits },
            { "@Passes", result.Passes },
            { "@OverallPerformance", result.OverallPerformance ?? "" },
            { "@CertificatePath", result.CertificatePath ?? "" }
        };
                return ExecuteNonQuery(@"INSERT INTO BECEResults 
            (BECEStudentId, Aggregate, CoreSubjectsPassed, OptionalSubjectsPassed, TotalSubjectsPassed, 
             Credits, Passes, OverallPerformance, CertificatePath, GeneratedDate)
            VALUES 
            (@BECEStudentId, @Aggregate, @CoreSubjectsPassed, @OptionalSubjectsPassed, @TotalSubjectsPassed,
             @Credits, @Passes, @OverallPerformance, @CertificatePath, CURRENT_TIMESTAMP)",
                    insertParams) > 0;
            }
        }

        public BECESchoolStats GetBECESchoolStats(int beceSchoolId)
        {
            var stats = new BECESchoolStats();

            object studentCount = ExecuteScalar("SELECT COUNT(*) FROM BECEStudents WHERE BECESchoolId = @SchoolId",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });
            stats.TotalStudents = Convert.ToInt32(studentCount);

            var dt = ExecuteQuery(@"SELECT COUNT(DISTINCT s.BECEStudentId) FROM BECEScores s 
                JOIN BECEStudents st ON s.BECEStudentId = st.Id 
                WHERE st.BECESchoolId = @SchoolId 
                GROUP BY s.BECEStudentId HAVING COUNT(*) >= 10",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });

            stats.ScoresComplete = dt.Rows.Count > 0 ? Convert.ToInt32(dt.Rows[0][0]) : 0;

            object certCount = ExecuteScalar(@"SELECT COUNT(*) FROM BECEResults r 
                JOIN BECEStudents s ON r.BECEStudentId = s.Id 
                WHERE s.BECESchoolId = @SchoolId",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });
            stats.CertificatesGenerated = Convert.ToInt32(certCount);

            object avgAgg = ExecuteScalar(@"SELECT AVG(r.Aggregate) FROM BECEResults r 
                JOIN BECEStudents s ON r.BECEStudentId = s.Id 
                WHERE s.BECESchoolId = @SchoolId",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });
            stats.AverageAggregate = avgAgg != DBNull.Value ? Convert.ToDecimal(avgAgg) : 0;

            object bestAgg = ExecuteScalar(@"SELECT MIN(r.Aggregate) FROM BECEResults r 
                JOIN BECEStudents s ON r.BECEStudentId = s.Id 
                WHERE s.BECESchoolId = @SchoolId",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });
            stats.BestAggregate = bestAgg != DBNull.Value ? Convert.ToInt32(bestAgg) : 0;

            object worstAgg = ExecuteScalar(@"SELECT MAX(r.Aggregate) FROM BECEResults r 
                JOIN BECEStudents s ON r.BECEStudentId = s.Id 
                WHERE s.BECESchoolId = @SchoolId",
                new Dictionary<string, object> { { "@SchoolId", beceSchoolId } });
            stats.WorstAggregate = worstAgg != DBNull.Value ? Convert.ToInt32(worstAgg) : 0;

            return stats;
        }

        private BECESchool MapToBECESchool(DataRow row)
        {
            return new BECESchool
            {
                Id = ConvertToInt(row["Id"]),
                SchoolName = row["SchoolName"]?.ToString(),
                SchoolLocation = row["SchoolLocation"]?.ToString(),
                SchoolMotto = row["SchoolMotto"]?.ToString(),
                WAECCode = row["WAECCode"]?.ToString(),
                CentreNumber = row["CentreNumber"]?.ToString(),
                AcademicYear = row["AcademicYear"]?.ToString(),
                SchoolCrestPath = row["SchoolCrestPath"]?.ToString(),
                PrincipalName = row["PrincipalName"]?.ToString(),
                CARTeacherName = row["CARTeacherName"]?.ToString(),
                SchoolContact = row["SchoolContact"]?.ToString(),
                RegistrationDate = ConvertToDateTime(row["RegistrationDate"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"])
            };
        }

        private BECEStudent MapToBECEStudent(DataRow row)
        {
            return new BECEStudent
            {
                Id = ConvertToInt(row["Id"]),
                BECESchoolId = ConvertToInt(row["BECESchoolId"]),
                StudentName = row["StudentName"]?.ToString(),
                Gender = row["Gender"]?.ToString(),
                StudentSerialNumber = row["StudentSerialNumber"]?.ToString(),
                IndexNumber = row["IndexNumber"]?.ToString(),
                StudentPhotoPath = row["StudentPhotoPath"]?.ToString(),
                RegistrationDate = ConvertToDateTime(row["RegistrationDate"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"])
            };
        }
        private void EnsurePromotionRecordsTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS PromotionRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                SchoolId INTEGER NOT NULL,
                ClassId INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                AnnualAverage DECIMAL(5,2) DEFAULT 0,
                CorePassed INTEGER DEFAULT 0,
                OptionalPassed INTEGER DEFAULT 0,
                TotalPassed INTEGER DEFAULT 0,
                TotalCore INTEGER DEFAULT 0,
                TotalOptional INTEGER DEFAULT 0,
                TotalSubjects INTEGER DEFAULT 0,
                Rank INTEGER DEFAULT 0,
                IsPromoted BOOLEAN DEFAULT 0,
                NextClass TEXT,
                Reason TEXT,
                CriteriaSnapshotJson TEXT,
                ComputedAt DATETIME NOT NULL,
                ComputedByUserId INTEGER,
                ComputedByName TEXT,
                IsManuallyOverridden BOOLEAN DEFAULT 0,
                OverrideReason TEXT,
                OverriddenAt DATETIME,
                OverriddenByUserId INTEGER,
                UNIQUE(StudentId, AcademicYear)
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_promotionrecords_student ON PromotionRecords(StudentId, AcademicYear);",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_promotionrecords_class ON PromotionRecords(ClassId, AcademicYear);",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_promotionrecords_school_year ON PromotionRecords(SchoolId, AcademicYear);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsurePromotionRecordsTable error: {ex.Message}");
            }
        }
        private BECESubjectScore MapToBECEScore(DataRow row)
        {
            return new BECESubjectScore
            {
                Id = ConvertToInt(row["Id"]),
                BECEStudentId = ConvertToInt(row["BECEStudentId"]),
                SubjectCategory = row["SubjectCategory"]?.ToString(),
                SubjectName = row["SubjectName"]?.ToString(),
                SubjectCode = row["SubjectCode"]?.ToString(),
                Score = ConvertToInt(row["Score"]),
                GradeWord = row["GradeWord"]?.ToString(),
                Interpretation = row["Interpretation"]?.ToString(),
                EntryDate = ConvertToDateTime(row["EntryDate"])
            };
        }

        private BECEResult MapToBECEResult(DataRow row)
        {
            return new BECEResult
            {
                Id = ConvertToInt(row["Id"]),
                BECEStudentId = ConvertToInt(row["BECEStudentId"]),
                Aggregate = ConvertToInt(row["Aggregate"]),
                CoreSubjectsPassed = ConvertToInt(row["CoreSubjectsPassed"]),
                OptionalSubjectsPassed = ConvertToInt(row["OptionalSubjectsPassed"]),
                TotalSubjectsPassed = ConvertToInt(row["TotalSubjectsPassed"]),
                Credits = row["Credits"] != DBNull.Value ? ConvertToInt(row["Credits"]) : 0,
                Passes = row["Passes"] != DBNull.Value ? ConvertToInt(row["Passes"]) : 0,
                OverallPerformance = row["OverallPerformance"]?.ToString(),
                CertificatePath = row["CertificatePath"]?.ToString(),
                GeneratedDate = ConvertToDateTime(row["GeneratedDate"])
            };
        }
        #endregion


        #region Connection Management

        public SqliteConnection GetConnection()
        {
            lock (_lockObject)
            {
                try
                {
                    bool needsNewConnection = false;
                    if (_sharedConnection == null)
                    {
                        needsNewConnection = true;
                    }
                    else
                    {
                        try
                        {
                            if (_sharedConnection.State != ConnectionState.Open)
                                needsNewConnection = true;
                        }
                        catch (ObjectDisposedException)
                        {
                            _sharedConnection = null;
                            _connectionCount = 0;
                            needsNewConnection = true;
                        }
                    }

                    if (needsNewConnection)
                    {
                        _sharedConnection = new SqliteConnection(_connectionString);
                        _sharedConnection.Open();

                        using (var cmd = _sharedConnection.CreateCommand())
                        {
                            cmd.CommandText = "PRAGMA synchronous = NORMAL;";
                            cmd.ExecuteNonQuery();
                            cmd.CommandText = "PRAGMA journal_mode = WAL;";
                            cmd.ExecuteNonQuery();
                            cmd.CommandText = "PRAGMA temp_store = MEMORY;";
                            cmd.ExecuteNonQuery();
                            cmd.CommandText = "PRAGMA cache_size = -2000;";
                            cmd.ExecuteNonQuery();
                        }
                        _connectionCount = 1;
                    }
                    else
                    {
                        _connectionCount++;
                    }
                    return _sharedConnection;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Database connection error: {ex.Message}", ex);
                }
            }
        }

        public void CloseConnection(SqliteConnection connection)
        {
            lock (_lockObject)
            {
                try
                {
                    if (connection == _sharedConnection)
                    {
                        _connectionCount--;
                        if (_connectionCount <= 0 && _sharedConnection != null)
                        {
                            _sharedConnection.Close();
                            _sharedConnection.Dispose();
                            _sharedConnection = null;
                            _connectionCount = 0;
                        }
                    }
                    else if (connection != null && connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                        connection.Dispose();
                    }
                }
                catch { }
            }
        }

        public static void CloseAllConnections()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_sharedConnection != null)
                    {
                        _sharedConnection.Close();
                        _sharedConnection.Dispose();
                        _sharedConnection = null;
                        _connectionCount = 0;
                    }
                }
                catch { }
            }
        }

        public int ExecuteNonQuery(string query, Dictionary<string, object> parameters = null, SqliteConnection connection = null)
        {
            bool closeConnection = false;
            try
            {
                if (connection == null) { connection = GetConnection(); closeConnection = true; }
                using (var command = new SqliteCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }
                    return command.ExecuteNonQuery();
                }
            }
            finally
            {
                if (closeConnection && connection != null) CloseConnection(connection);
            }
        }

        public object ExecuteScalar(string query, Dictionary<string, object> parameters = null, SqliteConnection connection = null)
        {
            bool closeConnection = false;
            try
            {
                if (connection == null) { connection = GetConnection(); closeConnection = true; }
                using (var command = new SqliteCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }
                    return command.ExecuteScalar();
                }
            }
            finally
            {
                if (closeConnection && connection != null) CloseConnection(connection);
            }
        }

        public DataTable ExecuteQuery(string query, Dictionary<string, object> parameters = null, SqliteConnection connection = null)
        {
            bool closeConnection = false;
            var dataTable = new DataTable();
            try
            {
                if (connection == null) { connection = GetConnection(); closeConnection = true; }
                using (var command = new SqliteCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }
                    using (var reader = command.ExecuteReader())
                    {
                        dataTable.Load(reader);
                    }
                }
                return dataTable;
            }
            finally
            {
                if (closeConnection && connection != null) CloseConnection(connection);
            }
        }

        public static string GetDatabaseFilePath()
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString)) return null;
                var parts = _connectionString.Split(';');
                foreach (var p in parts)
                {
                    if (p.Trim().StartsWith("Data Source", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = p.IndexOf('=');
                        if (idx >= 0) return p.Substring(idx + 1).Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region Master Admin Methods

        private bool HasRoleIdColumn()
        {
            if (_hasRoleIdColumn.HasValue) return _hasRoleIdColumn.Value;

            try
            {
                var names = GetTableColumnNames(null, "Users");
                _hasRoleIdColumn = names.Contains("RoleId");
                return _hasRoleIdColumn.Value;
            }
            catch
            {
                _hasRoleIdColumn = false;
                return false;
            }
        }

        public void InitializeMasterAdmin()
        {
            try
            {
                string masterUsername = "Alimamy";
                string masterPassword = "@limamy@mara$umah";
                string passwordHash = PasswordHasher.HashPassword(masterPassword);

                var existing = GetUserByUsername(masterUsername);

                if (existing == null)
                {
                    string query = @"INSERT INTO Users (SchoolId, Username, PasswordHash, FullName, Role, Phone, Email, IsActive, CreatedDate, ModifiedDate)
                                    VALUES (@SchoolId, @Username, @PasswordHash, @FullName, @Role, @Phone, @Email, 1, @CreatedDate, @ModifiedDate)";
                    var parameters = new Dictionary<string, object>
                    {
                        { "@SchoolId", null },
                        { "@Username", masterUsername },
                        { "@PasswordHash", passwordHash },
                        { "@FullName", "Master Administrator" },
                        { "@Role", "MasterAdmin" },
                        { "@Phone", "" },
                        { "@Email", "" },
                        { "@CreatedDate", DateTime.Now },
                        { "@ModifiedDate", DateTime.Now }
                    };
                    ExecuteNonQuery(query, parameters);
                    Debug.WriteLine("Master admin created successfully");
                }
                else
                {
                    bool needsUpdate = false;

                    if (existing.PasswordHash != passwordHash)
                    {
                        UpdateUserPassword(existing.Id, passwordHash);
                        needsUpdate = true;
                    }

                    if (existing.FullName != "Master Administrator" || existing.Role != "MasterAdmin" || !existing.IsActive)
                    {
                        existing.FullName = "Master Administrator";
                        existing.Role = "MasterAdmin";
                        existing.IsActive = true;
                        UpdateUser(existing);
                        needsUpdate = true;
                    }

                    if (needsUpdate)
                        Debug.WriteLine("Master admin credentials updated");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeMasterAdmin error: {ex.Message}");
            }
        }

        public User GetUserByUsername(string username)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@Username", username } };
                DataTable dt = ExecuteQuery("SELECT * FROM Users WHERE Username = @Username COLLATE NOCASE", parameters);

                if (dt.Rows.Count == 0) return null;

                DataRow row = dt.Rows[0];
                return new User
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolId = ConvertToNullableInt(row["SchoolId"]),
                    Username = ConvertToString(row["Username"]),
                    PasswordHash = ConvertToString(row["PasswordHash"]),
                    FullName = ConvertToString(row["FullName"]),
                    Role = ConvertToString(row["Role"]),
                    Phone = ConvertToString(row["Phone"]),
                    Email = ConvertToString(row["Email"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),
                    LastLoginDate = ConvertToDateTime(row["LastLoginDate"]),
                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserByUsername error: {ex.Message}");
                return null;
            }
        }

        public bool UpdateUserPassword(int userId, string passwordHash)
        {
            var parameters = new Dictionary<string, object> { { "@Id", userId }, { "@PasswordHash", passwordHash }, { "@ModifiedDate", DateTime.Now } };
            return ExecuteNonQuery("UPDATE Users SET PasswordHash = @PasswordHash, ModifiedDate = @ModifiedDate WHERE Id = @Id", parameters) > 0;
        }

        public bool UpdateUser(User user)
        {
            string query = @"UPDATE Users SET FullName = @FullName, Role = @Role, Phone = @Phone, Email = @Email, IsActive = @IsActive, ModifiedDate = @ModifiedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                {"@Id", user.Id},
                {"@FullName", user.FullName},
                {"@Role", user.Role},
                {"@Phone", user.Phone ?? ""},
                {"@Email", user.Email ?? ""},
                {"@IsActive", user.IsActive},
                {"@ModifiedDate", DateTime.Now}
            };
            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool DeleteUser(int userId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@Id", userId } };
                return ExecuteNonQuery("DELETE FROM Users WHERE Id = @Id", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteUser error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Trial Analytics Methods

        public void RecordTrialStart(string installationId, string deviceId, DateTime trialEndDate)
        {
            try
            {
                var existing = ExecuteScalar("SELECT COUNT(*) FROM TrialAnalytics WHERE InstallationId = @InstallationId",
                    new Dictionary<string, object> { { "@InstallationId", installationId } });

                if (existing != null && Convert.ToInt32(existing) > 0)
                    return;

                string query = @"INSERT INTO TrialAnalytics 
                    (InstallationId, TrialStartDate, TrialEndDate, StudentsCreatedDuringTrial, 
                     ClassesCreatedDuringTrial, UsersCreatedDuringTrial, ConvertedToPaid, DeviceId, CreatedAt)
                    VALUES (@InstallationId, @TrialStartDate, @TrialEndDate, 0, 0, 0, 0, @DeviceId, @CreatedAt)";

                var parameters = new Dictionary<string, object>
                {
                    { "@InstallationId", installationId },
                    { "@TrialStartDate", DateTime.Now },
                    { "@TrialEndDate", trialEndDate },
                    { "@DeviceId", deviceId ?? "" },
                    { "@CreatedAt", DateTime.Now }
                };

                ExecuteNonQuery(query, parameters);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecordTrialStart error: {ex.Message}");
            }
        }

        public DataTable GetTrialAnalytics()
        {
            return ExecuteQuery("SELECT * FROM TrialAnalytics ORDER BY TrialStartDate DESC");
        }

        #endregion

        #region School Methods

        public List<School> GetAllSchools()
        {
            var schools = new List<School>();
            DataTable dt = ExecuteQuery("SELECT * FROM Schools ORDER BY SchoolName");
            foreach (DataRow row in dt.Rows)
            {
                schools.Add(new School
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolName = ConvertToString(row["SchoolName"]),
                    District = ConvertToString(row["District"]),
                    Town = ConvertToString(row["Town"]),
                    Chiefdom = ConvertToString(row["Chiefdom"]),
                    Address = ConvertToString(row["Address"]),
                    Phone = ConvertToString(row["Phone"]),
                    Email = ConvertToString(row["Email"]),
                    PrincipalName = ConvertToString(row["PrincipalName"]),
                    LogoPath = ConvertToString(row["LogoPath"]),
                    LicenseKey = ConvertToString(row["LicenseKey"]),
                    LicenseExpiryDate = ConvertToDateTime(row["LicenseExpiryDate"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),
                    ResultType = ConvertToString(row["ResultType"]),
                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                });
            }
            return schools;
        }

        public School GetSchoolById(int schoolId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", schoolId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Schools WHERE Id = @Id", parameters);
            if (dt.Rows.Count == 0) return null;
            DataRow row = dt.Rows[0];
            return new School
            {
                Id = ConvertToInt(row["Id"]),
                SchoolName = ConvertToString(row["SchoolName"]),
                District = ConvertToString(row["District"]),
                Town = ConvertToString(row["Town"]),
                Chiefdom = ConvertToString(row["Chiefdom"]),
                Address = ConvertToString(row["Address"]),
                Phone = ConvertToString(row["Phone"]),
                Email = ConvertToString(row["Email"]),
                PrincipalName = ConvertToString(row["PrincipalName"]),
                LogoPath = ConvertToString(row["LogoPath"]),
                LicenseKey = ConvertToString(row["LicenseKey"]),
                LicenseExpiryDate = ConvertToDateTime(row["LicenseExpiryDate"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                ResultType = ConvertToString(row["ResultType"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };
        }

        public int InsertSchool(School school)
        {
            var checkParams = new Dictionary<string, object> { { "@SchoolName", school.SchoolName } };
            int exists = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM Schools WHERE SchoolName = @SchoolName", checkParams));
            if (exists > 0) return 0;

            string query = @"INSERT INTO Schools (SchoolName, District, Town, Chiefdom, Address, Phone, Email, PrincipalName, LogoPath, LicenseKey, LicenseExpiryDate, IsActive, ResultType, CreatedDate, ModifiedDate) VALUES (@SchoolName, @District, @Town, @Chiefdom, @Address, @Phone, @Email, @PrincipalName, @LogoPath, @LicenseKey, @LicenseExpiryDate, @IsActive, @ResultType, @CreatedDate, @ModifiedDate); SELECT last_insert_rowid();";
            var parameters = new Dictionary<string, object>
            {
                {"@SchoolName", school.SchoolName ?? ""},
                {"@District", school.District ?? ""},
                {"@Town", school.Town ?? ""},
                {"@Chiefdom", school.Chiefdom ?? ""},
                {"@Address", school.Address ?? ""},
                {"@Phone", school.Phone ?? ""},
                {"@Email", school.Email ?? ""},
                {"@PrincipalName", school.PrincipalName ?? ""},
                {"@LogoPath", school.LogoPath},
                {"@LicenseKey", school.LicenseKey ?? ""},
                {"@LicenseExpiryDate", school.LicenseExpiryDate},
                {"@IsActive", school.IsActive},
                {"@ResultType", school.ResultType ?? "FiveTests"},
                {"@CreatedDate", DateTime.Now},
                {"@ModifiedDate", DateTime.Now}
            };
            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool IsDuplicateSubject(int schoolId, int? classId, string subjectName, string subjectCode, int? excludeId = null)
        {
            try
            {
                string normalizedName = (subjectName?.Trim() ?? "").ToLowerInvariant();
                string normalizedCode = (subjectCode?.Trim() ?? "").ToLowerInvariant();

                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@NameNormalized", normalizedName },
                    { "@CodeNormalized", normalizedCode }
                };

                string classCondition;
                if (classId.HasValue)
                {
                    classCondition = "AND (ClassId = @ClassId OR ClassId IS NULL)";
                    parameters.Add("@ClassId", classId.Value);
                }
                else
                {
                    classCondition = "AND ClassId IS NULL";
                }

                string excludeCondition = "";
                if (excludeId.HasValue)
                {
                    excludeCondition = "AND Id != @ExcludeId";
                    parameters.Add("@ExcludeId", excludeId.Value);
                }

                string query = $@"
                    SELECT COUNT(*) FROM Subjects 
                    WHERE SchoolId = @SchoolId 
                    {excludeCondition}
                    AND (
                        LOWER(TRIM(COALESCE(SubjectName, ''))) = @NameNormalized
                        OR (LOWER(TRIM(COALESCE(SubjectCode, ''))) = @CodeNormalized AND @CodeNormalized != '')
                    )
                    {classCondition}
                    AND (IsDeleted = 0 OR IsDeleted IS NULL)";

                int existsByNameOrCode = Convert.ToInt32(ExecuteScalar(query, parameters));

                if (existsByNameOrCode == 0 && !string.IsNullOrEmpty(normalizedCode))
                {
                    string codeQuery = $@"
                        SELECT COUNT(*) FROM Subjects 
                        WHERE SchoolId = @SchoolId 
                        {excludeCondition}
                        AND LOWER(TRIM(COALESCE(SubjectCode, ''))) = @NameNormalized
                        {classCondition}
                        AND (IsDeleted = 0 OR IsDeleted IS NULL)";

                    existsByNameOrCode = Convert.ToInt32(ExecuteScalar(codeQuery, parameters));
                }

                return existsByNameOrCode > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IsDuplicateSubject error: {ex.Message}");
                return false;
            }
        }

        public bool UpdateSchool(School school)
        {
            string query = @"UPDATE Schools SET SchoolName = @SchoolName, District = @District, Town = @Town, Chiefdom = @Chiefdom, Address = @Address, Phone = @Phone, Email = @Email, PrincipalName = @PrincipalName, LogoPath = @LogoPath, LicenseKey = @LicenseKey, LicenseExpiryDate = @LicenseExpiryDate, IsActive = @IsActive, ResultType = @ResultType, ModifiedDate = @ModifiedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                {"@Id", school.Id},
                {"@SchoolName", school.SchoolName ?? ""},
                {"@District", school.District ?? ""},
                {"@Town", school.Town ?? ""},
                {"@Chiefdom", school.Chiefdom ?? ""},
                {"@Address", school.Address ?? ""},
                {"@Phone", school.Phone ?? ""},
                {"@Email", school.Email ?? ""},
                {"@PrincipalName", school.PrincipalName ?? ""},
                {"@LogoPath", school.LogoPath},
                {"@LicenseKey", school.LicenseKey ?? ""},
                {"@LicenseExpiryDate", school.LicenseExpiryDate},
                {"@IsActive", school.IsActive},
                {"@ResultType", school.ResultType ?? "FiveTests"},
                {"@ModifiedDate", DateTime.Now}
            };
            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool UpdateSchoolResultType(int schoolId, string resultType)
        {
            if (schoolId <= 0) return false;
            if (string.IsNullOrWhiteSpace(resultType)) return false;

            string newType = resultType.Trim();
            if (!string.Equals(newType, "FiveTests", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(newType, "SixTests", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"UpdateSchoolResultType: unknown result type '{resultType}'");
                return false;
            }

            newType = string.Equals(newType, "SixTests", StringComparison.OrdinalIgnoreCase)
                ? "SixTests"
                : "FiveTests";

            try
            {
                string oldType = "";
                try
                {
                    object current = ExecuteScalar(
                        "SELECT ResultType FROM Schools WHERE Id = @Id",
                        new Dictionary<string, object> { { "@Id", schoolId } });

                    if (current != null && current != DBNull.Value)
                        oldType = current.ToString();
                }
                catch { }

                if (string.Equals(oldType, newType, StringComparison.OrdinalIgnoreCase))
                    return true;

                int affected = ExecuteNonQuery(
                    "UPDATE Schools SET ResultType = @ResultType, ModifiedDate = @ModifiedDate WHERE Id = @Id",
                    new Dictionary<string, object>
                    {
                { "@Id", schoolId },
                { "@ResultType", newType },
                { "@ModifiedDate", DateTime.Now }
                    });

                if (affected <= 0) return false;

                try
                {
                    ExecuteNonQuery(
                        "UPDATE SchoolReportConfig SET ResultType = @ResultType, ModifiedDate = @ModifiedDate WHERE SchoolId = @SchoolId",
                        new Dictionary<string, object>
                        {
                    { "@SchoolId", schoolId },
                    { "@ResultType", newType },
                    { "@ModifiedDate", DateTime.Now }
                        });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UpdateSchoolResultType (config sync) warning: {ex.Message}");
                }

                try
                {
                    SchoolResultTypeChanged?.Invoke(null, new SchoolResultTypeChangedEventArgs
                    {
                        SchoolId = schoolId,
                        OldResultType = oldType,
                        NewResultType = newType
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SchoolResultTypeChanged handler error: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateSchoolResultType error: {ex.Message}");
                return false;
            }
        }

        public static void NotifySchoolResultTypeChanged(int schoolId, string oldType, string newType)
        {
            try
            {
                SchoolResultTypeChanged?.Invoke(null, new SchoolResultTypeChangedEventArgs
                {
                    SchoolId = schoolId,
                    OldResultType = oldType,
                    NewResultType = newType
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NotifySchoolResultTypeChanged error: {ex.Message}");
            }
        }
        public int InsertOrUpdateSchool(School school)
        {
            if (school == null) return 0;
            var checkParams = new Dictionary<string, object>
            {
                { "@SchoolName", school.SchoolName }, { "@District", school.District }, { "@Town", school.Town }
            };
            object existing = ExecuteScalar("SELECT Id FROM Schools WHERE SchoolName = @SchoolName AND District = @District AND Town = @Town", checkParams);
            if (existing != null)
            {
                int id = Convert.ToInt32(existing);
                if (string.IsNullOrWhiteSpace(school.ResultType)) school.ResultType = "FiveTests";
                var updateParams = new Dictionary<string, object>
                {
                    { "@Id", id }, { "@SchoolName", school.SchoolName }, { "@District", school.District }, { "@Town", school.Town },
                    { "@PrincipalName", school.PrincipalName ?? "" }, { "@LogoPath", school.LogoPath ?? "" },
                    { "@LicenseKey", school.LicenseKey ?? "" }, { "@LicenseExpiryDate", school.LicenseExpiryDate }, { "@ResultType", school.ResultType ?? "FiveTests" }, { "@ModifiedDate", DateTime.Now }
                };
                ExecuteNonQuery(@"UPDATE Schools SET SchoolName = @SchoolName, District = @District, Town = @Town, PrincipalName = @PrincipalName, LogoPath = @LogoPath, LicenseKey = @LicenseKey, LicenseExpiryDate = @LicenseExpiryDate, ResultType = @ResultType, ModifiedDate = @ModifiedDate WHERE Id = @Id", updateParams);
                return id;
            }

            string insertQuery = @"INSERT INTO Schools (SchoolName, District, Town, Chiefdom, Address, Phone, Email, PrincipalName, LogoPath, LicenseKey, LicenseExpiryDate, IsActive, ResultType, CreatedDate, ModifiedDate) VALUES (@SchoolName, @District, @Town, @Chiefdom, @Address, @Phone, @Email, @PrincipalName, @LogoPath, @LicenseKey, @LicenseExpiryDate, @IsActive, @ResultType, @CreatedDate, @ModifiedDate); SELECT last_insert_rowid();";
            if (string.IsNullOrWhiteSpace(school.ResultType)) school.ResultType = "FiveTests";
            var parameters = new Dictionary<string, object>
            {
                { "@SchoolName", school.SchoolName }, { "@District", school.District }, { "@Town", school.Town },
                { "@Chiefdom", school.Chiefdom ?? "" }, { "@Address", school.Address ?? "" }, { "@Phone", school.Phone ?? "" }, { "@Email", school.Email ?? "" },
                { "@PrincipalName", school.PrincipalName ?? "" }, { "@LogoPath", school.LogoPath ?? "" }, { "@LicenseKey", school.LicenseKey ?? "" }, { "@LicenseExpiryDate", school.LicenseExpiryDate },
                { "@IsActive", school.IsActive }, { "@ResultType", school.ResultType ?? "FiveTests" }, { "@CreatedDate", DateTime.Now }, { "@ModifiedDate", DateTime.Now }
            };
            object res = ExecuteScalar(insertQuery, parameters);
            return Convert.ToInt32(res);
        }

        public int GetSchoolIdByLicenseKey(string licenseKey)
        {
            if (string.IsNullOrEmpty(licenseKey)) return 0;
            var parameters = new Dictionary<string, object> { { "@LicenseKey", licenseKey } };
            object obj = ExecuteScalar("SELECT Id FROM Schools WHERE LicenseKey = @LicenseKey", parameters);
            if (obj == null) return 0;
            return Convert.ToInt32(obj);
        }

        #endregion

        #region Parent Code Methods

        public List<ParentCode> GetParentCodes(int schoolId)
        {
            var list = new List<ParentCode>();
            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };

                DataTable dt = ExecuteQuery(@"
            SELECT pc.*, 
                   s.FullName AS StudentName, 
                   s.AdmissionNumber AS AdmissionNumber,
                   c.ClassName AS ClassName
            FROM ParentCodes pc
            LEFT JOIN Students s ON pc.StudentId = s.Id
            LEFT JOIN Classes  c ON s.ClassId = c.Id
            WHERE pc.SchoolId = @SchoolId
            ORDER BY pc.CreatedDate DESC", parameters);

                foreach (DataRow row in dt.Rows)
                    list.Add(MapParentCodeRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetParentCodes error: {ex.Message}");
            }
            return list;
        }

        public int SaveParentCode(ParentCode code)
        {
            if (code == null) return 0;

            try
            {
                var checkParams = new Dictionary<string, object> { { "@AccessCode", code.AccessCode ?? "" } };
                object existingId = ExecuteScalar("SELECT Id FROM ParentCodes WHERE AccessCode = @AccessCode", checkParams);
                if (existingId != null && existingId != DBNull.Value)
                    return 0;

                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", code.SchoolId },
                    { "@StudentId", code.StudentId },
                    { "@AccessCode", code.AccessCode ?? "" },
                    { "@ParentName", code.ParentName ?? "" },
                    { "@ParentPhone", code.ParentPhone ?? "" },
                    { "@ParentEmail", code.ParentEmail ?? "" },
                    { "@CreatedDate", code.CreatedDate == default ? DateTime.Now : code.CreatedDate },
                    { "@ExpiryDate", (object)code.ExpiryDate ?? DBNull.Value },
                    { "@SubscriptionExpiryDate", (object)code.SubscriptionExpiryDate ?? DBNull.Value },
                    { "@UsageCount", code.UsageCount },
                    { "@MaxUsage", code.MaxUsage },
                    { "@IsActive", code.IsActive ? 1 : 0 }
                };

                string query = @"INSERT INTO ParentCodes
                    (SchoolId, StudentId, AccessCode, ParentName, ParentPhone, ParentEmail,
                     CreatedDate, ExpiryDate, SubscriptionExpiryDate, UsageCount, MaxUsage, IsActive)
                    VALUES
                    (@SchoolId, @StudentId, @AccessCode, @ParentName, @ParentPhone, @ParentEmail,
                     @CreatedDate, @ExpiryDate, @SubscriptionExpiryDate, @UsageCount, @MaxUsage, @IsActive);
                    SELECT last_insert_rowid();";

                object result = ExecuteScalar(query, parameters);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveParentCode error: {ex.Message}");
                return 0;
            }
        }

        public bool DeleteParentCode(int parentCodeId)
        {
            try
            {
                if (parentCodeId <= 0) return false;
                var parameters = new Dictionary<string, object> { { "@Id", parentCodeId } };
                return ExecuteNonQuery("DELETE FROM ParentCodes WHERE Id = @Id", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteParentCode error: {ex.Message}");
                return false;
            }
        }

        public bool TerminateParentCode(int parentCodeId)
        {
            try
            {
                if (parentCodeId <= 0) return false;

                var parameters = new Dictionary<string, object>
                {
                    { "@Id", parentCodeId },
                    { "@Now", DateTime.Now }
                };

                return ExecuteNonQuery(
                    "UPDATE ParentCodes SET IsActive = 0, SubscriptionExpiryDate = @Now, ExpiryDate = @Now WHERE Id = @Id",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TerminateParentCode error: {ex.Message}");
                return false;
            }
        }

        private ParentCode MapParentCodeRow(DataRow row)
        {
            return new ParentCode
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                StudentId = ConvertToInt(row["StudentId"]),
                AccessCode = ConvertToString(row["AccessCode"]),
                ParentName = ConvertToString(row["ParentName"]),
                ParentPhone = ConvertToString(row["ParentPhone"]),
                ParentEmail = ConvertToString(row["ParentEmail"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ExpiryDate = row["ExpiryDate"] == DBNull.Value ? (DateTime?)null : ConvertToDateTime(row["ExpiryDate"]),
                SubscriptionExpiryDate = row["SubscriptionExpiryDate"] == DBNull.Value
                                            ? DateTime.MinValue
                                            : ConvertToDateTime(row["SubscriptionExpiryDate"]),
                UsageCount = ConvertToInt(row["UsageCount"]),
                MaxUsage = ConvertToInt(row["MaxUsage"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                StudentName = row.Table.Columns.Contains("StudentName")
                                ? ConvertToString(row["StudentName"]) : "",
                AdmissionNumber = row.Table.Columns.Contains("AdmissionNumber")
                                ? ConvertToString(row["AdmissionNumber"]) : "",
                ClassName = row.Table.Columns.Contains("ClassName")
                                ? ConvertToString(row["ClassName"]) : ""
            };
        }

        #endregion

        #region Teacher Access Methods

        public List<TeacherAccessRow> GetTeacherAccess(int schoolId)
        {
            var list = new List<TeacherAccessRow>();
            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };

                DataTable dt = ExecuteQuery(@"
                    SELECT ta.Id,
                           ta.TeacherId,
                           ta.AccessLevel,
                           ta.IsActive,
                           ta.AssignedDate,
                           u.FullName AS TeacherName,
                           u.Username,
                           u.Phone,
                           c.ClassName,
                           s.SubjectName
                    FROM TeacherAccess ta
                    INNER JOIN Users u ON ta.TeacherId = u.Id
                    LEFT JOIN Classes c ON ta.ClassId = c.Id
                    LEFT JOIN Subjects s ON ta.SubjectId = s.Id
                    WHERE u.SchoolId = @SchoolId
                      AND u.Role = 'Teacher'
                    ORDER BY u.FullName, c.ClassName, s.SubjectName", parameters);

                foreach (DataRow row in dt.Rows)
                {
                    list.Add(new TeacherAccessRow
                    {
                        Id = ConvertToInt(row["Id"]),
                        TeacherId = ConvertToInt(row["TeacherId"]),
                        TeacherName = ConvertToString(row["TeacherName"]),
                        Username = ConvertToString(row["Username"]),
                        Phone = ConvertToString(row["Phone"]),
                        ClassName = row["ClassName"] == DBNull.Value ? "—" : ConvertToString(row["ClassName"]),
                        SubjectName = row["SubjectName"] == DBNull.Value ? "—" : ConvertToString(row["SubjectName"]),
                        AccessLevel = row["AccessLevel"] == DBNull.Value ? "Limited" : ConvertToString(row["AccessLevel"]),
                        IsActive = ConvertToBoolean(row["IsActive"]),
                        AssignedDate = ConvertToDateTime(row["AssignedDate"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTeacherAccess error: {ex.Message}");
            }
            return list;
        }

        public List<User> GetTeachersBySchool(int schoolId)
        {
            var teachers = new List<User>();
            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM Users WHERE SchoolId = @SchoolId AND Role = 'Teacher' AND (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY FullName",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    teachers.Add(new User
                    {
                        Id = ConvertToInt(row["Id"]),
                        SchoolId = ConvertToNullableInt(row["SchoolId"]),
                        Username = ConvertToString(row["Username"]),
                        FullName = ConvertToString(row["FullName"]),
                        Role = ConvertToString(row["Role"]),
                        Phone = ConvertToString(row["Phone"]),
                        Email = ConvertToString(row["Email"]),
                        IsActive = ConvertToBoolean(row["IsActive"]),
                        CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                        ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTeachersBySchool error: {ex.Message}");
            }
            return teachers;
        }

        #endregion

        #region Class Methods

        public List<Class> GetClasses(int schoolId)
        {
            var classes = new List<Class>();
            var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Classes WHERE SchoolId = @SchoolId ORDER BY ClassName", parameters);
            foreach (DataRow row in dt.Rows)
            {
                classes.Add(new Class
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolId = ConvertToInt(row["SchoolId"]),
                    ClassName = ConvertToString(row["ClassName"]),
                    ClassLevel = ConvertToString(row["ClassLevel"]),
                    Stream = ConvertToString(row["Stream"]),
                    AcademicYear = ConvertToString(row["AcademicYear"]),
                    ClassTeacherName = ConvertToString(row["ClassTeacherName"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),
                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"]),
                    ResultTypeOverride = row.Table.Columns.Contains("ResultTypeOverride")
                        ? ConvertToString(row["ResultTypeOverride"])
                        : null
                });
            }
            return classes;
        }

        public Class GetClassById(int classId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", classId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Classes WHERE Id = @Id", parameters);
            if (dt.Rows.Count == 0) return null;
            DataRow row = dt.Rows[0];
            return new Class
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ClassName = ConvertToString(row["ClassName"]),
                ClassLevel = ConvertToString(row["ClassLevel"]),
                Stream = ConvertToString(row["Stream"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                ClassTeacherName = ConvertToString(row["ClassTeacherName"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"]),
                ResultTypeOverride = row.Table.Columns.Contains("ResultTypeOverride")
        ? ConvertToString(row["ResultTypeOverride"])
        : null
            };
        }

        public int InsertClass(Class cls)
        {
            string query = @"INSERT INTO Classes (SchoolId, ClassName, ClassLevel, Stream, AcademicYear, ClassTeacherName, IsActive, ResultTypeOverride, CreatedDate, ModifiedDate) VALUES (@SchoolId, @ClassName, @ClassLevel, @Stream, @AcademicYear, @ClassTeacherName, @IsActive, @ResultTypeOverride, @CreatedDate, @ModifiedDate); SELECT last_insert_rowid();";
            var parameters = new Dictionary<string, object>
    {
        {"@SchoolId", cls.SchoolId},
        {"@ClassName", cls.ClassName},
        {"@ClassLevel", cls.ClassLevel},
        {"@Stream", cls.Stream},
        {"@AcademicYear", cls.AcademicYear},
        {"@ClassTeacherName", cls.ClassTeacherName ?? ""},
        {"@IsActive", cls.IsActive},
        {"@ResultTypeOverride", (object)cls.ResultTypeOverride ?? DBNull.Value},
        {"@CreatedDate", DateTime.Now},
        {"@ModifiedDate", DateTime.Now}
    };
            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool UpdateClass(Class cls)
        {
            string query = @"UPDATE Classes SET ClassName = @ClassName, ClassLevel = @ClassLevel, Stream = @Stream, AcademicYear = @AcademicYear, ClassTeacherName = @ClassTeacherName, IsActive = @IsActive, ResultTypeOverride = @ResultTypeOverride, ModifiedDate = @ModifiedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
    {
        {"@Id", cls.Id},
        {"@ClassName", cls.ClassName},
        {"@ClassLevel", cls.ClassLevel},
        {"@Stream", cls.Stream},
        {"@AcademicYear", cls.AcademicYear},
        {"@ClassTeacherName", cls.ClassTeacherName ?? ""},
        {"@IsActive", cls.IsActive},
        {"@ResultTypeOverride", (object)cls.ResultTypeOverride ?? DBNull.Value},
        {"@ModifiedDate", DateTime.Now}
    };
            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool DeleteClass(int classId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@Id", classId } };
                return ExecuteNonQuery("DELETE FROM Classes WHERE Id = @Id", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteClass error: {ex.Message}");
                return false;
            }
        }

        public bool ReassignClassIds()
        {
            SqliteConnection conn = null;
            try
            {
                conn = GetConnection();
                using (var tran = conn.BeginTransaction())
                {
                    ExecuteNonQuery("PRAGMA foreign_keys = OFF;", null, conn);
                    ExecuteNonQuery("CREATE TEMP TABLE IF NOT EXISTS temp_ClassIdMap(oldId INTEGER, newId INTEGER);", null, conn);
                    ExecuteNonQuery("DELETE FROM temp_ClassIdMap;", null, conn);

                    var dt = ExecuteQuery("SELECT Id FROM Classes ORDER BY Id", null, conn);
                    int seq = 1;
                    foreach (DataRow row in dt.Rows)
                    {
                        int oldId = Convert.ToInt32(row["Id"]);
                        var p = new Dictionary<string, object> { { "@oldId", oldId }, { "@newId", seq } };
                        ExecuteNonQuery("INSERT INTO temp_ClassIdMap(oldId,newId) VALUES(@oldId,@newId);", p, conn);
                        seq++;
                    }

                    ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS Classes_new (
                        Id INTEGER PRIMARY KEY,
                        SchoolId INTEGER NOT NULL,
                        ClassName TEXT NOT NULL,
                        ClassLevel TEXT NOT NULL,
                        Stream TEXT,
                        AcademicYear TEXT NOT NULL,
                        ClassTeacherName TEXT,
                        IsActive BOOLEAN DEFAULT 1,
                        ResultTypeOverride TEXT,
                        IsDeleted BOOLEAN DEFAULT 0,
                        DeletedDate DATETIME,
                        DeletedBy TEXT,
                        CreatedDate DATETIME,
                        ModifiedDate DATETIME
                    );", null, conn);

                    ExecuteNonQuery(@"INSERT INTO Classes_new (Id, SchoolId, ClassName, ClassLevel, Stream, AcademicYear,
                        ClassTeacherName, IsActive, ResultTypeOverride, IsDeleted, DeletedDate, DeletedBy, CreatedDate, ModifiedDate)
                        SELECT m.newId, c.SchoolId, c.ClassName, c.ClassLevel, c.Stream, c.AcademicYear,
                        c.ClassTeacherName, c.IsActive, c.ResultTypeOverride, c.IsDeleted, c.DeletedDate, c.DeletedBy, c.CreatedDate, c.ModifiedDate
                         FROM Classes c JOIN temp_ClassIdMap m ON c.Id = m.oldId ORDER BY m.newId;", null, conn);

                    ExecuteNonQuery(@"
                          UPDATE Students
                          SET ClassId = (SELECT newId FROM temp_ClassIdMap WHERE oldId = Students.ClassId)
                          WHERE ClassId IS NOT NULL
                          AND EXISTS (SELECT 1 FROM temp_ClassIdMap WHERE oldId = Students.ClassId);",
                          null, conn);

                    ExecuteNonQuery(@"
                          UPDATE Subjects
                          SET ClassId = (SELECT newId FROM temp_ClassIdMap WHERE oldId = Subjects.ClassId)
                          WHERE ClassId IS NOT NULL
                          AND EXISTS (SELECT 1 FROM temp_ClassIdMap WHERE oldId = Subjects.ClassId);",
                        null, conn);

                    ExecuteNonQuery("DROP TABLE IF EXISTS Classes;", null, conn);
                    ExecuteNonQuery("ALTER TABLE Classes_new RENAME TO Classes;", null, conn);

                    try
                    {
                        ExecuteNonQuery(@"DELETE FROM sqlite_sequence WHERE name='Classes';", null, conn);
                        ExecuteNonQuery(@"INSERT OR REPLACE INTO sqlite_sequence(name, seq) VALUES('Classes', (SELECT IFNULL(MAX(Id),0) FROM Classes));", null, conn);
                    }
                    catch { }

                    ExecuteNonQuery("DELETE FROM temp_ClassIdMap;", null, conn);
                    ExecuteNonQuery("PRAGMA foreign_keys = ON;", null, conn);
                    tran.Commit();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReassignClassIds error: {ex.Message}");
                try { ExecuteNonQuery("PRAGMA foreign_keys = ON;", null, conn); } catch { }
                return false;
            }
            finally
            {
                if (conn != null) CloseConnection(conn);
            }
        }

        #endregion

        #region Subject Methods

        public List<Subject> GetSubjects(int schoolId, int? classId = null)
        {
            var subjects = new List<Subject>();
            string query;
            Dictionary<string, object> parameters;

            if (classId.HasValue)
            {
                query = @"SELECT * FROM Subjects WHERE SchoolId = @SchoolId AND (ClassId = @ClassId OR ClassId IS NULL) ORDER BY SubjectName";
                parameters = new Dictionary<string, object> { { "@SchoolId", schoolId }, { "@ClassId", classId.Value } };
            }
            else
            {
                query = "SELECT * FROM Subjects WHERE SchoolId = @SchoolId ORDER BY SubjectName";
                parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            }

            DataTable dt = ExecuteQuery(query, parameters);
            foreach (DataRow row in dt.Rows)
            {
                subjects.Add(new Subject
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolId = ConvertToInt(row["SchoolId"]),
                    SubjectCode = ConvertToString(row["SubjectCode"]),
                    SubjectName = ConvertToString(row["SubjectName"]),
                    AcademicYear = row.Table.Columns.Contains("AcademicYear") ? ConvertToString(row["AcademicYear"]) : null,
                    IsCore = ConvertToBoolean(row["IsCore"]),
                    ClassId = ConvertToNullableInt(row["ClassId"]),
                    PassMark = ConvertToDecimal(row["PassMark"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),

                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                });
            }
            return subjects;
        }

        public int InsertSubject(Subject subject)
        {
            var checkParams = new Dictionary<string, object>
            {
                { "@SchoolId", subject.SchoolId },
                { "@SubjectName", subject.SubjectName?.Trim() ?? "" },
                { "@SubjectCode", subject.SubjectCode?.Trim() ?? "" },
                { "@ClassId", subject.ClassId }
            };

            string checkQuery = @"SELECT COUNT(*) FROM Subjects WHERE SchoolId = @SchoolId
                AND (
                    LOWER(TRIM(SubjectName)) = LOWER(TRIM(@SubjectName))
                    OR (@SubjectCode <> '' AND LOWER(TRIM(SubjectCode)) = LOWER(TRIM(@SubjectCode)))
                )
                AND ((ClassId = @ClassId) OR (ClassId IS NULL AND @ClassId IS NULL))";

            int exists = Convert.ToInt32(ExecuteScalar(checkQuery, checkParams));
            if (exists > 0) return 0;

            string query = @"INSERT INTO Subjects (SchoolId, SubjectCode, SubjectName, IsCore, ClassId, AcademicYear, PassMark, IsActive, CreatedDate, ModifiedDate) VALUES (@SchoolId, @SubjectCode, @SubjectName, @IsCore, @ClassId, @AcademicYear, @PassMark, @IsActive, @CreatedDate, @ModifiedDate); SELECT last_insert_rowid();";
            var parameters = new Dictionary<string, object>
            {
                {"@SchoolId", subject.SchoolId},
                {"@SubjectCode", subject.SubjectCode ?? ""},
                {"@SubjectName", subject.SubjectName},
                {"@IsCore", subject.IsCore},
                {"@ClassId", subject.ClassId},
                {"@AcademicYear", (object)subject.AcademicYear ?? DBNull.Value},
                {"@PassMark", subject.PassMark},
                {"@IsActive", subject.IsActive},
                {"@CreatedDate", DateTime.Now},
                {"@ModifiedDate", DateTime.Now}
            };
            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool UpdateSubject(Subject subject)
        {
            var checkParams = new Dictionary<string, object>
            {
                { "@SchoolId", subject.SchoolId },
                { "@SubjectName", subject.SubjectName?.Trim() ?? "" },
                { "@SubjectCode", subject.SubjectCode?.Trim() ?? "" },
                { "@ClassId", subject.ClassId },
                { "@Id", subject.Id }
            };

            string checkQuery = @"SELECT COUNT(*) FROM Subjects WHERE SchoolId = @SchoolId AND Id != @Id
                AND (
                    LOWER(TRIM(SubjectName)) = LOWER(TRIM(@SubjectName))
                    OR (@SubjectCode <> '' AND LOWER(TRIM(SubjectCode)) = LOWER(TRIM(@SubjectCode)))
                )
                AND ((ClassId = @ClassId) OR (ClassId IS NULL AND @ClassId IS NULL))";

            int exists = Convert.ToInt32(ExecuteScalar(checkQuery, checkParams));
            if (exists > 0) return false;

            string query = @"UPDATE Subjects SET SubjectCode = @SubjectCode, SubjectName = @SubjectName, IsCore = @IsCore, ClassId = @ClassId, AcademicYear = @AcademicYear, PassMark = @PassMark, IsActive = @IsActive, ModifiedDate = @ModifiedDate WHERE Id = @Id";
            var parameters = new Dictionary<string, object>
            {
                {"@Id", subject.Id},
                {"@SubjectCode", subject.SubjectCode ?? ""},
                {"@SubjectName", subject.SubjectName},
                {"@IsCore", subject.IsCore},
                {"@ClassId", subject.ClassId},
                {"@AcademicYear", (object)subject.AcademicYear ?? DBNull.Value},
                {"@PassMark", subject.PassMark},
                {"@IsActive", subject.IsActive},
                {"@ModifiedDate", DateTime.Now}
            };
            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool DeleteSubject(int subjectId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", subjectId } };
            return ExecuteNonQuery("DELETE FROM Subjects WHERE Id = @Id", parameters) > 0;
        }

        public (int Inserted, List<int> SkippedClassIds) InsertSubjectIntoClasses(
    Subject template, IEnumerable<int> classIds)
        {
            var result = (Inserted: 0, Skipped: new List<int>());
            if (template == null || classIds == null) return result;

            var ids = classIds.Distinct().ToList();
            if (ids.Count == 0) return result;

            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();

                var toInsert = new List<int>();
                foreach (var cid in ids)
                {
                    if (IsDuplicateSubject(template.SchoolId, cid,
                                           template.SubjectName, template.SubjectCode))
                    {
                        result.Skipped.Add(cid);
                    }
                    else
                    {
                        toInsert.Add(cid);
                    }
                }

                if (toInsert.Count == 0) return result;

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var cid in toInsert)
                        {
                            var parameters = new Dictionary<string, object>
                    {
                        { "@SchoolId", template.SchoolId },
                        { "@SubjectCode", template.SubjectCode ?? "" },
                        { "@SubjectName", template.SubjectName },
                        { "@IsCore", template.IsCore },
                        { "@ClassId", cid },
                        { "@AcademicYear", (object)template.AcademicYear ?? DBNull.Value },
                        { "@PassMark", template.PassMark },
                        { "@IsActive", template.IsActive },
                        { "@CreatedDate", DateTime.Now },
                        { "@ModifiedDate", DateTime.Now }
                    };

                            ExecuteNonQuery(@"
                        INSERT INTO Subjects
                            (SchoolId, SubjectCode, SubjectName, IsCore, ClassId,
                             AcademicYear, PassMark, IsActive, CreatedDate, ModifiedDate)
                        VALUES
                            (@SchoolId, @SubjectCode, @SubjectName, @IsCore, @ClassId,
                             @AcademicYear, @PassMark, @IsActive, @CreatedDate, @ModifiedDate)",
                                parameters, connection);

                            result.Inserted++;
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertSubjectIntoClasses error: {ex.Message}");
                return (0, new List<int>());
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }

            return result;
        }

        public bool DeleteSubjectsBulk(IEnumerable<int> subjectIds)
        {
            var ids = subjectIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return false;

            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var id in ids)
                        {
                            ExecuteNonQuery(
                                "DELETE FROM Subjects WHERE Id = @Id",
                                new Dictionary<string, object> { { "@Id", id } },
                                connection);
                        }
                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteSubjectsBulk error: {ex.Message}");
                return false;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }

        #endregion

        #region Student Methods

        public List<Student> GetStudents(int schoolId, int classId)
        {
            var students = new List<Student>();
            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", schoolId },
                { "@ClassId", classId }
            };

            DataTable dt = ExecuteQuery(
                "SELECT * FROM Students WHERE SchoolId = @SchoolId AND ClassId = @ClassId AND (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY FullName",
                parameters);

            foreach (DataRow row in dt.Rows)
                students.Add(MapStudentRow(row));

            return students;
        }

        public Student GetStudentById(int studentId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", studentId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Students WHERE Id = @Id", parameters);
            if (dt.Rows.Count == 0) return null;
            return MapStudentRow(dt.Rows[0]);
        }

        public string GetStudentDisplayLabel(int studentId)
        {
            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT s.FullName, c.ClassName, c.Stream
            FROM Students s
            LEFT JOIN Classes c ON s.ClassId = c.Id
            WHERE s.Id = @Id
            LIMIT 1",
                    new Dictionary<string, object> { { "@Id", studentId } });

                if (dt.Rows.Count == 0) return null;

                string name = dt.Rows[0]["FullName"]?.ToString() ?? "Unknown";
                string className = dt.Rows[0]["ClassName"]?.ToString() ?? "Unknown Class";
                string stream = dt.Rows[0]["Stream"]?.ToString() ?? "";

                string classLabel = string.IsNullOrWhiteSpace(stream)
                    ? className
                    : $"{className} {stream}".Trim();

                return $"{name} � {classLabel}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentDisplayLabel error: {ex.Message}");
                return null;
            }
        }

        public bool HardDeleteRecord(string tableName, int id, string deletedBy = "System")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tableName) || id <= 0) return false;
                if (!Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$")) return false;

                string query = $"DELETE FROM {tableName} WHERE Id = @Id";
                var parameters = new Dictionary<string, object> { { "@Id", id } };
                return ExecuteNonQuery(query, parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HardDeleteRecord error: {ex.Message}");
                return false;
            }
        }


        public Student GetStudentByAdmissionNumber(string admissionNumber)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@AdmissionNumber", admissionNumber } };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM Students WHERE AdmissionNumber = @AdmissionNumber AND (IsDeleted = 0 OR IsDeleted IS NULL) LIMIT 1",
                    parameters);
                if (dt.Rows.Count == 0) return null;
                return MapStudentRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentByAdmissionNumber error: {ex.Message}");
                return null;
            }
        }

        public int InsertStudent(Student student)
        {
            var checkParams = new Dictionary<string, object> { { "@AdmissionNumber", student.AdmissionNumber } };
            int exists = Convert.ToInt32(ExecuteScalar(
                "SELECT COUNT(*) FROM Students WHERE AdmissionNumber = @AdmissionNumber",
                checkParams));
            if (exists > 0) return 0;

            string query = @"INSERT INTO Students
                (SchoolId, ClassId, AdmissionNumber, FullName, DateOfBirth, Gender,
                 Address, ParentName, ParentPhone, HouseColour, Stream, PhotoPath,
                 IsActive, CreatedDate, ModifiedDate)
                VALUES
                (@SchoolId, @ClassId, @AdmissionNumber, @FullName, @DateOfBirth, @Gender,
                 @Address, @ParentName, @ParentPhone, @HouseColour, @Stream, @PhotoPath,
                 @IsActive, @CreatedDate, @ModifiedDate);
                SELECT last_insert_rowid();";

            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", student.SchoolId },
                { "@ClassId", student.ClassId },
                { "@AdmissionNumber", student.AdmissionNumber },
                { "@FullName", student.FullName },
                { "@DateOfBirth", student.DateOfBirth },
                { "@Gender", student.Gender },
                { "@Address", student.Address ?? "" },
                { "@ParentName", student.ParentName ?? "" },
                { "@ParentPhone", student.ParentPhone ?? "" },
                { "@HouseColour", student.HouseColour ?? "" },
                { "@Stream", student.Stream ?? "" },
                { "@PhotoPath", student.PhotoPath ?? "" },
                { "@IsActive", student.IsActive },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
            };

            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public bool UpdateStudent(Student student)
        {
            string query = @"UPDATE Students SET
                AdmissionNumber = @AdmissionNumber,
                ClassId = @ClassId,
                FullName = @FullName,
                DateOfBirth = @DateOfBirth,
                Gender = @Gender,
                Address = @Address,
                ParentName = @ParentName,
                ParentPhone = @ParentPhone,
                HouseColour = @HouseColour,
                Stream = @Stream,
                PhotoPath = @PhotoPath,
                IsActive = @IsActive,
                ModifiedDate = @ModifiedDate
                WHERE Id = @Id";

            var parameters = new Dictionary<string, object>
            {
                { "@Id", student.Id },
                { "@AdmissionNumber", student.AdmissionNumber ?? "" },
                { "@ClassId", student.ClassId },
                { "@FullName", student.FullName },
                { "@DateOfBirth", student.DateOfBirth },
                { "@Gender", student.Gender },
                { "@Address", student.Address ?? "" },
                { "@ParentName", student.ParentName ?? "" },
                { "@ParentPhone", student.ParentPhone ?? "" },
                { "@HouseColour", student.HouseColour ?? "" },
                { "@Stream", student.Stream ?? "" },
                { "@PhotoPath", student.PhotoPath ?? "" },
                { "@IsActive", student.IsActive },
                { "@ModifiedDate", DateTime.Now }
            };

            return ExecuteNonQuery(query, parameters) > 0;
        }

        public bool DeleteStudent(int studentId)
        {
            var parameters = new Dictionary<string, object> { { "@Id", studentId } };
            return ExecuteNonQuery("DELETE FROM Students WHERE Id = @Id", parameters) > 0;
        }

        public int MoveStudentsToClass(
    List<int> studentIds,
    int newClassId,
    int? performedByUserId = null,
    string performedByName = null,
    string performedByRole = null,
    string academicYear = null)
        {
            if (studentIds == null || studentIds.Count == 0) return 0;

            int movedCount = 0;
            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string query = @"UPDATE Students SET
                    ClassId = @NewClassId,
                    ModifiedDate = @ModifiedDate
                    WHERE Id = @StudentId";

                        string auditQuery = @"INSERT INTO StudentMoveAudit
                    (StudentId, FromClassId, ToClassId, PerformedByUserId,
                     PerformedByName, PerformedByRole, AcademicYear, Details)
                    VALUES (@StudentId, @FromClassId, @ToClassId, @PerformedByUserId,
                     @PerformedByName, @PerformedByRole, @AcademicYear, @Details)";

                        foreach (int studentId in studentIds)
                        {
                            object currentClassObj = ExecuteScalar(
                                "SELECT ClassId FROM Students WHERE Id = @Id",
                                new Dictionary<string, object> { { "@Id", studentId } },
                                connection);

                            int? fromClassId = currentClassObj != null && currentClassObj != DBNull.Value
                                ? (int?)Convert.ToInt32(currentClassObj)
                                : null;

                            var parameters = new Dictionary<string, object>
                    {
                        { "@StudentId", studentId },
                        { "@NewClassId", newClassId },
                        { "@ModifiedDate", DateTime.Now }
                    };

                            int result = ExecuteNonQuery(query, parameters, connection);
                            if (result > 0)
                            {
                                movedCount++;

                                var auditParams = new Dictionary<string, object>
                        {
                            { "@StudentId", studentId },
                            { "@FromClassId", fromClassId },
                            { "@ToClassId", newClassId },
                            { "@PerformedByUserId", performedByUserId },
                            { "@PerformedByName", performedByName ?? "" },
                            { "@PerformedByRole", performedByRole ?? "" },
                            { "@AcademicYear", academicYear ?? "" },
                            { "@Details", $"Moved by {performedByName}" }
                        };

                                ExecuteNonQuery(auditQuery, auditParams, connection);
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }

                return movedCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MoveStudentsToClass error: {ex.Message}");
                return 0;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }

        public int GetStudentCount(int schoolId)
        {
            return Convert.ToInt32(ExecuteScalar(
                "SELECT COUNT(*) FROM Students WHERE SchoolId = @SchoolId AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new Dictionary<string, object> { { "@SchoolId", schoolId } }));
        }
        public Dictionary<int, int> GetStudentCountsByClass(int schoolId)
        {
            var map = new Dictionary<int, int>();
            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT ClassId, COUNT(*) AS Cnt
            FROM Students
            WHERE SchoolId = @SchoolId
              AND (IsDeleted = 0 OR IsDeleted IS NULL)
              AND ClassId IS NOT NULL
            GROUP BY ClassId",
                    new Dictionary<string, object> { { "@SchoolId", schoolId } });

                foreach (DataRow row in dt.Rows)
                    map[Convert.ToInt32(row["ClassId"])] = Convert.ToInt32(row["Cnt"]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentCountsByClass error: {ex.Message}");
            }
            return map;
        }
        public List<Student> GetStudentsPaged(int schoolId, int classId, int page, int pageSize, out int totalCount)
        {
            totalCount = 0;
            var students = new List<Student>();
            try
            {
                int offset = (page - 1) * pageSize;

                string countQuery = "SELECT COUNT(*) FROM Students WHERE SchoolId = @SchoolId AND ClassId = @ClassId AND (IsDeleted = 0 OR IsDeleted IS NULL)";
                totalCount = Convert.ToInt32(ExecuteScalar(countQuery, new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@ClassId", classId }
                }));

                string dataQuery = @"SELECT * FROM Students
                    WHERE SchoolId = @SchoolId AND ClassId = @ClassId
                      AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    ORDER BY FullName
                    LIMIT @Limit OFFSET @Offset";

                var dt = ExecuteQuery(dataQuery, new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@ClassId", classId },
                    { "@Limit", pageSize },
                    { "@Offset", offset }
                });

                foreach (DataRow row in dt.Rows)
                    students.Add(MapStudentRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting paged students: {ex.Message}");
            }
            return students;
        }

        public DataTable SearchStudentsFast(int schoolId, string searchTerm, int page, int pageSize, out int totalCount)
        {
            totalCount = 0;
            try
            {
                int offset = (page - 1) * pageSize;
                string searchPattern = $"%{searchTerm}%";

                string countQuery = @"SELECT COUNT(*) FROM Students s
                    WHERE s.SchoolId = @SchoolId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND (s.AdmissionNumber LIKE @Search OR s.FullName LIKE @Search)";

                totalCount = Convert.ToInt32(ExecuteScalar(countQuery, new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@Search", searchPattern }
                }));

                string dataQuery = @"SELECT s.Id, s.AdmissionNumber, s.FullName, s.Gender,
                        s.ParentName, s.ParentPhone, c.ClassName, s.PhotoPath
                    FROM Students s
                    JOIN Classes c ON s.ClassId = c.Id
                    WHERE s.SchoolId = @SchoolId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND (s.AdmissionNumber LIKE @Search OR s.FullName LIKE @Search)
                    ORDER BY s.FullName
                    LIMIT @Limit OFFSET @Offset";

                return ExecuteQuery(dataQuery, new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@Search", searchPattern },
                    { "@Limit", pageSize },
                    { "@Offset", offset }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching students: {ex.Message}");
                return new DataTable();
            }
        }

        public List<Student> SearchStudentsSchoolWide(int schoolId, string searchTerm)
        {
            var students = new List<Student>();
            try
            {
                string searchPattern = $"%{searchTerm}%";

                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@SearchTerm", searchPattern }
                };

                string query = @"SELECT s.* FROM Students s
                    WHERE s.SchoolId = @SchoolId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND (s.AdmissionNumber LIKE @SearchTerm OR s.FullName LIKE @SearchTerm)
                    ORDER BY s.FullName";

                DataTable dt = ExecuteQuery(query, parameters);
                foreach (DataRow row in dt.Rows)
                    students.Add(MapStudentRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchStudentsSchoolWide error: {ex.Message}");
            }
            return students;
        }

        public string GetStudentStream(int studentId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@StudentId", studentId } };
                var result = ExecuteScalar("SELECT Stream FROM Students WHERE Id = @StudentId", parameters);
                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentStream error: {ex.Message}");
                return "";
            }
        }

        public List<Class> GetClassesByAcademicYear(int schoolId, string academicYear)
        {
            var classes = new List<Class>();
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@AcademicYear", (academicYear ?? "").Trim() }
                };

                DataTable dt = ExecuteQuery(
                    @"SELECT * FROM Classes
                      WHERE SchoolId = @SchoolId
                        AND (IsDeleted = 0 OR IsDeleted IS NULL)
                        AND TRIM(IFNULL(AcademicYear,'')) = @AcademicYear
                      ORDER BY ClassName",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    classes.Add(new Class
                    {
                        Id = ConvertToInt(row["Id"]),
                        SchoolId = ConvertToInt(row["SchoolId"]),
                        ClassName = ConvertToString(row["ClassName"]),
                        ClassLevel = ConvertToString(row["ClassLevel"]),
                        Stream = ConvertToString(row["Stream"]),
                        AcademicYear = ConvertToString(row["AcademicYear"]),
                        ClassTeacherName = ConvertToString(row["ClassTeacherName"]),
                        IsActive = ConvertToBoolean(row["IsActive"]),
                        CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                        ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetClassesByAcademicYear error: {ex.Message}");
            }
            return classes;
        }

        private string DetectPromotionTableName()
        {
            string[] candidates =
            {
                "PromotionSummary", "PromotionSummaries", "PromotionResults",
                "PromotionResult", "PromotionRecords", "PromotionRecord",
                "StudentPromotions", "StudentPromotion", "Promotions", "Promotion",
                "PromotionData", "PromotionResultSummary"
            };

            try
            {
                DataTable dt = ExecuteQuery(
                    "SELECT name FROM sqlite_master WHERE type='table'");

                var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow row in dt.Rows)
                {
                    if (row[0] != null) tables.Add(row[0].ToString());
                }

                foreach (var c in candidates)
                {
                    if (tables.Contains(c)) return c;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DetectPromotionTableName error: {ex.Message}");
            }
            return null;
        }

        private HashSet<string> GetTableColumns(string tableName)
        {
            return GetTableColumnNames(null, tableName);
        }

        private string FindColumn(string tableName, params string[] variants)
        {
            var cols = GetTableColumns(tableName);
            if (cols.Count == 0) return null;

            foreach (var v in variants)
            {
                foreach (var c in cols)
                {
                    if (string.Equals(c, v, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }

            foreach (var v in variants)
            {
                string norm = v.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                foreach (var c in cols)
                {
                    string cn = c.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                    if (cn.Contains(norm)) return c;
                }
            }
            return null;
        }

        public List<Student> GetStudentsPromotedToJSS3(int schoolId, string academicYear)
        {
            var results = new List<Student>();

            string year = (academicYear ?? "").Trim();
            if (schoolId <= 0 || year.Length == 0) return results;

            try
            {
                string promoTable = DetectPromotionTableName();
                if (!string.IsNullOrEmpty(promoTable))
                {
                    string nextClassCol = FindColumn(promoTable,
                        "NextClass", "Next_Class", "PromotedTo", "Promoted_To",
                        "NextClassLevel", "NextClassName", "PromotedClass");

                    string studentIdCol = FindColumn(promoTable,
                        "StudentId", "Student_Id");

                    string admCol = FindColumn(promoTable,
                        "AdmissionNumber", "Admission_Number",
                        "AdmNo", "Adm_No", "AdmissionNo");

                    string yearCol = FindColumn(promoTable,
                        "AcademicYear", "Academic_Year", "Year");

                    string statusCol = FindColumn(promoTable,
                        "Status", "PromotionStatus", "Promotion_Status", "Result");

                    if (nextClassCol != null && (studentIdCol != null || admCol != null))
                    {
                        string normalNextClass =
                            $"REPLACE(REPLACE(UPPER(IFNULL([{nextClassCol}],'')), ' ', ''), '.', '')";

                        string nextClassTest =
                            $"({normalNextClass} LIKE 'JSSIII%' OR " +
                            $" {normalNextClass} LIKE 'JSS3%'   OR " +
                            $" {normalNextClass} LIKE 'BASIC9%' OR " +
                            $" {normalNextClass} LIKE 'FORM3%')";

                        string statusTest = "";
                        if (statusCol != null)
                        {
                            string normStatus = $"UPPER(IFNULL([{statusCol}],''))";
                            statusTest =
                                $" AND ({normStatus} LIKE '%PROMOT%' OR {normStatus} LIKE '%PASS%')";
                        }

                        string subQuery;
                        if (studentIdCol != null)
                            subQuery = $"SELECT [{studentIdCol}] FROM [{promoTable}] WHERE {nextClassTest}{statusTest}";
                        else
                            subQuery = $"SELECT [{admCol}] FROM [{promoTable}] WHERE {nextClassTest}{statusTest}";

                        var parameters = new Dictionary<string, object>
                        {
                            { "@SchoolId", schoolId }
                        };
                        if (yearCol != null)
                        {
                            subQuery += $" AND TRIM(IFNULL([{yearCol}],'')) = @AcademicYear";
                            parameters["@AcademicYear"] = year;
                        }

                        string studentKeyCol =
                            studentIdCol != null ? "s.Id" : "s.AdmissionNumber";

                        string sql = $@"
                            SELECT s.* FROM Students s
                            WHERE s.SchoolId = @SchoolId
                              AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                              AND {studentKeyCol} IN ({subQuery})
                            ORDER BY s.FullName;";

                        DataTable dt = ExecuteQuery(sql, parameters);
                        foreach (DataRow row in dt.Rows)
                            results.Add(MapStudentRow(row));

                        if (results.Count > 0)
                        {
                            Debug.WriteLine(
                                $"BECE loader: {results.Count} students from promotion table '{promoTable}' for {year}.");
                            return results;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentsPromotedToJSS3 (promotion table) error: {ex.Message}");
            }

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@AcademicYear", year }
                };

                const string jss3Query = @"
                    SELECT s.* FROM Students s
                    INNER JOIN Classes c ON s.ClassId = c.Id
                    WHERE s.SchoolId = @SchoolId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
                      AND TRIM(IFNULL(c.AcademicYear,'')) = @AcademicYear
                      AND (
                            REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'JSSIII%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'JSS3%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'BASIC9%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'FORM3%'
                      )
                    ORDER BY c.ClassName, s.FullName";

                DataTable dt = ExecuteQuery(jss3Query, parameters);
                foreach (DataRow row in dt.Rows)
                    results.Add(MapStudentRow(row));

                if (results.Count > 0)
                {
                    Debug.WriteLine(
                        $"BECE loader: {results.Count} students from JSS III classes for {year}.");
                    return results;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentsPromotedToJSS3 (jss3 classes) error: {ex.Message}");
            }

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@AcademicYear", year }
                };

                const string jss2Query = @"
                    SELECT s.* FROM Students s
                    INNER JOIN Classes c ON s.ClassId = c.Id
                    WHERE s.SchoolId = @SchoolId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
                      AND TRIM(IFNULL(c.AcademicYear,'')) = @AcademicYear
                      AND (
                            REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'JSSII%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'JSS2%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'BASIC8%'
                         OR REPLACE(REPLACE(UPPER(IFNULL(c.ClassName,'')), ' ', ''), '.', '') LIKE 'FORM2%'
                      )
                    ORDER BY c.ClassName, s.FullName";

                DataTable dt = ExecuteQuery(jss2Query, parameters);
                foreach (DataRow row in dt.Rows)
                    results.Add(MapStudentRow(row));

                if (results.Count > 0)
                {
                    Debug.WriteLine(
                        $"BECE loader: {results.Count} students from JSS II classes for {year} (fallback).");
                    return results;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentsPromotedToJSS3 (jss2 classes) error: {ex.Message}");
            }

            try
            {
                return GetAllStudentsForAcademicYear(schoolId, year);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentsPromotedToJSS3 (all) error: {ex.Message}");
            }

            return results;
        }

        public List<Student> GetAllStudentsForAcademicYear(int schoolId, string academicYear)
        {
            var students = new List<Student>();
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@AcademicYear", (academicYear ?? "").Trim() }
                };

                DataTable dt = ExecuteQuery(
                    @"SELECT s.* FROM Students s
                      INNER JOIN Classes c ON s.ClassId = c.Id
                      WHERE s.SchoolId = @SchoolId
                        AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                        AND (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
                        AND TRIM(IFNULL(c.AcademicYear,'')) = @AcademicYear
                      ORDER BY c.ClassName, s.FullName",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    students.Add(MapStudentRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllStudentsForAcademicYear error: {ex.Message}");
            }
            return students;
        }

        private Student MapStudentRow(DataRow row)
        {
            var s = new Student
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ClassId = ConvertToInt(row["ClassId"]),
                AdmissionNumber = ConvertToString(row["AdmissionNumber"]),
                FullName = ConvertToString(row["FullName"]),
                DateOfBirth = ConvertToDateTime(row["DateOfBirth"]),
                Gender = ConvertToString(row["Gender"]),
                Address = ConvertToString(row["Address"]),
                ParentName = ConvertToString(row["ParentName"]),
                ParentPhone = ConvertToString(row["ParentPhone"]),
                HouseColour = ConvertToString(row["HouseColour"]),
                PhotoPath = ConvertToString(row["PhotoPath"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };

            if (row.Table.Columns.Contains("Stream"))
                s.Stream = ConvertToString(row["Stream"]);

            return s;
        }

        #endregion

        #region Affective Scores Methods

        public BehaviourScoreData GetBehaviourScores(int studentId, int term, string academicYear)
        {
            var result = new BehaviourScoreData();
            var parameters = new Dictionary<string, object> { { "@StudentId", studentId }, { "@Term", term }, { "@AcademicYear", academicYear } };
            DataTable dt = ExecuteQuery("SELECT * FROM AffectiveScores WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", parameters);

            if (dt.Rows.Count > 0)
            {
                DataRow row = dt.Rows[0];
                result.Term1ClassParticipationT1 = ConvertToInt(row["Term1ClassParticipationT1"]);
                result.Term1ClassParticipationT2 = ConvertToInt(row["Term1ClassParticipationT2"]);
                result.Term1HonestyT1 = ConvertToInt(row["Term1HonestyT1"]);
                result.Term1HonestyT2 = ConvertToInt(row["Term1HonestyT2"]);
                result.Term1EffortT1 = ConvertToInt(row["Term1EffortT1"]);
                result.Term1EffortT2 = ConvertToInt(row["Term1EffortT2"]);
                result.Term1SelfDisciplineT1 = ConvertToInt(row["Term1SelfDisciplineT1"]);
                result.Term1SelfDisciplineT2 = ConvertToInt(row["Term1SelfDisciplineT2"]);
                result.Term1PunctualityT1 = ConvertToInt(row["Term1PunctualityT1"]);
                result.Term1PunctualityT2 = ConvertToInt(row["Term1PunctualityT2"]);
                result.Term1SchoolInvolvementT1 = ConvertToInt(row["Term1SchoolInvolvementT1"]);
                result.Term1SchoolInvolvementT2 = ConvertToInt(row["Term1SchoolInvolvementT2"]);
                result.Term1NeatnessT1 = ConvertToInt(row["Term1NeatnessT1"]);
                result.Term1NeatnessT2 = ConvertToInt(row["Term1NeatnessT2"]);
                result.Term2ClassParticipationT1 = ConvertToInt(row["Term2ClassParticipationT1"]);
                result.Term2ClassParticipationT2 = ConvertToInt(row["Term2ClassParticipationT2"]);
                result.Term2HonestyT1 = ConvertToInt(row["Term2HonestyT1"]);
                result.Term2HonestyT2 = ConvertToInt(row["Term2HonestyT2"]);
                result.Term2EffortT1 = ConvertToInt(row["Term2EffortT1"]);
                result.Term2EffortT2 = ConvertToInt(row["Term2EffortT2"]);
                result.Term2SelfDisciplineT1 = ConvertToInt(row["Term2SelfDisciplineT1"]);
                result.Term2SelfDisciplineT2 = ConvertToInt(row["Term2SelfDisciplineT2"]);
                result.Term2PunctualityT1 = ConvertToInt(row["Term2PunctualityT1"]);
                result.Term2PunctualityT2 = ConvertToInt(row["Term2PunctualityT2"]);
                result.Term2SchoolInvolvementT1 = ConvertToInt(row["Term2SchoolInvolvementT1"]);
                result.Term2SchoolInvolvementT2 = ConvertToInt(row["Term2SchoolInvolvementT2"]);
                result.Term2NeatnessT1 = ConvertToInt(row["Term2NeatnessT1"]);
                result.Term2NeatnessT2 = ConvertToInt(row["Term2NeatnessT2"]);
                result.Term3ClassParticipationT1 = ConvertToInt(row["Term3ClassParticipationT1"]);
                result.Term3ClassParticipationT2 = ConvertToInt(row["Term3ClassParticipationT2"]);
                result.Term3HonestyT1 = ConvertToInt(row["Term3HonestyT1"]);
                result.Term3HonestyT2 = ConvertToInt(row["Term3HonestyT2"]);
                result.Term3EffortT1 = ConvertToInt(row["Term3EffortT1"]);
                result.Term3EffortT2 = ConvertToInt(row["Term3EffortT2"]);
                result.Term3SelfDisciplineT1 = ConvertToInt(row["Term3SelfDisciplineT1"]);
                result.Term3SelfDisciplineT2 = ConvertToInt(row["Term3SelfDisciplineT2"]);
                result.Term3PunctualityT1 = ConvertToInt(row["Term3PunctualityT1"]);
                result.Term3PunctualityT2 = ConvertToInt(row["Term3PunctualityT2"]);
                result.Term3SchoolInvolvementT1 = ConvertToInt(row["Term3SchoolInvolvementT1"]);
                result.Term3SchoolInvolvementT2 = ConvertToInt(row["Term3SchoolInvolvementT2"]);
                result.Term3NeatnessT1 = ConvertToInt(row["Term3NeatnessT1"]);
                result.Term3NeatnessT2 = ConvertToInt(row["Term3NeatnessT2"]);
                result.Term1Average = ConvertToDecimal(row["Term1Average"]);
                result.Term2Average = ConvertToDecimal(row["Term2Average"]);
                result.Term3Average = ConvertToDecimal(row["Term3Average"]);
                result.FinalAverage = ConvertToDecimal(row["FinalAverage"]);
                result.FinalGrade = ConvertToString(row["FinalGrade"]);
                result.Term = term;
                result.AcademicYear = academicYear;
            }
            else
            {
                result.Term = term;
                result.AcademicYear = academicYear;
            }
            return result;
        }

        public bool SaveAffectiveScore(BehaviourScoreData score, int studentId, int term, string academicYear)
        {
            try
            {
                var checkParams = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear }
                };
                int exists = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM AffectiveScores WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", checkParams));

                if (exists > 0)
                {
                    string query = @"UPDATE AffectiveScores SET 
                        Term1ClassParticipationT1 = @Term1ClassParticipationT1,
                        Term1ClassParticipationT2 = @Term1ClassParticipationT2,
                        Term1HonestyT1 = @Term1HonestyT1,
                        Term1HonestyT2 = @Term1HonestyT2,
                        Term1EffortT1 = @Term1EffortT1,
                        Term1EffortT2 = @Term1EffortT2,
                        Term1SelfDisciplineT1 = @Term1SelfDisciplineT1,
                        Term1SelfDisciplineT2 = @Term1SelfDisciplineT2,
                        Term1PunctualityT1 = @Term1PunctualityT1,
                        Term1PunctualityT2 = @Term1PunctualityT2,
                        Term1SchoolInvolvementT1 = @Term1SchoolInvolvementT1,
                        Term1SchoolInvolvementT2 = @Term1SchoolInvolvementT2,
                        Term1NeatnessT1 = @Term1NeatnessT1,
                        Term1NeatnessT2 = @Term1NeatnessT2,
                        Term2ClassParticipationT1 = @Term2ClassParticipationT1,
                        Term2ClassParticipationT2 = @Term2ClassParticipationT2,
                        Term2HonestyT1 = @Term2HonestyT1,
                        Term2HonestyT2 = @Term2HonestyT2,
                        Term2EffortT1 = @Term2EffortT1,
                        Term2EffortT2 = @Term2EffortT2,
                        Term2SelfDisciplineT1 = @Term2SelfDisciplineT1,
                        Term2SelfDisciplineT2 = @Term2SelfDisciplineT2,
                        Term2PunctualityT1 = @Term2PunctualityT1,
                        Term2PunctualityT2 = @Term2PunctualityT2,
                        Term2SchoolInvolvementT1 = @Term2SchoolInvolvementT1,
                        Term2SchoolInvolvementT2 = @Term2SchoolInvolvementT2,
                        Term2NeatnessT1 = @Term2NeatnessT1,
                        Term2NeatnessT2 = @Term2NeatnessT2,
                        Term3ClassParticipationT1 = @Term3ClassParticipationT1,
                        Term3ClassParticipationT2 = @Term3ClassParticipationT2,
                        Term3HonestyT1 = @Term3HonestyT1,
                        Term3HonestyT2 = @Term3HonestyT2,
                        Term3EffortT1 = @Term3EffortT1,
                        Term3EffortT2 = @Term3EffortT2,
                        Term3SelfDisciplineT1 = @Term3SelfDisciplineT1,
                        Term3SelfDisciplineT2 = @Term3SelfDisciplineT2,
                        Term3PunctualityT1 = @Term3PunctualityT1,
                        Term3PunctualityT2 = @Term3PunctualityT2,
                        Term3SchoolInvolvementT1 = @Term3SchoolInvolvementT1,
                        Term3SchoolInvolvementT2 = @Term3SchoolInvolvementT2,
                        Term3NeatnessT1 = @Term3NeatnessT1,
                        Term3NeatnessT2 = @Term3NeatnessT2,
                        Term1Average = @Term1Average,
                        Term2Average = @Term2Average,
                        Term3Average = @Term3Average,
                        FinalAverage = @FinalAverage,
                        FinalGrade = @FinalGrade,
                        ModifiedDate = @ModifiedDate
                        WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear";

                    var parameters = BuildAffectiveParameters(score, studentId, term, academicYear);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
                else
                {
                    string query = @"INSERT INTO AffectiveScores (
                        StudentId, Term, AcademicYear,
                        Term1ClassParticipationT1, Term1ClassParticipationT2, Term1HonestyT1, Term1HonestyT2,
                        Term1EffortT1, Term1EffortT2, Term1SelfDisciplineT1, Term1SelfDisciplineT2,
                        Term1PunctualityT1, Term1PunctualityT2, Term1SchoolInvolvementT1, Term1SchoolInvolvementT2,
                        Term1NeatnessT1, Term1NeatnessT2,
                        Term2ClassParticipationT1, Term2ClassParticipationT2, Term2HonestyT1, Term2HonestyT2,
                        Term2EffortT1, Term2EffortT2, Term2SelfDisciplineT1, Term2SelfDisciplineT2,
                        Term2PunctualityT1, Term2PunctualityT2, Term2SchoolInvolvementT1, Term2SchoolInvolvementT2,
                        Term2NeatnessT1, Term2NeatnessT2,
                        Term3ClassParticipationT1, Term3ClassParticipationT2, Term3HonestyT1, Term3HonestyT2,
                        Term3EffortT1, Term3EffortT2, Term3SelfDisciplineT1, Term3SelfDisciplineT2,
                        Term3PunctualityT1, Term3PunctualityT2, Term3SchoolInvolvementT1, Term3SchoolInvolvementT2,
                        Term3NeatnessT1, Term3NeatnessT2,
                        Term1Average, Term2Average, Term3Average, FinalAverage, FinalGrade,
                        CreatedDate, ModifiedDate
                    ) VALUES (
                        @StudentId, @Term, @AcademicYear,
                        @Term1ClassParticipationT1, @Term1ClassParticipationT2, @Term1HonestyT1, @Term1HonestyT2,
                        @Term1EffortT1, @Term1EffortT2, @Term1SelfDisciplineT1, @Term1SelfDisciplineT2,
                        @Term1PunctualityT1, @Term1PunctualityT2, @Term1SchoolInvolvementT1, @Term1SchoolInvolvementT2,
                        @Term1NeatnessT1, @Term1NeatnessT2,
                        @Term2ClassParticipationT1, @Term2ClassParticipationT2, @Term2HonestyT1, @Term2HonestyT2,
                        @Term2EffortT1, @Term2EffortT2, @Term2SelfDisciplineT1, @Term2SelfDisciplineT2,
                        @Term2PunctualityT1, @Term2PunctualityT2, @Term2SchoolInvolvementT1, @Term2SchoolInvolvementT2,
                        @Term2NeatnessT1, @Term2NeatnessT2,
                        @Term3ClassParticipationT1, @Term3ClassParticipationT2, @Term3HonestyT1, @Term3HonestyT2,
                        @Term3EffortT1, @Term3EffortT2, @Term3SelfDisciplineT1, @Term3SelfDisciplineT2,
                        @Term3PunctualityT1, @Term3PunctualityT2, @Term3SchoolInvolvementT1, @Term3SchoolInvolvementT2,
                        @Term3NeatnessT1, @Term3NeatnessT2,
                        @Term1Average, @Term2Average, @Term3Average, @FinalAverage, @FinalGrade,
                        @CreatedDate, @ModifiedDate
                    )";

                    var parameters = BuildAffectiveParameters(score, studentId, term, academicYear);
                    parameters.Add("@CreatedDate", DateTime.Now);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveAffectiveScore error: {ex.Message}");
                return false;
            }
        }

        private Dictionary<string, object> BuildAffectiveParameters(BehaviourScoreData score, int studentId, int term, string academicYear)
        {
            return new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@Term", term },
                { "@AcademicYear", academicYear },
                { "@Term1ClassParticipationT1", score.Term1ClassParticipationT1 },
                { "@Term1ClassParticipationT2", score.Term1ClassParticipationT2 },
                { "@Term1HonestyT1", score.Term1HonestyT1 },
                { "@Term1HonestyT2", score.Term1HonestyT2 },
                { "@Term1EffortT1", score.Term1EffortT1 },
                { "@Term1EffortT2", score.Term1EffortT2 },
                { "@Term1SelfDisciplineT1", score.Term1SelfDisciplineT1 },
                { "@Term1SelfDisciplineT2", score.Term1SelfDisciplineT2 },
                { "@Term1PunctualityT1", score.Term1PunctualityT1 },
                { "@Term1PunctualityT2", score.Term1PunctualityT2 },
                { "@Term1SchoolInvolvementT1", score.Term1SchoolInvolvementT1 },
                { "@Term1SchoolInvolvementT2", score.Term1SchoolInvolvementT2 },
                { "@Term1NeatnessT1", score.Term1NeatnessT1 },
                { "@Term1NeatnessT2", score.Term1NeatnessT2 },
                { "@Term2ClassParticipationT1", score.Term2ClassParticipationT1 },
                { "@Term2ClassParticipationT2", score.Term2ClassParticipationT2 },
                { "@Term2HonestyT1", score.Term2HonestyT1 },
                { "@Term2HonestyT2", score.Term2HonestyT2 },
                { "@Term2EffortT1", score.Term2EffortT1 },
                { "@Term2EffortT2", score.Term2EffortT2 },
                { "@Term2SelfDisciplineT1", score.Term2SelfDisciplineT1 },
                { "@Term2SelfDisciplineT2", score.Term2SelfDisciplineT2 },
                { "@Term2PunctualityT1", score.Term2PunctualityT1 },
                { "@Term2PunctualityT2", score.Term2PunctualityT2 },
                { "@Term2SchoolInvolvementT1", score.Term2SchoolInvolvementT1 },
                { "@Term2SchoolInvolvementT2", score.Term2SchoolInvolvementT2 },
                { "@Term2NeatnessT1", score.Term2NeatnessT1 },
                { "@Term2NeatnessT2", score.Term2NeatnessT2 },
                { "@Term3ClassParticipationT1", score.Term3ClassParticipationT1 },
                { "@Term3ClassParticipationT2", score.Term3ClassParticipationT2 },
                { "@Term3HonestyT1", score.Term3HonestyT1 },
                { "@Term3HonestyT2", score.Term3HonestyT2 },
                { "@Term3EffortT1", score.Term3EffortT1 },
                { "@Term3EffortT2", score.Term3EffortT2 },
                { "@Term3SelfDisciplineT1", score.Term3SelfDisciplineT1 },
                { "@Term3SelfDisciplineT2", score.Term3SelfDisciplineT2 },
                { "@Term3PunctualityT1", score.Term3PunctualityT1 },
                { "@Term3PunctualityT2", score.Term3PunctualityT2 },
                { "@Term3SchoolInvolvementT1", score.Term3SchoolInvolvementT1 },
                { "@Term3SchoolInvolvementT2", score.Term3SchoolInvolvementT2 },
                { "@Term3NeatnessT1", score.Term3NeatnessT1 },
                { "@Term3NeatnessT2", score.Term3NeatnessT2 },
                { "@Term1Average", score.Term1Average },
                { "@Term2Average", score.Term2Average },
                { "@Term3Average", score.Term3Average },
                { "@FinalAverage", score.FinalAverage },
                { "@FinalGrade", score.FinalGrade ?? "" },
                { "@ModifiedDate", DateTime.Now }
            };
        }

        #endregion

        #region Psychomotor Scores Methods

        public PsychomotorScoreData GetPsychomotorScores(int studentId, int term, string academicYear)
        {
            var result = new PsychomotorScoreData();
            var parameters = new Dictionary<string, object> { { "@StudentId", studentId }, { "@Term", term }, { "@AcademicYear", academicYear } };
            DataTable dt = ExecuteQuery("SELECT * FROM PsychomotorScores WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", parameters);

            if (dt.Rows.Count > 0)
            {
                DataRow row = dt.Rows[0];
                result.Term1IndividualSportsT1 = ConvertToInt(row["Term1IndividualSportsT1"]);
                result.Term1IndividualSportsT2 = ConvertToInt(row["Term1IndividualSportsT2"]);
                result.Term1TeamPlayerT1 = ConvertToInt(row["Term1TeamPlayerT1"]);
                result.Term1TeamPlayerT2 = ConvertToInt(row["Term1TeamPlayerT2"]);
                result.Term1AthleticsT1 = ConvertToInt(row["Term1AthleticsT1"]);
                result.Term1AthleticsT2 = ConvertToInt(row["Term1AthleticsT2"]);
                result.Term1DrawingT1 = ConvertToInt(row["Term1DrawingT1"]);
                result.Term1DrawingT2 = ConvertToInt(row["Term1DrawingT2"]);
                result.Term1HandwritingT1 = ConvertToInt(row["Term1HandwritingT1"]);
                result.Term1HandwritingT2 = ConvertToInt(row["Term1HandwritingT2"]);
                result.Term2IndividualSportsT1 = ConvertToInt(row["Term2IndividualSportsT1"]);
                result.Term2IndividualSportsT2 = ConvertToInt(row["Term2IndividualSportsT2"]);
                result.Term2TeamPlayerT1 = ConvertToInt(row["Term2TeamPlayerT1"]);
                result.Term2TeamPlayerT2 = ConvertToInt(row["Term2TeamPlayerT2"]);
                result.Term2AthleticsT1 = ConvertToInt(row["Term2AthleticsT1"]);
                result.Term2AthleticsT2 = ConvertToInt(row["Term2AthleticsT2"]);
                result.Term2DrawingT1 = ConvertToInt(row["Term2DrawingT1"]);
                result.Term2DrawingT2 = ConvertToInt(row["Term2DrawingT2"]);
                result.Term2HandwritingT1 = ConvertToInt(row["Term2HandwritingT1"]);
                result.Term2HandwritingT2 = ConvertToInt(row["Term2HandwritingT2"]);
                result.Term3IndividualSportsT1 = ConvertToInt(row["Term3IndividualSportsT1"]);
                result.Term3IndividualSportsT2 = ConvertToInt(row["Term3IndividualSportsT2"]);
                result.Term3TeamPlayerT1 = ConvertToInt(row["Term3TeamPlayerT1"]);
                result.Term3TeamPlayerT2 = ConvertToInt(row["Term3TeamPlayerT2"]);
                result.Term3AthleticsT1 = ConvertToInt(row["Term3AthleticsT1"]);
                result.Term3AthleticsT2 = ConvertToInt(row["Term3AthleticsT2"]);
                result.Term3DrawingT1 = ConvertToInt(row["Term3DrawingT1"]);
                result.Term3DrawingT2 = ConvertToInt(row["Term3DrawingT2"]);
                result.Term3HandwritingT1 = ConvertToInt(row["Term3HandwritingT1"]);
                result.Term3HandwritingT2 = ConvertToInt(row["Term3HandwritingT2"]);
                result.Term1Average = ConvertToDecimal(row["Term1Average"]);
                result.Term2Average = ConvertToDecimal(row["Term2Average"]);
                result.Term3Average = ConvertToDecimal(row["Term3Average"]);
                result.FinalAverage = ConvertToDecimal(row["FinalAverage"]);
                result.FinalGrade = ConvertToString(row["FinalGrade"]);
                result.Term = term;
                result.AcademicYear = academicYear;
            }
            else
            {
                result.Term = term;
                result.AcademicYear = academicYear;
            }
            return result;
        }

        public bool SavePsychomotorScore(PsychomotorScoreData score, int studentId, int term, string academicYear)
        {
            try
            {
                var checkParams = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear }
                };
                int exists = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM PsychomotorScores WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", checkParams));

                if (exists > 0)
                {
                    string query = @"UPDATE PsychomotorScores SET 
                        Term1IndividualSportsT1 = @Term1IndividualSportsT1,
                        Term1IndividualSportsT2 = @Term1IndividualSportsT2,
                        Term1TeamPlayerT1 = @Term1TeamPlayerT1,
                        Term1TeamPlayerT2 = @Term1TeamPlayerT2,
                        Term1AthleticsT1 = @Term1AthleticsT1,
                        Term1AthleticsT2 = @Term1AthleticsT2,
                        Term1DrawingT1 = @Term1DrawingT1,
                        Term1DrawingT2 = @Term1DrawingT2,
                        Term1HandwritingT1 = @Term1HandwritingT1,
                        Term1HandwritingT2 = @Term1HandwritingT2,
                        Term2IndividualSportsT1 = @Term2IndividualSportsT1,
                        Term2IndividualSportsT2 = @Term2IndividualSportsT2,
                        Term2TeamPlayerT1 = @Term2TeamPlayerT1,
                        Term2TeamPlayerT2 = @Term2TeamPlayerT2,
                        Term2AthleticsT1 = @Term2AthleticsT1,
                        Term2AthleticsT2 = @Term2AthleticsT2,
                        Term2DrawingT1 = @Term2DrawingT1,
                        Term2DrawingT2 = @Term2DrawingT2,
                        Term2HandwritingT1 = @Term2HandwritingT1,
                        Term2HandwritingT2 = @Term2HandwritingT2,
                        Term3IndividualSportsT1 = @Term3IndividualSportsT1,
                        Term3IndividualSportsT2 = @Term3IndividualSportsT2,
                        Term3TeamPlayerT1 = @Term3TeamPlayerT1,
                        Term3TeamPlayerT2 = @Term3TeamPlayerT2,
                        Term3AthleticsT1 = @Term3AthleticsT1,
                        Term3AthleticsT2 = @Term3AthleticsT2,
                        Term3DrawingT1 = @Term3DrawingT1,
                        Term3DrawingT2 = @Term3DrawingT2,
                        Term3HandwritingT1 = @Term3HandwritingT1,
                        Term3HandwritingT2 = @Term3HandwritingT2,
                        Term1Average = @Term1Average,
                        Term2Average = @Term2Average,
                        Term3Average = @Term3Average,
                        FinalAverage = @FinalAverage,
                        FinalGrade = @FinalGrade,
                        ModifiedDate = @ModifiedDate
                        WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear";

                    var parameters = BuildPsychomotorParameters(score, studentId, term, academicYear);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
                else
                {
                    string query = @"INSERT INTO PsychomotorScores (
                        StudentId, Term, AcademicYear,
                        Term1IndividualSportsT1, Term1IndividualSportsT2, Term1TeamPlayerT1, Term1TeamPlayerT2,
                        Term1AthleticsT1, Term1AthleticsT2, Term1DrawingT1, Term1DrawingT2,
                        Term1HandwritingT1, Term1HandwritingT2,
                        Term2IndividualSportsT1, Term2IndividualSportsT2, Term2TeamPlayerT1, Term2TeamPlayerT2,
                        Term2AthleticsT1, Term2AthleticsT2, Term2DrawingT1, Term2DrawingT2,
                        Term2HandwritingT1, Term2HandwritingT2,
                        Term3IndividualSportsT1, Term3IndividualSportsT2, Term3TeamPlayerT1, Term3TeamPlayerT2,
                        Term3AthleticsT1, Term3AthleticsT2, Term3DrawingT1, Term3DrawingT2,
                        Term3HandwritingT1, Term3HandwritingT2,
                        Term1Average, Term2Average, Term3Average, FinalAverage, FinalGrade,
                        CreatedDate, ModifiedDate
                    ) VALUES (
                        @StudentId, @Term, @AcademicYear,
                        @Term1IndividualSportsT1, @Term1IndividualSportsT2, @Term1TeamPlayerT1, @Term1TeamPlayerT2,
                        @Term1AthleticsT1, @Term1AthleticsT2, @Term1DrawingT1, @Term1DrawingT2,
                        @Term1HandwritingT1, @Term1HandwritingT2,
                        @Term2IndividualSportsT1, @Term2IndividualSportsT2, @Term2TeamPlayerT1, @Term2TeamPlayerT2,
                        @Term2AthleticsT1, @Term2AthleticsT2, @Term2DrawingT1, @Term2DrawingT2,
                        @Term2HandwritingT1, @Term2HandwritingT2,
                        @Term3IndividualSportsT1, @Term3IndividualSportsT2, @Term3TeamPlayerT1, @Term3TeamPlayerT2,
                        @Term3AthleticsT1, @Term3AthleticsT2, @Term3DrawingT1, @Term3DrawingT2,
                        @Term3HandwritingT1, @Term3HandwritingT2,
                        @Term1Average, @Term2Average, @Term3Average, @FinalAverage, @FinalGrade,
                        @CreatedDate, @ModifiedDate
                    )";

                    var parameters = BuildPsychomotorParameters(score, studentId, term, academicYear);
                    parameters.Add("@CreatedDate", DateTime.Now);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SavePsychomotorScore error: {ex.Message}");
                return false;
            }
        }

        private Dictionary<string, object> BuildPsychomotorParameters(PsychomotorScoreData score, int studentId, int term, string academicYear)
        {
            return new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@Term", term },
                { "@AcademicYear", academicYear },
                { "@Term1IndividualSportsT1", score.Term1IndividualSportsT1 },
                { "@Term1IndividualSportsT2", score.Term1IndividualSportsT2 },
                { "@Term1TeamPlayerT1", score.Term1TeamPlayerT1 },
                { "@Term1TeamPlayerT2", score.Term1TeamPlayerT2 },
                { "@Term1AthleticsT1", score.Term1AthleticsT1 },
                { "@Term1AthleticsT2", score.Term1AthleticsT2 },
                { "@Term1DrawingT1", score.Term1DrawingT1 },
                { "@Term1DrawingT2", score.Term1DrawingT2 },
                { "@Term1HandwritingT1", score.Term1HandwritingT1 },
                { "@Term1HandwritingT2", score.Term1HandwritingT2 },
                { "@Term2IndividualSportsT1", score.Term2IndividualSportsT1 },
                { "@Term2IndividualSportsT2", score.Term2IndividualSportsT2 },
                { "@Term2TeamPlayerT1", score.Term2TeamPlayerT1 },
                { "@Term2TeamPlayerT2", score.Term2TeamPlayerT2 },
                { "@Term2AthleticsT1", score.Term2AthleticsT1 },
                { "@Term2AthleticsT2", score.Term2AthleticsT2 },
                { "@Term2DrawingT1", score.Term2DrawingT1 },
                { "@Term2DrawingT2", score.Term2DrawingT2 },
                { "@Term2HandwritingT1", score.Term2HandwritingT1 },
                { "@Term2HandwritingT2", score.Term2HandwritingT2 },
                { "@Term3IndividualSportsT1", score.Term3IndividualSportsT1 },
                { "@Term3IndividualSportsT2", score.Term3IndividualSportsT2 },
                { "@Term3TeamPlayerT1", score.Term3TeamPlayerT1 },
                { "@Term3TeamPlayerT2", score.Term3TeamPlayerT2 },
                { "@Term3AthleticsT1", score.Term3AthleticsT1 },
                { "@Term3AthleticsT2", score.Term3AthleticsT2 },
                { "@Term3DrawingT1", score.Term3DrawingT1 },
                { "@Term3DrawingT2", score.Term3DrawingT2 },
                { "@Term3HandwritingT1", score.Term3HandwritingT1 },
                { "@Term3HandwritingT2", score.Term3HandwritingT2 },
                { "@Term1Average", score.Term1Average },
                { "@Term2Average", score.Term2Average },
                { "@Term3Average", score.Term3Average },
                { "@FinalAverage", score.FinalAverage },
                { "@FinalGrade", score.FinalGrade ?? "" },
                { "@ModifiedDate", DateTime.Now }
            };
        }

        #endregion

        #region Cognitive Scores Methods


        public List<SubjectScoreData> GetSubjectScores(int studentId, int term, string academicYear)
        {
            var results = new List<SubjectScoreData>();
            var parameters = new Dictionary<string, object> { { "@StudentId", studentId }, { "@Term", term }, { "@AcademicYear", academicYear } };
            DataTable dt = ExecuteQuery("SELECT * FROM AcademicPerformance WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", parameters);

            foreach (DataRow row in dt.Rows)
            {
                int subjectId = ConvertToInt(row["SubjectId"]);
                var score = new SubjectScoreData
                {
                    SubjectId = subjectId,
                    T1 = row["T1"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T1"]),
                    T2 = row["T2"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T2"]),
                    T3 = row["T3"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T3"]),
                    T4 = row["T4"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T4"]),
                    T5 = row["T5"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T5"]),
                    T6 = row["T6"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T6"]),
                    Exam = row["Exam"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["Exam"]),
                    FinalScore = row["FinalScore"] == DBNull.Value ? 0 : ConvertToDecimal(row["FinalScore"]),
                    Grade = row["Grade"] == DBNull.Value ? "" : ConvertToString(row["Grade"]),
                    StudentId = studentId,
                    Term = term,
                    AcademicYear = academicYear
                };
                results.Add(score);
            }
            return results;
        }
        public (List<SubjectScoreData> Term1, List<SubjectScoreData> Term2, List<SubjectScoreData> Term3)
            GetAllSubjectScores(int studentId, string academicYear)
        {
            var allScores = new List<SubjectScoreData>();
            var parameters = new Dictionary<string, object>
    {
        { "@StudentId", studentId },
        { "@AcademicYear", academicYear }
    };

            string query = @"
        SELECT Term, SubjectId, T1, T2, T3, T4, T5, T6, Exam, FinalScore, Grade
        FROM AcademicPerformance
        WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear
    ";

            DataTable dt = ExecuteQuery(query, parameters);

            foreach (DataRow row in dt.Rows)
            {
                allScores.Add(new SubjectScoreData
                {
                    Term = ConvertToInt(row["Term"]),
                    SubjectId = ConvertToInt(row["SubjectId"]),
                    T1 = row["T1"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T1"]),
                    T2 = row["T2"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T2"]),
                    T3 = row["T3"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T3"]),
                    T4 = row["T4"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T4"]),
                    T5 = row["T5"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T5"]),
                    T6 = row["T6"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["T6"]),
                    Exam = row["Exam"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["Exam"]),
                    FinalScore = row["FinalScore"] == DBNull.Value ? 0 : ConvertToDecimal(row["FinalScore"]),
                    Grade = row["Grade"] == DBNull.Value ? "" : ConvertToString(row["Grade"]),
                    StudentId = studentId,
                    AcademicYear = academicYear
                });
            }

            return (
                Term1: allScores.Where(s => s.Term == 1).ToList(),
                Term2: allScores.Where(s => s.Term == 2).ToList(),
                Term3: allScores.Where(s => s.Term == 3).ToList()
            );
        }
        public bool SaveCognitiveScore(SubjectScoreData score, int studentId, int subjectId, int term, string academicYear)
        {
            if (score == null || studentId <= 0 || subjectId <= 0) return false;

            try
            {
                var checkParams = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SubjectId", subjectId },
            { "@Term", term },
            { "@AcademicYear", academicYear ?? "" }
        };

                DataTable existingDt = ExecuteQuery(
                    "SELECT * FROM AcademicPerformance WHERE StudentId = @StudentId AND SubjectId = @SubjectId AND Term = @Term AND AcademicYear = @AcademicYear",
                    checkParams);

                SubjectScoreData before = null;
                if (existingDt.Rows.Count > 0)
                {
                    var r = existingDt.Rows[0];
                    before = new SubjectScoreData
                    {
                        SubjectId = subjectId,
                        T1 = r["T1"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T1"]),
                        T2 = r["T2"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T2"]),
                        T3 = r["T3"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T3"]),
                        T4 = r["T4"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T4"]),
                        T5 = r["T5"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T5"]),
                        T6 = r["T6"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["T6"]),
                        Exam = r["Exam"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(r["Exam"]),
                        FinalScore = r["FinalScore"] == DBNull.Value ? 0 : ConvertToDecimal(r["FinalScore"]),
                        Grade = r["Grade"] == DBNull.Value ? "" : ConvertToString(r["Grade"])
                    };
                }

                bool writeOk;
                string operationType;

                if (before != null)
                {
                    operationType = "Update";
                    writeOk = UpdateCognitiveScoreInternal(score, studentId, subjectId, term, academicYear);
                }
                else
                {
                    operationType = "Insert";
                    writeOk = InsertCognitiveScoreInternal(score, studentId, subjectId, term, academicYear);
                }

                if (writeOk)
                {
                    try
                    {
                        LogScoreChanges(before, score, studentId, subjectId, term, academicYear, operationType);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Audit logging failed (non-fatal): {ex.Message}");
                    }
                }

                return writeOk;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveCognitiveScore error: {ex.Message}");
                return false;
            }
        }
        private bool UpdateCognitiveScoreInternal(SubjectScoreData score, int studentId, int subjectId, int term, string academicYear)
        {
            var updateParams = new Dictionary<string, object>
    {
        { "@StudentId", studentId },
        { "@SubjectId", subjectId },
        { "@Term", term },
        { "@AcademicYear", academicYear ?? "" },
        { "@ModifiedDate", DateTime.Now }
    };

            string updateFields;

            if (term == 1)
            {
                updateFields = "T1 = @T1, T2 = @T2";
                updateParams.Add("@T1", score.T1 ?? 0);
                updateParams.Add("@T2", score.T2 ?? 0);
            }
            else if (term == 2)
            {
                updateFields = "T3 = @T3, T4 = @T4";
                updateParams.Add("@T3", score.T3 ?? 0);
                updateParams.Add("@T4", score.T4 ?? 0);
            }
            else if (term == 3)
            {
                updateFields = "T5 = @T5, T6 = @T6, Exam = @Exam";
                updateParams.Add("@T5", score.T5 ?? 0);
                updateParams.Add("@T6", score.T6 ?? 0);
                updateParams.Add("@Exam", score.Exam ?? 0);
            }
            else
            {
                return false;
            }

            updateFields += ", FinalScore = @FinalScore, Grade = @Grade";
            updateParams.Add("@FinalScore", score.FinalScore);
            updateParams.Add("@Grade", score.Grade ?? "");

            string query = $"UPDATE AcademicPerformance SET {updateFields}, ModifiedDate = @ModifiedDate WHERE StudentId = @StudentId AND SubjectId = @SubjectId AND Term = @Term AND AcademicYear = @AcademicYear";
            return ExecuteNonQuery(query, updateParams) > 0;
        }

        private bool InsertCognitiveScoreInternal(SubjectScoreData score, int studentId, int subjectId, int term, string academicYear)
        {
            var currentUser = Global.CurrentUser;
            int? enteredBy = currentUser?.UserId;
            string enteredRole = currentUser?.Role ?? "";

            var insertParams = new Dictionary<string, object>
    {
        { "@StudentId", studentId },
        { "@SubjectId", subjectId },
        { "@Term", term },
        { "@AcademicYear", academicYear ?? "" },
        { "@T1", term == 1 ? (score.T1 ?? 0) : 0 },
        { "@T2", term == 1 ? (score.T2 ?? 0) : 0 },
        { "@T3", term == 2 ? (score.T3 ?? 0) : 0 },
        { "@T4", term == 2 ? (score.T4 ?? 0) : 0 },
        { "@T5", term == 3 ? (score.T5 ?? 0) : 0 },
        { "@T6", term == 3 ? (score.T6 ?? 0) : 0 },
        { "@Exam", term == 3 ? (score.Exam ?? 0) : 0 },
        { "@FinalScore", score.FinalScore },
        { "@Grade", score.Grade ?? "" },
        { "@EnteredByUserId", (object)enteredBy ?? DBNull.Value },
        { "@EnteredByRole",   enteredRole },
        { "@EnteredDate",     DateTime.UtcNow },
        { "@CreatedDate", DateTime.Now },
        { "@ModifiedDate", DateTime.Now }
    };

            string query = @"INSERT INTO AcademicPerformance
                (StudentId, SubjectId, Term, AcademicYear, T1, T2, T3, T4, T5, T6, Exam, FinalScore, Grade,
                 EnteredByUserId, EnteredByRole, EnteredDate,
                 CreatedDate, ModifiedDate)
                VALUES
                (@StudentId, @SubjectId, @Term, @AcademicYear, @T1, @T2, @T3, @T4, @T5, @T6, @Exam, @FinalScore, @Grade,
                 @EnteredByUserId, @EnteredByRole, @EnteredDate,
                 @CreatedDate, @ModifiedDate)";
            return ExecuteNonQuery(query, insertParams) > 0;
        }

        private void EnsureNotificationsTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Notifications (
                        Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        SyncId          TEXT NOT NULL UNIQUE,
                        SchoolId        INTEGER NOT NULL,
                        UserId          INTEGER NOT NULL,
                        Type            TEXT NOT NULL,
                        Title           TEXT NOT NULL,
                        Body            TEXT NOT NULL,
                        PayloadJson     TEXT,
                        CreatedDateUtc  DATETIME NOT NULL,
                        IsRead          INTEGER DEFAULT 0,
                        IsPushed        INTEGER DEFAULT 0,
                        PushedUtc       DATETIME
                    )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_notifications_user ON Notifications(UserId, CreatedDateUtc DESC);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_notifications_unread ON Notifications(UserId, IsRead);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_notifications_push ON Notifications(IsPushed, SchoolId);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureNotificationsTable error: {ex.Message}");
            }
        }
        private void EnsureParentSubscriptionsTable(SqliteConnection connection)
        {
            try
            {
                // 1. Global parent identity — one row per phone number, shared across all schools
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS Parents (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PhoneE164 TEXT NOT NULL UNIQUE,
                        FullName TEXT,
                        DeviceId TEXT,
                        PinHash TEXT,
                        IsSubscribed INTEGER DEFAULT 0,
                        CreatedUtc DATETIME NOT NULL,
                        UpdatedUtc DATETIME NOT NULL
                    )", null, connection);

                // 2. Drop legacy ParentSubscriptions if it still has SchoolId (old shape)
                var columns = GetTableColumnNames(connection, "ParentSubscriptions");
                if (columns.Count > 0 && columns.Contains("SchoolId"))
                {
                    ExecuteNonQuery("DROP TABLE IF EXISTS ParentSubscriptions", null, connection);
                }

                // 3. New ParentSubscriptions — one active sub per parent, no school link
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS ParentSubscriptions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SyncId TEXT NOT NULL UNIQUE,
                        ParentId INTEGER NOT NULL,
                        PlanCode TEXT NOT NULL DEFAULT 'monthly',
                        Amount DECIMAL(18,2) DEFAULT 0,
                        Currency TEXT NOT NULL DEFAULT 'SLL',
                        Provider TEXT NOT NULL DEFAULT 'OrangeMoney',
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        PaymentReference TEXT,
                        ProviderTransactionId TEXT,
                        StartedUtc DATETIME,
                        ExpiresUtc DATETIME,
                        GraceUntilUtc DATETIME,
                        Source TEXT NOT NULL DEFAULT 'mobile',
                        CreatedUtc DATETIME NOT NULL,
                        UpdatedUtc DATETIME NOT NULL,
                        UNIQUE(ParentId)
                    )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_parentsubs_parent ON ParentSubscriptions(ParentId, Status);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_parentsubs_ref ON ParentSubscriptions(PaymentReference);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_parents_phone ON Parents(PhoneE164);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureParentSubscriptionsTable error: {ex.Message}");
            }
        }

        private void EnsureSyncQueueTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS SyncQueue (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TargetUrl TEXT NOT NULL,
                        PayloadJson TEXT NOT NULL,
                        HttpMethod TEXT NOT NULL DEFAULT 'POST',
                        CreatedUtc DATETIME NOT NULL,
                        RetryCount INTEGER DEFAULT 0,
                        LastError TEXT
                    )", null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSyncQueueTable error: {ex.Message}");
            }
        }

        // ── Parent Management (Desktop Admin) ───────────────────────────

        public DataTable GetAllParentsWithSubscriptions()
        {
            try
            {
                return ExecuteQuery(@"
                    SELECT 
                        p.Id AS ParentId,
                        p.PhoneE164,
                        p.FullName,
                        p.DeviceId,
                        p.IsSubscribed,
                        p.CreatedUtc,
                        ps.PlanCode,
                        ps.Status AS SubStatus,
                        ps.ExpiresUtc,
                        (SELECT COUNT(*) FROM Students s WHERE s.ParentPhone = p.PhoneE164 AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)) AS ChildCount,
                        (SELECT GROUP_CONCAT(sch.SchoolName, ', ') 
                         FROM Students s 
                         JOIN Schools sch ON s.SchoolId = sch.Id 
                         WHERE s.ParentPhone = p.PhoneE164 AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)) AS SchoolsList
                    FROM Parents p
                    LEFT JOIN ParentSubscriptions ps ON p.Id = ps.ParentId
                    ORDER BY p.CreatedUtc DESC");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllParentsWithSubscriptions error: {ex.Message}");
                return new DataTable();
            }
        }

        public bool ResetParentDevice(int parentId)
        {
            try
            {
                return ExecuteNonQuery(
                    "UPDATE Parents SET DeviceId = NULL, UpdatedUtc = @Now WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", parentId }, { "@Now", DateTime.UtcNow } }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetParentDevice error: {ex.Message}");
                return false;
            }
        }

        public bool RevokeParentSubscription(int parentId)
        {
            try
            {
                // 1. Mark parent as unsubscribed
                ExecuteNonQuery(
                    "UPDATE Parents SET IsSubscribed = 0, UpdatedUtc = @Now WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", parentId }, { "@Now", DateTime.UtcNow } });

                // 2. Expire the active subscription
                ExecuteNonQuery(
                    "UPDATE ParentSubscriptions SET Status = 'Expired', ExpiresUtc = @Now, UpdatedUtc = @Now WHERE ParentId = @Id",
                    new Dictionary<string, object> { { "@Id", parentId }, { "@Now", DateTime.UtcNow } });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevokeParentSubscription error: {ex.Message}");
                return false;
            }
        }
        private void EnsurePublishedReportsTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS PublishedReports (
                        Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                        SyncId                TEXT NOT NULL UNIQUE,
                        SchoolId              INTEGER NOT NULL,
                        ClassId               INTEGER NOT NULL,
                        AcademicYear          TEXT NOT NULL,
                        Term                  INTEGER NOT NULL,
                        PublishedDateUtc      DATETIME NOT NULL,
                        PublishedByUserId     INTEGER NOT NULL,
                        PublishedByName       TEXT NOT NULL,
                        SnapshotCount         INTEGER DEFAULT 0,
                        IsRevoked             INTEGER DEFAULT 0,
                        RevokedDateUtc        DATETIME,
                        RevokedByUserId       INTEGER,
                        UNIQUE(SchoolId, ClassId, AcademicYear, Term))", null, connection);

                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS PublishedReportData (
                        Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                        SyncId                TEXT NOT NULL UNIQUE,
                        PublishedReportId     INTEGER NOT NULL,
                        StudentId             INTEGER NOT NULL,
                        DataJson              TEXT NOT NULL,
                        DataHash              TEXT,
                        CreatedDateUtc        DATETIME NOT NULL,
                        FOREIGN KEY (PublishedReportId) REFERENCES PublishedReports(Id) ON DELETE CASCADE)",
                    null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_pubdata_report ON PublishedReportData(PublishedReportId);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_pubdata_student ON PublishedReportData(StudentId);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsurePublishedReportsTable error: {ex.Message}");
            }
        }
        private void LogScoreChanges(SubjectScoreData before, SubjectScoreData after, int studentId, int subjectId, int term, string academicYear, string operationType)
        {
            string year = academicYear ?? "";
            DateTime utcNow = DateTime.UtcNow;
            int? userId = Global.CurrentUser?.UserId;
            string userName = Global.CurrentUser?.FullName ?? Global.CurrentUser?.Username ?? "";
            string userRole = Global.CurrentUser?.Role ?? "";

            var fields = new List<string>();
            if (term == 1) { fields.Add("T1"); fields.Add("T2"); }
            else if (term == 2) { fields.Add("T3"); fields.Add("T4"); }
            else if (term == 3) { fields.Add("T5"); fields.Add("T6"); fields.Add("Exam"); }

            fields.Add("FinalScore");
            fields.Add("Grade");

            foreach (var field in fields)
            {
                string oldVal = GetFieldValueAsString(before, field);
                string newVal = GetFieldValueAsString(after, field);

                if (operationType == "Update" && string.Equals(oldVal, newVal, StringComparison.Ordinal))
                    continue;

                var audit = new ScoreAuditLog
                {
                    StudentId = studentId,
                    SubjectId = subjectId,
                    Term = term,
                    AcademicYear = year,
                    FieldName = field,
                    OldValue = oldVal,
                    NewValue = newVal,
                    OperationType = operationType,
                    Source = "DatabaseHelper.SaveCognitiveScore",
                    UserId = userId,
                    UserName = userName,
                    UserRole = userRole,
                    ChangedAt = utcNow,
                    Remarks = null
                };

                InsertScoreAudit(audit);
            }

            // ── Notify the owning teacher when an admin edits a submitted score ──
            try
            {
                var currentUser = Global.CurrentUser;
                if (currentUser != null &&
                    !string.Equals(currentUser.Role, "Teacher", StringComparison.OrdinalIgnoreCase) &&
                    operationType == "Update")
                {
                    object teacherIdObj = ExecuteScalar(@"
                        SELECT ss.TeacherId FROM ScoreSubmissions ss
                        INNER JOIN Students s ON s.ClassId = ss.ClassId
                        WHERE s.Id = @StudentId
                          AND ss.SubjectId = @SubjectId
                          AND ss.Term = @Term
                          AND ss.AcademicYear = @AcademicYear
                        LIMIT 1",
                        new Dictionary<string, object>
                        {
                            { "@StudentId", studentId },
                            { "@SubjectId", subjectId },
                            { "@Term", term },
                            { "@AcademicYear", year }
                        });

                    if (teacherIdObj != null && teacherIdObj != DBNull.Value)
                    {
                        int teacherUserId = Convert.ToInt32(teacherIdObj);

                        object hasMobileObj = ExecuteScalar(
                            "SELECT IFNULL(HasMobileAccess, 1) FROM Users WHERE Id = @Id",
                            new Dictionary<string, object> { { "@Id", teacherUserId } });

                        bool hasMobile = hasMobileObj == null || hasMobileObj == DBNull.Value
                                         || Convert.ToInt32(hasMobileObj) == 1;

                        if (hasMobile)
                        {
                            string studentName = GetStudentNameForNotification(studentId);
                            string subjectName = GetSubjectNameForNotification(subjectId);
                            string fieldsChanged = string.Join(", ", fields);

                            InsertNotification(
                                schoolId: GetSchoolIdForStudent(studentId),
                                userId: teacherUserId,
                                type: "ScoreEdited",
                                title: "Score Updated by Admin",
                                body: $"Admin edited {studentName}'s score in {subjectName} (Term {term}). Fields: {fieldsChanged}.",
                                payloadJson: $"{{\"studentId\":{studentId},\"subjectId\":{subjectId},\"term\":{term},\"academicYear\":\"{year}\"}}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Teacher notification on admin score edit failed (non-fatal): {ex.Message}");
            }
        }

        private string GetStudentNameForNotification(int studentId)
        {
            try
            {
                var obj = ExecuteScalar("SELECT FullName FROM Students WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", studentId } });
                return obj?.ToString() ?? "a student";
            }
            catch { return "a student"; }
        }

        private string GetSubjectNameForNotification(int subjectId)
        {
            try
            {
                var obj = ExecuteScalar("SELECT SubjectName FROM Subjects WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", subjectId } });
                return obj?.ToString() ?? "a subject";
            }
            catch { return "a subject"; }
        }

        private int GetSchoolIdForStudent(int studentId)
        {
            try
            {
                var obj = ExecuteScalar("SELECT SchoolId FROM Students WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", studentId } });
                return obj == null || obj == DBNull.Value ? 0 : Convert.ToInt32(obj);
            }
            catch { return 0; }
        }

        private static string GetFieldValueAsString(SubjectScoreData score, string fieldName)
        {
            if (score == null) return null;
            switch (fieldName)
            {
                case "T1": return score.T1?.ToString();
                case "T2": return score.T2?.ToString();
                case "T3": return score.T3?.ToString();
                case "T4": return score.T4?.ToString();
                case "T5": return score.T5?.ToString();
                case "T6": return score.T6?.ToString();
                case "Exam": return score.Exam?.ToString();
                case "FinalScore": return score.FinalScore.ToString();
                case "Grade": return score.Grade ?? "";
                default: return null;
            }
        }
        #endregion


        #region User Methods

        public List<User> GetUsersBySchool(int schoolId)
        {
            var users = new List<User>();
            var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Users WHERE SchoolId = @SchoolId ORDER BY FullName", parameters);
            foreach (DataRow row in dt.Rows)
            {
                users.Add(new User
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolId = ConvertToNullableInt(row["SchoolId"]),
                    Username = ConvertToString(row["Username"]),
                    FullName = ConvertToString(row["FullName"]),
                    Role = ConvertToString(row["Role"]),
                    Phone = ConvertToString(row["Phone"]),
                    Email = ConvertToString(row["Email"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),
                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                });
            }
            return users;
        }

        public int GetUserCountBySchool(int schoolId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
                object obj = ExecuteScalar("SELECT COUNT(*) FROM Users WHERE SchoolId = @SchoolId AND (IsDeleted = 0 OR IsDeleted IS NULL)", parameters);
                return obj == null ? 0 : Convert.ToInt32(obj);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserCountBySchool error: {ex.Message}");
                return 0;
            }
        }

        public bool CanCreateUserForSchool(int schoolId)
        {
            if (schoolId <= 0) return true;
            int count = GetUserCountBySchool(schoolId);
            return count < 10;
        }

        public List<User> GetAllUsersWithSchoolInfo()
        {
            var users = new List<User>();
            DataTable dt = ExecuteQuery("SELECT u.*, s.SchoolName FROM Users u LEFT JOIN Schools s ON u.SchoolId = s.Id ORDER BY u.FullName");
            foreach (DataRow row in dt.Rows)
            {
                users.Add(new User
                {
                    Id = ConvertToInt(row["Id"]),
                    SchoolId = ConvertToNullableInt(row["SchoolId"]),
                    Username = ConvertToString(row["Username"]),
                    FullName = ConvertToString(row["FullName"]),
                    Role = ConvertToString(row["Role"]),
                    Phone = ConvertToString(row["Phone"]),
                    Email = ConvertToString(row["Email"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),
                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"]),
                    SchoolName = ConvertToString(row["SchoolName"])
                });
            }
            return users;
        }

        public int InsertUser(User user)
        {
            var checkParams = new Dictionary<string, object> { { "@Username", user.Username } };
            int exists = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM Users WHERE Username = @Username", checkParams));
            if (exists > 0) return 0;
            if (user.SchoolId.HasValue && user.SchoolId.Value > 0 && !CanCreateUserForSchool(user.SchoolId.Value)) return 0;

            string query = @"INSERT INTO Users (SchoolId, Username, PasswordHash, FullName, Role, Phone, Email, IsActive, CreatedDate, ModifiedDate) VALUES (@SchoolId, @Username, @PasswordHash, @FullName, @Role, @Phone, @Email, @IsActive, @CreatedDate, @ModifiedDate); SELECT last_insert_rowid();";
            var parameters = new Dictionary<string, object>
            {
                {"@SchoolId", user.SchoolId},
                {"@Username", user.Username},
                {"@PasswordHash", user.PasswordHash},
                {"@FullName", user.FullName},
                {"@Role", user.Role},
                {"@Phone", user.Phone ?? ""},
                {"@Email", user.Email ?? ""},
                {"@IsActive", user.IsActive},
                {"@CreatedDate", DateTime.Now},
                {"@ModifiedDate", DateTime.Now}
            };
            object result = ExecuteScalar(query, parameters);
            return Convert.ToInt32(result);
        }

        public User AuthenticateUser(string username, string password, string schoolName, string district, string town)
        {
            var schoolParams = new Dictionary<string, object> { { "@SchoolName", schoolName }, { "@District", district }, { "@Town", town } };
            object schoolIdObj = ExecuteScalar("SELECT Id FROM Schools WHERE SchoolName = @SchoolName AND District = @District AND Town = @Town AND IsActive = 1", schoolParams);
            if (schoolIdObj == null) return null;

            int schoolId = Convert.ToInt32(schoolIdObj);
            var userParams = new Dictionary<string, object> { { "@Username", username }, { "@SchoolId", schoolId } };
            DataTable dt = ExecuteQuery("SELECT * FROM Users WHERE Username = @Username COLLATE NOCASE AND SchoolId = @SchoolId AND IsActive = 1", userParams);
            if (dt.Rows.Count == 0) return null;

            DataRow row = dt.Rows[0];
            string storedHash = ConvertToString(row["PasswordHash"]);
            if (!PasswordHasher.VerifyPassword(password, storedHash)) return null;

            var updateParams = new Dictionary<string, object> { { "@Id", ConvertToInt(row["Id"]) }, { "@LastLoginDate", DateTime.Now } };
            ExecuteNonQuery("UPDATE Users SET LastLoginDate = @LastLoginDate WHERE Id = @Id", updateParams);

            return new User
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToNullableInt(row["SchoolId"]),
                Username = ConvertToString(row["Username"]),
                PasswordHash = storedHash,
                FullName = ConvertToString(row["FullName"]),
                Role = ConvertToString(row["Role"]),
                Phone = ConvertToString(row["Phone"]),
                Email = ConvertToString(row["Email"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                LastLoginDate = ConvertToDateTime(row["LastLoginDate"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };
        }

        #endregion

        #region Attendance Methods

        public AttendanceData GetAttendance(int studentId, int term, string academicYear)
        {
            var result = new AttendanceData();
            var parameters = new Dictionary<string, object> {
        { "@StudentId", studentId },
        { "@Term", term },
        { "@AcademicYear", academicYear }
    };

            EnsureAttendanceLateColumn();

            DataTable dt = ExecuteQuery("SELECT * FROM Attendance WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", parameters);
            if (dt.Rows.Count > 0)
            {
                DataRow row = dt.Rows[0];
                result.Id = ConvertToInt(row["Id"]);
                result.StudentId = ConvertToInt(row["StudentId"]);
                result.Term = ConvertToInt(row["Term"]);
                result.AcademicYear = ConvertToString(row["AcademicYear"]);
                result.DaysPresent = ConvertToInt(row["DaysPresent"]);
                result.DaysAbsent = ConvertToInt(row["DaysAbsent"]);
                result.DaysLate = ConvertToInt(row["DaysLate"]);
                result.SchoolDays = ConvertToInt(row["SchoolDays"]);
                result.Percentage = ConvertToNullableDecimal(row["Percentage"]);
            }
            return result;
        }

        public bool SaveAttendance(AttendanceData attendance)
        {
            EnsureAttendanceLateColumn();

            var checkParams = new Dictionary<string, object> {
        { "@StudentId", attendance.StudentId },
        { "@Term", attendance.Term },
        { "@AcademicYear", attendance.AcademicYear }
    };

            int count = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM Attendance WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear", checkParams));

            decimal percentage = attendance.SchoolDays > 0 ? (decimal)attendance.DaysPresent / attendance.SchoolDays * 100 : 0;

            if (count > 0)
            {
                var updateParams = new Dictionary<string, object>
        {
            { "@StudentId", attendance.StudentId },
            { "@Term", attendance.Term },
            { "@AcademicYear", attendance.AcademicYear },
            { "@DaysPresent", attendance.DaysPresent },
            { "@DaysAbsent", attendance.DaysAbsent },
            { "@DaysLate", attendance.DaysLate },
            { "@SchoolDays", attendance.SchoolDays },
            { "@Percentage", percentage },
            { "@ModifiedDate", DateTime.Now }
        };
                return ExecuteNonQuery(
                    "UPDATE Attendance SET DaysPresent = @DaysPresent, DaysAbsent = @DaysAbsent, DaysLate = @DaysLate, SchoolDays = @SchoolDays, Percentage = @Percentage, ModifiedDate = @ModifiedDate WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear",
                    updateParams) > 0;
            }
            else
            {
                var insertParams = new Dictionary<string, object>
        {
            { "@StudentId", attendance.StudentId },
            { "@Term", attendance.Term },
            { "@AcademicYear", attendance.AcademicYear },
            { "@DaysPresent", attendance.DaysPresent },
            { "@DaysAbsent", attendance.DaysAbsent },
            { "@DaysLate", attendance.DaysLate },
            { "@SchoolDays", attendance.SchoolDays },
            { "@Percentage", percentage },
            { "@CreatedDate", DateTime.Now },
            { "@ModifiedDate", DateTime.Now }
        };
                return ExecuteNonQuery(
                    "INSERT INTO Attendance (StudentId, Term, AcademicYear, DaysPresent, DaysAbsent, DaysLate, SchoolDays, Percentage, CreatedDate, ModifiedDate) VALUES (@StudentId, @Term, @AcademicYear, @DaysPresent, @DaysAbsent, @DaysLate, @SchoolDays, @Percentage, @CreatedDate, @ModifiedDate)",
                    insertParams) > 0;
            }
        }

        #endregion

        #region Bulk Class Score Methods

        public Dictionary<int, BehaviourScoreData> GetBehaviourScoresForClass(
            int classId, int term, string academicYear)
        {
            var result = new Dictionary<int, BehaviourScoreData>();
            if (classId <= 0) return result;

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@ClassId", classId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear ?? "" }
                };

                DataTable dt = ExecuteQuery(@"
                    SELECT a.*
                    FROM AffectiveScores a
                    INNER JOIN Students s ON a.StudentId = s.Id
                    WHERE s.ClassId = @ClassId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND a.Term = @Term
                      AND a.AcademicYear = @AcademicYear",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    int studentId = ConvertToInt(row["StudentId"]);
                    if (studentId <= 0) continue;

                    result[studentId] = new BehaviourScoreData
                    {
                        Term1ClassParticipationT1 = ConvertToInt(row["Term1ClassParticipationT1"]),
                        Term1ClassParticipationT2 = ConvertToInt(row["Term1ClassParticipationT2"]),
                        Term1HonestyT1 = ConvertToInt(row["Term1HonestyT1"]),
                        Term1HonestyT2 = ConvertToInt(row["Term1HonestyT2"]),
                        Term1EffortT1 = ConvertToInt(row["Term1EffortT1"]),
                        Term1EffortT2 = ConvertToInt(row["Term1EffortT2"]),
                        Term1SelfDisciplineT1 = ConvertToInt(row["Term1SelfDisciplineT1"]),
                        Term1SelfDisciplineT2 = ConvertToInt(row["Term1SelfDisciplineT2"]),
                        Term1PunctualityT1 = ConvertToInt(row["Term1PunctualityT1"]),
                        Term1PunctualityT2 = ConvertToInt(row["Term1PunctualityT2"]),
                        Term1SchoolInvolvementT1 = ConvertToInt(row["Term1SchoolInvolvementT1"]),
                        Term1SchoolInvolvementT2 = ConvertToInt(row["Term1SchoolInvolvementT2"]),
                        Term1NeatnessT1 = ConvertToInt(row["Term1NeatnessT1"]),
                        Term1NeatnessT2 = ConvertToInt(row["Term1NeatnessT2"]),
                        Term2ClassParticipationT1 = ConvertToInt(row["Term2ClassParticipationT1"]),
                        Term2ClassParticipationT2 = ConvertToInt(row["Term2ClassParticipationT2"]),
                        Term2HonestyT1 = ConvertToInt(row["Term2HonestyT1"]),
                        Term2HonestyT2 = ConvertToInt(row["Term2HonestyT2"]),
                        Term2EffortT1 = ConvertToInt(row["Term2EffortT1"]),
                        Term2EffortT2 = ConvertToInt(row["Term2EffortT2"]),
                        Term2SelfDisciplineT1 = ConvertToInt(row["Term2SelfDisciplineT1"]),
                        Term2SelfDisciplineT2 = ConvertToInt(row["Term2SelfDisciplineT2"]),
                        Term2PunctualityT1 = ConvertToInt(row["Term2PunctualityT1"]),
                        Term2PunctualityT2 = ConvertToInt(row["Term2PunctualityT2"]),
                        Term2SchoolInvolvementT1 = ConvertToInt(row["Term2SchoolInvolvementT1"]),
                        Term2SchoolInvolvementT2 = ConvertToInt(row["Term2SchoolInvolvementT2"]),
                        Term2NeatnessT1 = ConvertToInt(row["Term2NeatnessT1"]),
                        Term2NeatnessT2 = ConvertToInt(row["Term2NeatnessT2"]),
                        Term3ClassParticipationT1 = ConvertToInt(row["Term3ClassParticipationT1"]),
                        Term3ClassParticipationT2 = ConvertToInt(row["Term3ClassParticipationT2"]),
                        Term3HonestyT1 = ConvertToInt(row["Term3HonestyT1"]),
                        Term3HonestyT2 = ConvertToInt(row["Term3HonestyT2"]),
                        Term3EffortT1 = ConvertToInt(row["Term3EffortT1"]),
                        Term3EffortT2 = ConvertToInt(row["Term3EffortT2"]),
                        Term3SelfDisciplineT1 = ConvertToInt(row["Term3SelfDisciplineT1"]),
                        Term3SelfDisciplineT2 = ConvertToInt(row["Term3SelfDisciplineT2"]),
                        Term3PunctualityT1 = ConvertToInt(row["Term3PunctualityT1"]),
                        Term3PunctualityT2 = ConvertToInt(row["Term3PunctualityT2"]),
                        Term3SchoolInvolvementT1 = ConvertToInt(row["Term3SchoolInvolvementT1"]),
                        Term3SchoolInvolvementT2 = ConvertToInt(row["Term3SchoolInvolvementT2"]),
                        Term3NeatnessT1 = ConvertToInt(row["Term3NeatnessT1"]),
                        Term3NeatnessT2 = ConvertToInt(row["Term3NeatnessT2"]),
                        Term1Average = ConvertToDecimal(row["Term1Average"]),
                        Term2Average = ConvertToDecimal(row["Term2Average"]),
                        Term3Average = ConvertToDecimal(row["Term3Average"]),
                        FinalAverage = ConvertToDecimal(row["FinalAverage"]),
                        FinalGrade = ConvertToString(row["FinalGrade"]),
                        Term = term,
                        AcademicYear = academicYear ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetBehaviourScoresForClass error: {ex.Message}");
            }

            return result;
        }

        public Dictionary<int, PsychomotorScoreData> GetPsychomotorScoresForClass(
            int classId, int term, string academicYear)
        {
            var result = new Dictionary<int, PsychomotorScoreData>();
            if (classId <= 0) return result;

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@ClassId", classId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear ?? "" }
                };

                DataTable dt = ExecuteQuery(@"
                    SELECT p.*
                    FROM PsychomotorScores p
                    INNER JOIN Students s ON p.StudentId = s.Id
                    WHERE s.ClassId = @ClassId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND p.Term = @Term
                      AND p.AcademicYear = @AcademicYear",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    int studentId = ConvertToInt(row["StudentId"]);
                    if (studentId <= 0) continue;

                    result[studentId] = new PsychomotorScoreData
                    {
                        Term1IndividualSportsT1 = ConvertToInt(row["Term1IndividualSportsT1"]),
                        Term1IndividualSportsT2 = ConvertToInt(row["Term1IndividualSportsT2"]),
                        Term1TeamPlayerT1 = ConvertToInt(row["Term1TeamPlayerT1"]),
                        Term1TeamPlayerT2 = ConvertToInt(row["Term1TeamPlayerT2"]),
                        Term1AthleticsT1 = ConvertToInt(row["Term1AthleticsT1"]),
                        Term1AthleticsT2 = ConvertToInt(row["Term1AthleticsT2"]),
                        Term1DrawingT1 = ConvertToInt(row["Term1DrawingT1"]),
                        Term1DrawingT2 = ConvertToInt(row["Term1DrawingT2"]),
                        Term1HandwritingT1 = ConvertToInt(row["Term1HandwritingT1"]),
                        Term1HandwritingT2 = ConvertToInt(row["Term1HandwritingT2"]),
                        Term2IndividualSportsT1 = ConvertToInt(row["Term2IndividualSportsT1"]),
                        Term2IndividualSportsT2 = ConvertToInt(row["Term2IndividualSportsT2"]),
                        Term2TeamPlayerT1 = ConvertToInt(row["Term2TeamPlayerT1"]),
                        Term2TeamPlayerT2 = ConvertToInt(row["Term2TeamPlayerT2"]),
                        Term2AthleticsT1 = ConvertToInt(row["Term2AthleticsT1"]),
                        Term2AthleticsT2 = ConvertToInt(row["Term2AthleticsT2"]),
                        Term2DrawingT1 = ConvertToInt(row["Term2DrawingT1"]),
                        Term2DrawingT2 = ConvertToInt(row["Term2DrawingT2"]),
                        Term2HandwritingT1 = ConvertToInt(row["Term2HandwritingT1"]),
                        Term2HandwritingT2 = ConvertToInt(row["Term2HandwritingT2"]),
                        Term3IndividualSportsT1 = ConvertToInt(row["Term3IndividualSportsT1"]),
                        Term3IndividualSportsT2 = ConvertToInt(row["Term3IndividualSportsT2"]),
                        Term3TeamPlayerT1 = ConvertToInt(row["Term3TeamPlayerT1"]),
                        Term3TeamPlayerT2 = ConvertToInt(row["Term3TeamPlayerT2"]),
                        Term3AthleticsT1 = ConvertToInt(row["Term3AthleticsT1"]),
                        Term3AthleticsT2 = ConvertToInt(row["Term3AthleticsT2"]),
                        Term3DrawingT1 = ConvertToInt(row["Term3DrawingT1"]),
                        Term3DrawingT2 = ConvertToInt(row["Term3DrawingT2"]),
                        Term3HandwritingT1 = ConvertToInt(row["Term3HandwritingT1"]),
                        Term3HandwritingT2 = ConvertToInt(row["Term3HandwritingT2"]),
                        Term1Average = ConvertToDecimal(row["Term1Average"]),
                        Term2Average = ConvertToDecimal(row["Term2Average"]),
                        Term3Average = ConvertToDecimal(row["Term3Average"]),
                        FinalAverage = ConvertToDecimal(row["FinalAverage"]),
                        FinalGrade = ConvertToString(row["FinalGrade"]),
                        Term = term,
                        AcademicYear = academicYear ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPsychomotorScoresForClass error: {ex.Message}");
            }

            return result;
        }

        public Dictionary<int, AttendanceData> GetAttendanceForClass(
            int classId, int term, string academicYear)
        {
            var result = new Dictionary<int, AttendanceData>();
            if (classId <= 0) return result;

            try { EnsureAttendanceLateColumn(); } catch { }

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@ClassId", classId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear ?? "" }
                };

                DataTable dt = ExecuteQuery(@"
                    SELECT a.*
                    FROM Attendance a
                    INNER JOIN Students s ON a.StudentId = s.Id
                    WHERE s.ClassId = @ClassId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND a.Term = @Term
                      AND a.AcademicYear = @AcademicYear",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    int studentId = ConvertToInt(row["StudentId"]);
                    if (studentId <= 0) continue;

                    result[studentId] = new AttendanceData
                    {
                        Id = ConvertToInt(row["Id"]),
                        StudentId = studentId,
                        Term = ConvertToInt(row["Term"]),
                        AcademicYear = ConvertToString(row["AcademicYear"]),
                        DaysPresent = ConvertToInt(row["DaysPresent"]),
                        DaysAbsent = ConvertToInt(row["DaysAbsent"]),
                        DaysLate = row.Table.Columns.Contains("DaysLate")
                            ? ConvertToInt(row["DaysLate"]) : 0,
                        SchoolDays = ConvertToInt(row["SchoolDays"]),
                        Percentage = ConvertToNullableDecimal(row["Percentage"])
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAttendanceForClass error: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Daily Attendance Methods

        public List<AttendanceRecord> GetAttendanceRecords(int studentId, string academicYear)
        {
            var records = new List<AttendanceRecord>();
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", academicYear }
                };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM DailyAttendance WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear ORDER BY Date",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    records.Add(new AttendanceRecord
                    {
                        Id = ConvertToInt(row["Id"]),
                        StudentId = ConvertToInt(row["StudentId"]),
                        Date = ConvertToDateTime(row["Date"]),
                        Status = ConvertToString(row["Status"]),
                        AcademicYear = ConvertToString(row["AcademicYear"]),
                        Term = ConvertToNullableInt(row["Term"]),
                        Remarks = ConvertToString(row["Remarks"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAttendanceRecords error: {ex.Message}");
            }

            return records;
        }

        #endregion


        #region Report Configuration Methods

        public SchoolReportConfig GetSchoolReportConfig(int schoolId)
        {
            var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            DataTable dt = ExecuteQuery("SELECT * FROM SchoolReportConfig WHERE SchoolId = @SchoolId", parameters);
            if (dt.Rows.Count == 0) return new SchoolReportConfig { SchoolId = schoolId };
            DataRow row = dt.Rows[0];
            return new SchoolReportConfig
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ResultType = ConvertToString(row["ResultType"]) ?? "FiveTests",
                NumberOfTerms = ConvertToInt(row["NumberOfTerms"]),
                PaperOrientation = ConvertToString(row["PaperOrientation"]),
                ShowBehaviourSection = ConvertToBoolean(row["ShowBehaviourSection"]),
                ShowPsychomotorSection = ConvertToBoolean(row["ShowPsychomotorSection"]),
                ShowAttendanceSection = ConvertToBoolean(row["ShowAttendanceSection"]),
                ShowSubjectAnalysis = ConvertToBoolean(row["ShowSubjectAnalysis"]),
                HeaderLogoPath = ConvertToString(row["HeaderLogoPath"]),
                FooterText = ConvertToString(row["FooterText"])
            };
        }

        public bool SaveSchoolReportConfig(SchoolReportConfig config)
        {
            var checkParams = new Dictionary<string, object> { { "@SchoolId", config.SchoolId } };
            int count = Convert.ToInt32(ExecuteScalar("SELECT COUNT(*) FROM SchoolReportConfig WHERE SchoolId = @SchoolId", checkParams));

            if (count > 0)
            {
                var parameters = new Dictionary<string, object>
                {
                    {"@SchoolId", config.SchoolId}, {"@NumberOfTerms", config.NumberOfTerms},
                    {"@PaperOrientation", config.PaperOrientation}, {"@ShowBehaviourSection", config.ShowBehaviourSection},
                    {"@ShowPsychomotorSection", config.ShowPsychomotorSection}, {"@ShowAttendanceSection", config.ShowAttendanceSection},
                    {"@ShowSubjectAnalysis", config.ShowSubjectAnalysis}, {"@HeaderLogoPath", config.HeaderLogoPath},
                    {"@FooterText", config.FooterText}, {"@ModifiedDate", DateTime.Now}
                };
                return ExecuteNonQuery("UPDATE SchoolReportConfig SET NumberOfTerms = @NumberOfTerms, PaperOrientation = @PaperOrientation, ShowBehaviourSection = @ShowBehaviourSection, ShowPsychomotorSection = @ShowPsychomotorSection, ShowAttendanceSection = @ShowAttendanceSection, ShowSubjectAnalysis = @ShowSubjectAnalysis, HeaderLogoPath = @HeaderLogoPath, FooterText = @FooterText, ModifiedDate = @ModifiedDate WHERE SchoolId = @SchoolId", parameters) > 0;
            }
            else
            {
                var parameters = new Dictionary<string, object>
                {
                    {"@SchoolId", config.SchoolId}, {"@NumberOfTerms", config.NumberOfTerms},
                    {"@PaperOrientation", config.PaperOrientation}, {"@ShowBehaviourSection", config.ShowBehaviourSection},
                    {"@ShowPsychomotorSection", config.ShowPsychomotorSection}, {"@ShowAttendanceSection", config.ShowAttendanceSection},
                    {"@ShowSubjectAnalysis", config.ShowSubjectAnalysis}, {"@HeaderLogoPath", config.HeaderLogoPath},
                    {"@FooterText", config.FooterText}, {"@CreatedDate", DateTime.Now}, {"@ModifiedDate", DateTime.Now}
                };
                return ExecuteNonQuery("INSERT INTO SchoolReportConfig (SchoolId, NumberOfTerms, PaperOrientation, ShowBehaviourSection, ShowPsychomotorSection, ShowAttendanceSection, ShowSubjectAnalysis, HeaderLogoPath, FooterText, CreatedDate, ModifiedDate) VALUES (@SchoolId, @NumberOfTerms, @PaperOrientation, @ShowBehaviourSection, @ShowPsychomotorSection, @ShowAttendanceSection, @ShowSubjectAnalysis, @HeaderLogoPath, @FooterText, @CreatedDate, @ModifiedDate)", parameters) > 0;
            }
        }

        #endregion

        #region Backup Methods

        public List<Backup> GetBackups()
        {
            var backups = new List<Backup>();
            DataTable dt = ExecuteQuery("SELECT * FROM Backups ORDER BY BackupDate DESC");
            foreach (DataRow row in dt.Rows)
            {
                backups.Add(new Backup
                {
                    Id = ConvertToInt(row["Id"]),
                    BackupName = ConvertToString(row["BackupName"]),
                    BackupDate = ConvertToDateTime(row["BackupDate"]),
                    FileSize = ConvertToInt(row["FileSize"]),
                    BackupPath = ConvertToString(row["BackupPath"]),
                    BackupType = ConvertToString(row["BackupType"]),
                    BackupBy = ConvertToString(row["BackupBy"])
                });
            }
            return backups;
        }

        public bool BackupDatabase(string backupFolder)
        {
            string dbPath = GetDatabaseFilePath();
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return false;

            string backupFile = Path.Combine(backupFolder, $"SRMS.Desktop_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            CloseAllConnections();

            try
            {
                File.Copy(dbPath, backupFile, true);
                var fileInfo = new FileInfo(backupFile);

                if (fileInfo.Length == 0) return false;

                var parameters = new Dictionary<string, object>
                {
                    {"@BackupName", Path.GetFileName(backupFile)},
                    {"@BackupDate", DateTime.Now},
                    {"@FileSize", fileInfo.Length},
                    {"@BackupPath", backupFile},
                    {"@BackupType", "Manual"},
                    {"@BackupBy", Global.CurrentUser?.Username ?? "System"}
                };
                ExecuteNonQuery("INSERT INTO Backups (BackupName, BackupDate, FileSize, BackupPath, BackupType, BackupBy) VALUES (@BackupName, @BackupDate, @FileSize, @BackupPath, @BackupType, @BackupBy)", parameters);
                LogSystemAudit("BACKUP_CREATED", $"Created database backup '{backupFile}'.");
                PruneBackups(backupFolder);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BackupDatabase error: {ex.Message}");
                return false;
            }
        }

        private void PruneBackups(string backupFolder)
        {
            try
            {
                int keep = Math.Max(3, AppConfiguration.MaxBackupFiles);
                var files = Directory.GetFiles(backupFolder, "SRMS.Desktop_Backup_*.db")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.CreationTimeUtc)
                    .ToList();

                foreach (var file in files.Skip(keep))
                {
                    try
                    {
                        File.Delete(file.FullName);
                        ExecuteNonQuery("DELETE FROM Backups WHERE BackupPath = @Path", new Dictionary<string, object>
                        {
                            { "@Path", file.FullName }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Backup pruning skipped '{file.FullName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PruneBackups error: {ex.Message}");
            }
        }

        public bool RestoreDatabase(string backupPath)
        {
            string targetDb = GetDatabaseFilePath();
            if (string.IsNullOrEmpty(targetDb) || !File.Exists(backupPath)) return false;
            if (new FileInfo(backupPath).Length == 0) return false;
            CloseAllConnections();

            try
            {
                string safetyFolder = Path.GetDirectoryName(targetDb);
                string safetyCopy = Path.Combine(safetyFolder, $"SRMS_PreRestore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                File.Copy(targetDb, safetyCopy, true);
                File.Copy(backupPath, targetDb, true);
                LogSystemAudit("DATABASE_RESTORED", $"Restored database from '{backupPath}'. Safety copy: '{safetyCopy}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreDatabase error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Settings Methods

        public bool InsertSetting(int studentId, string settingKey, string settingValue)
        {
            try
            {
                try
                {
                    var parameters = new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@SettingKey", settingKey },
                { "@SettingValue", settingValue ?? "" },
                { "@Now", DateTime.Now }
            };

                    int affected = ExecuteNonQuery(
                        @"INSERT INTO StudentSettings (StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
                  VALUES (@StudentId, @SettingKey, @SettingValue, @Now, @Now)
                  ON CONFLICT(StudentId, SettingKey)
                  DO UPDATE SET SettingValue = @SettingValue, ModifiedDate = @Now",
                        parameters);

                    if (affected > 0) return true;
                }
                catch { }

                var checkParams = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SettingKey", settingKey }
        };
                int exists = Convert.ToInt32(ExecuteScalar(
                    "SELECT COUNT(*) FROM Settings WHERE StudentId = @StudentId AND SettingKey = @SettingKey",
                    checkParams));

                if (exists > 0)
                {
                    var parameters = new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@SettingKey", settingKey },
                { "@SettingValue", settingValue ?? "" },
                { "@ModifiedDate", DateTime.Now }
            };
                    return ExecuteNonQuery(
                        "UPDATE Settings SET SettingValue = @SettingValue, ModifiedDate = @ModifiedDate WHERE StudentId = @StudentId AND SettingKey = @SettingKey",
                        parameters) > 0;
                }
                else
                {
                    var parameters = new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@SettingKey", settingKey },
                { "@SettingValue", settingValue ?? "" },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
            };
                    return ExecuteNonQuery(
                        "INSERT INTO Settings (StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate) VALUES (@StudentId, @SettingKey, @SettingValue, @CreatedDate, @ModifiedDate)",
                        parameters) > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertSetting error: {ex.Message}");
                return false;
            }
        }

        public string GetSetting(int studentId, string settingKey)
        {
            try
            {
                try
                {
                    var parameters = new Dictionary<string, object>
            {
                { "@StudentId", studentId },
                { "@SettingKey", settingKey }
            };
                    object result = ExecuteScalar(
                        "SELECT SettingValue FROM StudentSettings WHERE StudentId = @StudentId AND SettingKey = @SettingKey LIMIT 1",
                        parameters);

                    if (result != null && result != DBNull.Value)
                        return result.ToString();
                }
                catch { }

                var legacyParams = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SettingKey", settingKey }
        };
                object legacy = ExecuteScalar(
                    "SELECT SettingValue FROM Settings WHERE StudentId = @StudentId AND SettingKey = @SettingKey LIMIT 1",
                    legacyParams);
                return legacy?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetSetting error: {ex.Message}");
                return "";
            }
        }
        public string GetSchoolSetting(int schoolId, string settingKey)
        {
            try
            {
                if (schoolId <= 0 || string.IsNullOrWhiteSpace(settingKey))
                    return "";

                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@SettingKey", settingKey }
        };

                object result = ExecuteScalar(
                    @"SELECT SettingValue
              FROM Settings
              WHERE SchoolId = @SchoolId
                AND SchoolId IS NOT NULL
                AND SettingKey = @SettingKey
              ORDER BY Id DESC
              LIMIT 1",
                    parameters);

                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetSchoolSetting error: {ex.Message}");
                return "";
            }
        }

        public bool InsertSchoolSetting(int schoolId, string settingKey, string settingValue)
        {
            try
            {
                if (schoolId <= 0 || string.IsNullOrWhiteSpace(settingKey))
                    return false;

                var checkParams = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@SettingKey", settingKey }
        };

                object existingId = ExecuteScalar(
                    @"SELECT Id FROM Settings
              WHERE SchoolId = @SchoolId
                AND SchoolId IS NOT NULL
                AND SettingKey = @SettingKey
              ORDER BY Id DESC
              LIMIT 1",
                    checkParams);

                if (existingId != null && existingId != DBNull.Value)
                {
                    int id = Convert.ToInt32(existingId);

                    try
                    {
                        ExecuteNonQuery(
                            @"DELETE FROM Settings
                      WHERE SchoolId = @SchoolId
                        AND SettingKey = @SettingKey
                        AND Id <> @Id",
                            new Dictionary<string, object>
                            {
                        { "@SchoolId", schoolId },
                        { "@SettingKey", settingKey },
                        { "@Id", id }
                            });
                    }
                    catch { }

                    return ExecuteNonQuery(
                        @"UPDATE Settings
                  SET SettingValue = @SettingValue,
                      ModifiedDate = @ModifiedDate
                  WHERE Id = @Id",
                        new Dictionary<string, object>
                        {
                    { "@Id", id },
                    { "@SettingValue", settingValue ?? "" },
                    { "@ModifiedDate", DateTime.Now }
                        }) > 0;
                }

                return ExecuteNonQuery(
                    @"INSERT INTO Settings
                (SchoolId, StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
              VALUES
                (@SchoolId, NULL, @SettingKey, @SettingValue, @CreatedDate, @ModifiedDate)",
                    new Dictionary<string, object>
                    {
                { "@SchoolId", schoolId },
                { "@SettingKey", settingKey },
                { "@SettingValue", settingValue ?? "" },
                { "@CreatedDate", DateTime.Now },
                { "@ModifiedDate", DateTime.Now }
                    }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertSchoolSetting error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Utility Methods

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public int ConvertToInt(object value) => (value == null || value == DBNull.Value) ? 0 : Convert.ToInt32(value);
        public int? ConvertToNullableInt(object value) => (value == null || value == DBNull.Value) ? (int?)null : Convert.ToInt32(value);
        public decimal ConvertToDecimal(object value) => (value == null || value == DBNull.Value) ? 0 : Convert.ToDecimal(value);
        public decimal? ConvertToNullableDecimal(object value) => (value == null || value == DBNull.Value) ? (decimal?)null : Convert.ToDecimal(value);
        public DateTime ConvertToDateTime(object value) => (value == null || value == DBNull.Value) ? DateTime.Now : Convert.ToDateTime(value);
        public bool ConvertToBoolean(object value) => value != null && value != DBNull.Value && Convert.ToBoolean(value);
        public string ConvertToString(object value) => (value == null || value == DBNull.Value) ? "" : value.ToString();

        #endregion

        #region Promotion Criteria Methods
        #region Promotion Records Methods

        public PromotionRecord GetPromotionRecord(int studentId, string academicYear)
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return null;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@AcademicYear", academicYear.Trim() }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM PromotionRecords WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear LIMIT 1",
                    parameters);

                if (dt.Rows.Count == 0) return null;
                return MapPromotionRecordRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionRecord error: {ex.Message}");
                return null;
            }
        }

        public List<PromotionRecord> GetPromotionRecordsForClass(int classId, string academicYear)
        {
            var results = new List<PromotionRecord>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return results;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@ClassId", classId },
            { "@AcademicYear", academicYear.Trim() }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM PromotionRecords WHERE ClassId = @ClassId AND AcademicYear = @AcademicYear ORDER BY Rank",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapPromotionRecordRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionRecordsForClass error: {ex.Message}");
            }

            return results;
        }

        public List<PromotionRecord> GetPromotionRecordsForSchool(int schoolId, string academicYear)
        {
            var results = new List<PromotionRecord>();
            if (schoolId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return results;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@AcademicYear", academicYear.Trim() }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM PromotionRecords WHERE SchoolId = @SchoolId AND AcademicYear = @AcademicYear ORDER BY ClassId, Rank",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapPromotionRecordRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionRecordsForSchool error: {ex.Message}");
            }

            return results;
        }

        public bool SavePromotionRecord(PromotionRecord record)
        {
            if (record == null || record.StudentId <= 0 || string.IsNullOrWhiteSpace(record.AcademicYear))
                return false;

            try
            {
                var checkParams = new Dictionary<string, object>
        {
            { "@StudentId", record.StudentId },
            { "@AcademicYear", record.AcademicYear.Trim() }
        };

                object existingId = ExecuteScalar(
                    "SELECT Id FROM PromotionRecords WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear",
                    checkParams);

                if (existingId != null && existingId != DBNull.Value)
                {
                    record.Id = Convert.ToInt32(existingId);
                    return UpdatePromotionRecordInternal(record);
                }

                return InsertPromotionRecordInternal(record);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SavePromotionRecord error: {ex.Message}");
                return false;
            }
        }

        public int SavePromotionRecordsBulk(IEnumerable<PromotionRecord> records)
        {
            if (records == null) return 0;

            var list = records.ToList();
            if (list.Count == 0) return 0;

            int saved = 0;
            SqliteConnection connection = null;
            SqliteTransaction transaction = null;

            try
            {
                connection = GetConnection();
                transaction = connection.BeginTransaction();

                foreach (var record in list)
                {
                    if (record == null || record.StudentId <= 0) continue;

                    var checkParams = new Dictionary<string, object>
            {
                { "@StudentId", record.StudentId },
                { "@AcademicYear", record.AcademicYear?.Trim() ?? "" }
            };

                    object existingId = ExecuteScalar(
                        "SELECT Id FROM PromotionRecords WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear",
                        checkParams, connection);

                    bool ok;
                    if (existingId != null && existingId != DBNull.Value)
                    {
                        record.Id = Convert.ToInt32(existingId);
                        ok = UpdatePromotionRecordInternal(record, connection);
                    }
                    else
                    {
                        ok = InsertPromotionRecordInternal(record, connection);
                    }

                    if (ok) saved++;
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SavePromotionRecordsBulk error: {ex.Message}");
                try { transaction?.Rollback(); } catch { }
                return 0;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
                if (connection != null) CloseConnection(connection);
            }

            return saved;
        }

        public (int SavedCount, int BatchId) SavePromotionBatch(
    int schoolId,
    int classId,
    string academicYear,
    IEnumerable<PromotionRecord> records,
    int? performedByUserId,
    string performedByName,
    string performedByRole)
        {
            var recordList = records?.ToList() ?? new List<PromotionRecord>();
            if (recordList.Count == 0) return (0, 0);

            int saved = 0;
            int batchId = 0;
            SqliteConnection connection = null;

            try
            {
                connection = GetConnection();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        ExecuteNonQuery(@"
                    INSERT INTO PromotionBatches
                        (SchoolId, SourceClassId, DestinationClassId,
                         SourceAcademicYear, DestinationAcademicYear, StudentCount,
                         PerformedByUserId, PerformedByName, PerformedByRole,
                         BatchTimestamp)
                    VALUES
                        (@SchoolId, @SrcId, @SrcId,
                         @Year, @Year, @Count,
                         @UserId, @Name, @Role, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@SchoolId", schoolId },
                        { "@SrcId", classId },
                        { "@Year", academicYear ?? "" },
                        { "@Count", recordList.Count },
                        { "@UserId", (object)performedByUserId ?? DBNull.Value },
                        { "@Name", performedByName ?? "" },
                        { "@Role", performedByRole ?? "" },
                        { "@Now", DateTime.Now }
                            }, connection);

                        object batchObj = ExecuteScalar("SELECT last_insert_rowid()", null, connection);
                        batchId = batchObj == null || batchObj == DBNull.Value
                            ? 0 : Convert.ToInt32(batchObj);

                        foreach (var r in recordList)
                        {
                            if (r == null || r.StudentId <= 0) continue;
                            r.SchoolId = schoolId;
                            r.ClassId = classId;
                            r.AcademicYear = (academicYear ?? "").Trim();

                            object existing = ExecuteScalar(
                                "SELECT Id FROM PromotionRecords WHERE StudentId = @Sid AND AcademicYear = @Year",
                                new Dictionary<string, object>
                                {
                            { "@Sid", r.StudentId },
                            { "@Year", r.AcademicYear }
                                }, connection);

                            bool ok;
                            if (existing != null && existing != DBNull.Value)
                            {
                                r.Id = Convert.ToInt32(existing);
                                ok = ExecuteNonQuery(@"
                            UPDATE PromotionRecords SET
                                SchoolId = @SchoolId, ClassId = @ClassId,
                                AnnualAverage = @Avg, CorePassed = @CP, OptionalPassed = @OP,
                                TotalPassed = @TP, TotalCore = @TC, TotalOptional = @TO,
                                TotalSubjects = @TS, Rank = @Rank, IsPromoted = @Promoted,
                                NextClass = @Next, Reason = @Reason,
                                CriteriaSnapshotJson = @Snap,
                                ComputedAt = @Now, ComputedByUserId = @UserId,
                                ComputedByName = @Name
                            WHERE Id = @Id",
                                    BuildPromotionRecordParams(r, performedByUserId, performedByName),
                                    connection) > 0;
                            }
                            else
                            {
                                ok = ExecuteNonQuery(@"
                            INSERT INTO PromotionRecords
                                (StudentId, SchoolId, ClassId, AcademicYear,
                                 AnnualAverage, CorePassed, OptionalPassed, TotalPassed,
                                 TotalCore, TotalOptional, TotalSubjects, Rank,
                                 IsPromoted, NextClass, Reason, CriteriaSnapshotJson,
                                 ComputedAt, ComputedByUserId, ComputedByName,
                                 IsManuallyOverridden)
                            VALUES
                                (@StudentId, @SchoolId, @ClassId, @AcademicYear,
                                 @Avg, @CP, @OP, @TP,
                                 @TC, @TO, @TS, @Rank,
                                 @Promoted, @Next, @Reason, @Snap,
                                 @Now, @UserId, @Name, 0)",
                                    BuildPromotionRecordParams(r, performedByUserId, performedByName),
                                    connection) > 0;
                            }

                            if (ok) saved++;
                        }

                        foreach (var r in recordList)
                        {
                            if (r == null || r.StudentId <= 0) continue;

                            WriteStudentSetting(connection, r.StudentId, "PromotionStatus",
                                r.IsPromoted ? "Promoted" : "Repeat");
                            WriteStudentSetting(connection, r.StudentId, "NextClass", r.NextClass ?? "");
                            WriteStudentSetting(connection, r.StudentId, "PromotionYear", r.AcademicYear ?? "");
                            WriteStudentSetting(connection, r.StudentId, "AnnualAverage",
                                r.AnnualAverage.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                            WriteStudentSetting(connection, r.StudentId, "Rank", r.Rank.ToString());
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SavePromotionBatch error: {ex.Message}");
                return (0, 0);
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }

            return (saved, batchId);
        }

        private Dictionary<string, object> BuildPromotionRecordParams(
            PromotionRecord r, int? userId, string userName)
        {
            return new Dictionary<string, object>
    {
        { "@Id", r.Id },
        { "@StudentId", r.StudentId },
        { "@SchoolId", r.SchoolId },
        { "@ClassId", r.ClassId },
        { "@AcademicYear", r.AcademicYear ?? "" },
        { "@Avg", r.AnnualAverage },
        { "@CP", r.CorePassed },
        { "@OP", r.OptionalPassed },
        { "@TP", r.TotalPassed },
        { "@TC", r.TotalCore },
        { "@TO", r.TotalOptional },
        { "@TS", r.TotalSubjects },
        { "@Rank", r.Rank },
        { "@Promoted", r.IsPromoted },
        { "@Next", r.NextClass ?? "" },
        { "@Reason", r.Reason ?? "" },
        { "@Snap", r.CriteriaSnapshotJson ?? "" },
        { "@Now", DateTime.Now },
        { "@UserId", (object)userId ?? DBNull.Value },
        { "@Name", userName ?? "" }
    };
        }

        private void WriteStudentSetting(SqliteConnection connection, int studentId,
                                         string key, string value)
        {
            try
            {
                ExecuteNonQuery(@"
            INSERT INTO StudentSettings (StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
            VALUES (@Sid, @Key, @Val, @Now, @Now)
            ON CONFLICT(StudentId, SettingKey)
            DO UPDATE SET SettingValue = @Val, ModifiedDate = @Now",
                    new Dictionary<string, object>
                    {
                { "@Sid", studentId },
                { "@Key", key },
                { "@Val", value ?? "" },
                { "@Now", DateTime.Now }
                    }, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WriteStudentSetting({key}) error: {ex.Message}");
            }
        }

        public int CountPromotionRecordsForClass(int classId, string academicYear)
        {
            try
            {
                object result = ExecuteScalar(
                    "SELECT COUNT(*) FROM PromotionRecords WHERE ClassId = @C AND AcademicYear = @Y",
                    new Dictionary<string, object>
                    {
                { "@C", classId },
                { "@Y", (academicYear ?? "").Trim() }
                    });
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CountPromotionRecordsForClass error: {ex.Message}");
                return 0;
            }
        }

        public PromotionBatch GetLastBatchForClass(int classId, string academicYear)
        {
            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT * FROM PromotionBatches
            WHERE SourceClassId = @C AND SourceAcademicYear = @Y AND RevertedAt IS NULL
            ORDER BY BatchTimestamp DESC LIMIT 1",
                    new Dictionary<string, object>
                    {
                { "@C", classId },
                { "@Y", (academicYear ?? "").Trim() }
                    });
                if (dt.Rows.Count == 0) return null;
                return new PromotionBatch
                {
                    Id = ConvertToInt(dt.Rows[0]["Id"]),
                    SchoolId = ConvertToInt(dt.Rows[0]["SchoolId"]),
                    SourceClassId = ConvertToInt(dt.Rows[0]["SourceClassId"]),
                    DestinationClassId = ConvertToInt(dt.Rows[0]["DestinationClassId"]),
                    SourceAcademicYear = ConvertToString(dt.Rows[0]["SourceAcademicYear"]),
                    DestinationAcademicYear = ConvertToString(dt.Rows[0]["DestinationAcademicYear"]),
                    StudentCount = ConvertToInt(dt.Rows[0]["StudentCount"]),
                    PerformedByName = ConvertToString(dt.Rows[0]["PerformedByName"]),
                    BatchTimestamp = ConvertToDateTime(dt.Rows[0]["BatchTimestamp"])
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLastBatchForClass error: {ex.Message}");
                return null;
            }
        }

        public bool DeletePromotionRecord(int studentId, string academicYear)
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@AcademicYear", academicYear.Trim() }
        };

                return ExecuteNonQuery(
                    "DELETE FROM PromotionRecords WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeletePromotionRecord error: {ex.Message}");
                return false;
            }
        }

        public bool DeletePromotionRecordsForClass(int classId, string academicYear)
        {
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@ClassId", classId },
            { "@AcademicYear", academicYear.Trim() }
        };

                ExecuteNonQuery(
                    "DELETE FROM PromotionRecords WHERE ClassId = @ClassId AND AcademicYear = @AcademicYear",
                    parameters);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeletePromotionRecordsForClass error: {ex.Message}");
                return false;
            }
        }

        private bool InsertPromotionRecordInternal(PromotionRecord record, SqliteConnection connection = null)
        {
            string query = @"
        INSERT INTO PromotionRecords
            (StudentId, SchoolId, ClassId, AcademicYear,
             AnnualAverage, CorePassed, OptionalPassed, TotalPassed,
             TotalCore, TotalOptional, TotalSubjects, Rank,
             IsPromoted, NextClass, Reason, CriteriaSnapshotJson,
             ComputedAt, ComputedByUserId, ComputedByName,
             IsManuallyOverridden, OverrideReason, OverriddenAt, OverriddenByUserId)
        VALUES
            (@StudentId, @SchoolId, @ClassId, @AcademicYear,
             @AnnualAverage, @CorePassed, @OptionalPassed, @TotalPassed,
             @TotalCore, @TotalOptional, @TotalSubjects, @Rank,
             @IsPromoted, @NextClass, @Reason, @CriteriaSnapshotJson,
             @ComputedAt, @ComputedByUserId, @ComputedByName,
             @IsManuallyOverridden, @OverrideReason, @OverriddenAt, @OverriddenByUserId);
        SELECT last_insert_rowid();";

            var parameters = BuildPromotionRecordParameters(record);
            object id = ExecuteScalar(query, parameters, connection);

            if (id != null && id != DBNull.Value)
            {
                record.Id = Convert.ToInt32(id);
                return true;
            }
            return false;
        }

        private bool UpdatePromotionRecordInternal(PromotionRecord record, SqliteConnection connection = null)
        {
            string query = @"
        UPDATE PromotionRecords SET
            SchoolId = @SchoolId,
            ClassId = @ClassId,
            AnnualAverage = @AnnualAverage,
            CorePassed = @CorePassed,
            OptionalPassed = @OptionalPassed,
            TotalPassed = @TotalPassed,
            TotalCore = @TotalCore,
            TotalOptional = @TotalOptional,
            TotalSubjects = @TotalSubjects,
            Rank = @Rank,
            IsPromoted = @IsPromoted,
            NextClass = @NextClass,
            Reason = @Reason,
            CriteriaSnapshotJson = @CriteriaSnapshotJson,
            ComputedAt = @ComputedAt,
            ComputedByUserId = @ComputedByUserId,
            ComputedByName = @ComputedByName,
            IsManuallyOverridden = @IsManuallyOverridden,
            OverrideReason = @OverrideReason,
            OverriddenAt = @OverriddenAt,
            OverriddenByUserId = @OverriddenByUserId
        WHERE Id = @Id";

            var parameters = BuildPromotionRecordParameters(record);
            parameters["@Id"] = record.Id;

            return ExecuteNonQuery(query, parameters, connection) > 0;
        }

        private Dictionary<string, object> BuildPromotionRecordParameters(PromotionRecord r)
        {
            return new Dictionary<string, object>
    {
        { "@StudentId", r.StudentId },
        { "@SchoolId", r.SchoolId },
        { "@ClassId", r.ClassId },
        { "@AcademicYear", r.AcademicYear?.Trim() ?? "" },
        { "@AnnualAverage", r.AnnualAverage },
        { "@CorePassed", r.CorePassed },
        { "@OptionalPassed", r.OptionalPassed },
        { "@TotalPassed", r.TotalPassed },
        { "@TotalCore", r.TotalCore },
        { "@TotalOptional", r.TotalOptional },
        { "@TotalSubjects", r.TotalSubjects },
        { "@Rank", r.Rank },
        { "@IsPromoted", r.IsPromoted },
        { "@NextClass", r.NextClass ?? "" },
        { "@Reason", r.Reason ?? "" },
        { "@CriteriaSnapshotJson", r.CriteriaSnapshotJson ?? "" },
        { "@ComputedAt", r.ComputedAt == default ? DateTime.Now : r.ComputedAt },
        { "@ComputedByUserId", r.ComputedByUserId },
        { "@ComputedByName", r.ComputedByName ?? "" },
        { "@IsManuallyOverridden", r.IsManuallyOverridden },
        { "@OverrideReason", r.OverrideReason ?? "" },
        { "@OverriddenAt", r.OverriddenAt.HasValue ? (object)r.OverriddenAt.Value : DBNull.Value },
        { "@OverriddenByUserId", r.OverriddenByUserId }
    };
        }

        private PromotionRecord MapPromotionRecordRow(DataRow row)
        {
            if (row == null) return null;

            return new PromotionRecord
            {
                Id = ConvertToInt(row["Id"]),
                StudentId = ConvertToInt(row["StudentId"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ClassId = ConvertToInt(row["ClassId"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                AnnualAverage = ConvertToDecimal(row["AnnualAverage"]),
                CorePassed = ConvertToInt(row["CorePassed"]),
                OptionalPassed = ConvertToInt(row["OptionalPassed"]),
                TotalPassed = ConvertToInt(row["TotalPassed"]),
                TotalCore = ConvertToInt(row["TotalCore"]),
                TotalOptional = ConvertToInt(row["TotalOptional"]),
                TotalSubjects = ConvertToInt(row["TotalSubjects"]),
                Rank = ConvertToInt(row["Rank"]),
                IsPromoted = ConvertToBoolean(row["IsPromoted"]),
                NextClass = ConvertToString(row["NextClass"]),
                Reason = ConvertToString(row["Reason"]),
                CriteriaSnapshotJson = ConvertToString(row["CriteriaSnapshotJson"]),
                ComputedAt = ConvertToDateTime(row["ComputedAt"]),
                ComputedByUserId = row["ComputedByUserId"] == DBNull.Value ? (int?)null : ConvertToInt(row["ComputedByUserId"]),
                ComputedByName = ConvertToString(row["ComputedByName"]),
                IsManuallyOverridden = ConvertToBoolean(row["IsManuallyOverridden"]),
                OverrideReason = ConvertToString(row["OverrideReason"]),
                OverriddenAt = row["OverriddenAt"] == DBNull.Value ? (DateTime?)null : ConvertToDateTime(row["OverriddenAt"]),
                OverriddenByUserId = row["OverriddenByUserId"] == DBNull.Value ? (int?)null : ConvertToInt(row["OverriddenByUserId"])
            };
        }

        #endregion
        public SchoolPromotionCriteria GetSchoolPromotionCriteria(int schoolId, int? classId = null)
        {
            if (classId.HasValue)
            {
                var classParams = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@ClassId", classId.Value }
                };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId AND ClassId = @ClassId",
                    classParams);

                if (dt.Rows.Count > 0)
                {
                    return MapPromotionCriteriaRow(dt.Rows[0]);
                }
            }

            var schoolParams = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            DataTable schoolDt = ExecuteQuery(
                "SELECT * FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId AND ClassId IS NULL",
                schoolParams);

            if (schoolDt.Rows.Count > 0)
            {
                return MapPromotionCriteriaRow(schoolDt.Rows[0]);
            }

            return null;
        }

        public SchoolPromotionCriteria GetSchoolPromotionCriteria(int schoolId)
        {
            return GetSchoolPromotionCriteria(schoolId, null);
        }

        public List<SchoolPromotionCriteria> GetAllSchoolPromotionCriteria(int schoolId)
        {
            var results = new List<SchoolPromotionCriteria>();
            var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
            DataTable dt = ExecuteQuery(
                "SELECT * FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId ORDER BY ClassId NULLS FIRST",
                parameters);

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapPromotionCriteriaRow(row));
            }

            return results;
        }

        public bool SaveSchoolPromotionCriteria(SchoolPromotionCriteria criteria)
        {
            if (criteria == null) return false;

            var checkParams = new Dictionary<string, object>
            {
                { "@SchoolId", criteria.SchoolId },
                { "@ClassId", criteria.ClassId }
            };
            int exists = Convert.ToInt32(ExecuteScalar(
                "SELECT COUNT(*) FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId AND (ClassId = @ClassId OR (ClassId IS NULL AND @ClassId IS NULL))",
                checkParams));

            if (exists > 0)
            {
                var updateParams = new Dictionary<string, object>
    {
        { "@SchoolId", criteria.SchoolId },
        { "@ClassId", criteria.ClassId },
        { "@CriteriaType", criteria.CriteriaType },
        { "@MinAveragePercentage", criteria.MinAveragePercentage },
        { "@MinCoreSubjectsPassed", criteria.MinCoreSubjectsPassed },
        { "@MinOptionalSubjectsPassed", criteria.MinOptionalSubjectsPassed },
        { "@MinTotalSubjectsPassed", criteria.MinTotalSubjectsPassed },
        { "@CoreSubjectPassMark", criteria.CoreSubjectPassMark },
        { "@OptionalSubjectPassMark", criteria.OptionalSubjectPassMark },
        { "@TotalPassMark", criteria.TotalPassMark },
        { "@RequireEnglishOrMaths", criteria.RequireEnglishOrMaths },
        { "@EnglishMathsPassMark", criteria.EnglishMathsPassMark },
        { "@IncludeAttendance", criteria.IncludeAttendance },
        { "@MinAttendancePercentage", criteria.MinAttendancePercentage },
        { "@ModifiedDate", DateTime.Now }
    };
                return ExecuteNonQuery(
                    @"UPDATE SchoolPromotionCriteria SET 
            CriteriaType = @CriteriaType,
            MinAveragePercentage = @MinAveragePercentage,
            MinCoreSubjectsPassed = @MinCoreSubjectsPassed,
            MinOptionalSubjectsPassed = @MinOptionalSubjectsPassed,
            MinTotalSubjectsPassed = @MinTotalSubjectsPassed,
            CoreSubjectPassMark = @CoreSubjectPassMark,
            OptionalSubjectPassMark = @OptionalSubjectPassMark,
            TotalPassMark = @TotalPassMark,
            RequireEnglishOrMaths = @RequireEnglishOrMaths,
            EnglishMathsPassMark = @EnglishMathsPassMark,
            IncludeAttendance = @IncludeAttendance,
            MinAttendancePercentage = @MinAttendancePercentage,
            ModifiedDate = @ModifiedDate
          WHERE SchoolId = @SchoolId AND (ClassId = @ClassId OR (ClassId IS NULL AND @ClassId IS NULL))",
                    updateParams) > 0;
            }
            else
            {
                var insertParams = new Dictionary<string, object>
    {
        { "@SchoolId", criteria.SchoolId },
        { "@ClassId", criteria.ClassId },
        { "@CriteriaType", criteria.CriteriaType },
        { "@MinAveragePercentage", criteria.MinAveragePercentage },
        { "@MinCoreSubjectsPassed", criteria.MinCoreSubjectsPassed },
        { "@MinOptionalSubjectsPassed", criteria.MinOptionalSubjectsPassed },
        { "@MinTotalSubjectsPassed", criteria.MinTotalSubjectsPassed },
        { "@CoreSubjectPassMark", criteria.CoreSubjectPassMark },
        { "@OptionalSubjectPassMark", criteria.OptionalSubjectPassMark },
        { "@TotalPassMark", criteria.TotalPassMark },
        { "@RequireEnglishOrMaths", criteria.RequireEnglishOrMaths },
        { "@EnglishMathsPassMark", criteria.EnglishMathsPassMark },
        { "@IncludeAttendance", criteria.IncludeAttendance },
        { "@MinAttendancePercentage", criteria.MinAttendancePercentage },
        { "@CreatedDate", DateTime.Now },
        { "@ModifiedDate", DateTime.Now }
    };
                return ExecuteNonQuery(
                    @"INSERT INTO SchoolPromotionCriteria 
            (SchoolId, ClassId, CriteriaType, MinAveragePercentage, MinCoreSubjectsPassed,
             MinOptionalSubjectsPassed, MinTotalSubjectsPassed, CoreSubjectPassMark,
             OptionalSubjectPassMark, TotalPassMark,
             RequireEnglishOrMaths, EnglishMathsPassMark,
             IncludeAttendance, MinAttendancePercentage,
             CreatedDate, ModifiedDate)
          VALUES 
            (@SchoolId, @ClassId, @CriteriaType, @MinAveragePercentage, @MinCoreSubjectsPassed,
             @MinOptionalSubjectsPassed, @MinTotalSubjectsPassed, @CoreSubjectPassMark,
             @OptionalSubjectPassMark, @TotalPassMark,
             @RequireEnglishOrMaths, @EnglishMathsPassMark,
             @IncludeAttendance, @MinAttendancePercentage,
             @CreatedDate, @ModifiedDate)",
                    insertParams) > 0;
            }
        }

        public bool DeleteSchoolPromotionCriteria(int schoolId, int? classId = null)
        {
            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", schoolId },
                { "@ClassId", classId }
            };
            return ExecuteNonQuery(
                "DELETE FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId AND (ClassId = @ClassId OR (ClassId IS NULL AND @ClassId IS NULL))",
                parameters) > 0;
        }

        private SchoolPromotionCriteria MapPromotionCriteriaRow(DataRow row)
        {
            if (row == null) return null;

            bool hasEmFlag = row.Table.Columns.Contains("RequireEnglishOrMaths");
            bool hasEmMark = row.Table.Columns.Contains("EnglishMathsPassMark");

            return new SchoolPromotionCriteria
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ClassId = row["ClassId"] == DBNull.Value ? (int?)null : ConvertToInt(row["ClassId"]),
                CriteriaType = ConvertToString(row["CriteriaType"]),
                MinAveragePercentage = row["MinAveragePercentage"] == DBNull.Value ? (decimal?)null : ConvertToDecimal(row["MinAveragePercentage"]),
                MinCoreSubjectsPassed = row["MinCoreSubjectsPassed"] == DBNull.Value ? (int?)null : ConvertToInt(row["MinCoreSubjectsPassed"]),
                MinOptionalSubjectsPassed = row["MinOptionalSubjectsPassed"] == DBNull.Value ? (int?)null : ConvertToInt(row["MinOptionalSubjectsPassed"]),
                MinTotalSubjectsPassed = row["MinTotalSubjectsPassed"] == DBNull.Value ? (int?)null : ConvertToInt(row["MinTotalSubjectsPassed"]),
                CoreSubjectPassMark = ConvertToDecimal(row["CoreSubjectPassMark"]),
                OptionalSubjectPassMark = ConvertToDecimal(row["OptionalSubjectPassMark"]),
                TotalPassMark = ConvertToDecimal(row["TotalPassMark"]),

                RequireEnglishOrMaths = hasEmFlag && ConvertToBoolean(row["RequireEnglishOrMaths"]),
                EnglishMathsPassMark = hasEmMark ? ConvertToDecimal(row["EnglishMathsPassMark"]) : 50m,

                IncludeAttendance = ConvertToBoolean(row["IncludeAttendance"]),
                MinAttendancePercentage = ConvertToDecimal(row["MinAttendancePercentage"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };
        }

        #endregion

        #region Promotion Criteria Enhanced Methods

        public Class GetClassByGroupName(int schoolId, string groupName)
        {
            var parameters = new Dictionary<string, object>
            {
                { "@SchoolId", schoolId },
                { "@ClassName", groupName }
            };

            DataTable dt = ExecuteQuery(
                "SELECT * FROM Classes WHERE SchoolId = @SchoolId AND ClassName = @ClassName AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                parameters);

            if (dt.Rows.Count == 0) return null;

            DataRow row = dt.Rows[0];
            return new Class
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                ClassName = ConvertToString(row["ClassName"]),
                ClassLevel = ConvertToString(row["ClassLevel"]),
                Stream = ConvertToString(row["Stream"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                ClassTeacherName = ConvertToString(row["ClassTeacherName"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };
        }

        public SchoolPromotionCriteria GetSchoolPromotionCriteriaByGroupName(int schoolId, string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return null;

            var classObj = GetClassByGroupName(schoolId, groupName);

            if (classObj != null)
            {
                var classParams = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@ClassId", classObj.Id }
                };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM SchoolPromotionCriteria WHERE SchoolId = @SchoolId AND ClassId = @ClassId",
                    classParams);

                if (dt.Rows.Count > 0)
                {
                    var criteria = MapPromotionCriteriaRow(dt.Rows[0]);
                    criteria.ClassName = groupName;
                    return criteria;
                }
            }

            return null;
        }

        public bool DeleteSchoolPromotionCriteriaByGroupName(int schoolId, string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return DeleteSchoolPromotionCriteria(schoolId, null);
            }

            var classObj = GetClassByGroupName(schoolId, groupName);

            if (classObj != null)
            {
                return DeleteSchoolPromotionCriteria(schoolId, classObj.Id);
            }

            return false;
        }

        #endregion


        #region Special Case Methods

        public StudentSpecialCase GetStudentSpecialCase(int studentId, string academicYear)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", academicYear }
                };
                var dt = ExecuteQuery("SELECT * FROM StudentSpecialCase WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear", parameters);

                if (dt.Rows.Count == 0) return null;

                var row = dt.Rows[0];
                return new StudentSpecialCase
                {
                    Id = ConvertToInt(row["Id"]),
                    StudentId = ConvertToInt(row["StudentId"]),
                    AcademicYear = ConvertToString(row["AcademicYear"]),
                    SelectedTests = ConvertToString(row["SelectedTests"]),
                    Term1Formula = ConvertToString(row["Term1Formula"]),
                    Term2Formula = ConvertToString(row["Term2Formula"]),
                    Term3Formula = ConvertToString(row["Term3Formula"]),
                    Percent60Formula = ConvertToString(row["Percent60Formula"]),
                    AnnualMeanFormula = row["AnnualMeanFormula"] == DBNull.Value ? null : ConvertToString(row["AnnualMeanFormula"]),
                    IsSSSI = row["IsSSSI"] != DBNull.Value && ConvertToBoolean(row["IsSSSI"]),
                    ResultType = row["ResultType"] == DBNull.Value ? "FiveTests" : ConvertToString(row["ResultType"]),
                    FinalScoreFormula = ConvertToString(row["FinalScoreFormula"]),
                    IsActive = ConvertToBoolean(row["IsActive"]),

                    CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                    ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentSpecialCase error: {ex.Message}");
                return null;
            }
        }

        public bool SaveStudentSpecialCase(StudentSpecialCase config)
        {
            try
            {
                EnsureStudentSpecialCaseColumns(null);

                var checkParams = new Dictionary<string, object>
                {
                    { "@StudentId", config.StudentId },
                    { "@AcademicYear", config.AcademicYear }
                };
                int exists = ConvertToInt(ExecuteScalar(
                    "SELECT COUNT(*) FROM StudentSpecialCase WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear",
                    checkParams));

                if (exists > 0)
                {
                    string query = @"
                        UPDATE StudentSpecialCase SET 
                            SelectedTests = @SelectedTests,
                            Term1Formula = @Term1Formula,
                            Term2Formula = @Term2Formula,
                            Term3Formula = @Term3Formula,
                            Percent60Formula = @Percent60Formula,
                            AnnualMeanFormula = @AnnualMeanFormula,
                            FinalScoreFormula = @FinalScoreFormula,
                            IsActive = @IsActive,
                            IsSSSI = @IsSSSI,
                            ResultType = @ResultType,
                            ModifiedDate = @ModifiedDate
                            WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear";

                    var parameters = GetSpecialCaseParameters(config);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
                else
                {
                    string query = @"
                        INSERT INTO StudentSpecialCase 
                        (StudentId, AcademicYear, SelectedTests, Term1Formula, Term2Formula, Term3Formula, 
                         Percent60Formula, AnnualMeanFormula, FinalScoreFormula, IsActive, IsSSSI, ResultType, CreatedDate, ModifiedDate)
                    VALUES 
                    (@StudentId, @AcademicYear, @SelectedTests, @Term1Formula, @Term2Formula, @Term3Formula,
                     @Percent60Formula, @AnnualMeanFormula, @FinalScoreFormula, @IsActive, @IsSSSI, @ResultType, @CreatedDate, @ModifiedDate)";

                    var parameters = GetSpecialCaseParameters(config);
                    parameters.Add("@CreatedDate", DateTime.Now);
                    return ExecuteNonQuery(query, parameters) > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveStudentSpecialCase error: {ex.Message}");
                return false;
            }
        }

        public bool DeleteStudentSpecialCase(int studentId, string academicYear)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", academicYear }
                };
                return ExecuteNonQuery("DELETE FROM StudentSpecialCase WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteStudentSpecialCase error: {ex.Message}");
                return false;
            }
        }

        public bool DeleteStudentSpecialCasePermanently(int studentId, string academicYear)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", academicYear }
                };
                return ExecuteNonQuery("DELETE FROM StudentSpecialCase WHERE StudentId = @StudentId AND AcademicYear = @AcademicYear", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteStudentSpecialCasePermanently error: {ex.Message}");
                return false;
            }
        }

        public List<StudentSpecialCase> GetAllStudentSpecialCases(int schoolId)
        {
            var results = new List<StudentSpecialCase>();
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT sc.* FROM StudentSpecialCase sc
                    JOIN Students s ON sc.StudentId = s.Id
                    WHERE s.SchoolId = @SchoolId
                    ORDER BY sc.ModifiedDate DESC",
                    new Dictionary<string, object> { { "@SchoolId", schoolId } });

                foreach (DataRow row in dt.Rows)
                {
                    results.Add(new StudentSpecialCase
                    {
                        Id = ConvertToInt(row["Id"]),
                        StudentId = ConvertToInt(row["StudentId"]),
                        AcademicYear = ConvertToString(row["AcademicYear"]),
                        SelectedTests = ConvertToString(row["SelectedTests"]),
                        Term1Formula = ConvertToString(row["Term1Formula"]),
                        Term2Formula = ConvertToString(row["Term2Formula"]),
                        Term3Formula = ConvertToString(row["Term3Formula"]),
                        Percent60Formula = ConvertToString(row["Percent60Formula"]),
                        AnnualMeanFormula = row["AnnualMeanFormula"] == DBNull.Value ? null : ConvertToString(row["AnnualMeanFormula"]),
                        IsSSSI = row["IsSSSI"] != DBNull.Value && ConvertToBoolean(row["IsSSSI"]),
                        ResultType = row["ResultType"] == DBNull.Value ? "FiveTests" : ConvertToString(row["ResultType"]),
                        FinalScoreFormula = ConvertToString(row["FinalScoreFormula"]),
                        IsActive = ConvertToBoolean(row["IsActive"]),
                        CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                        ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllStudentSpecialCases error: {ex.Message}");
            }
            return results;
        }

        private Dictionary<string, object> GetSpecialCaseParameters(StudentSpecialCase config)
        {
            return new Dictionary<string, object>
            {
                { "@StudentId", config.StudentId },
                { "@AcademicYear", config.AcademicYear },
                { "@SelectedTests", config.SelectedTests ?? "[]" },
                { "@Term1Formula", config.Term1Formula ?? "" },
                { "@Term2Formula", config.Term2Formula ?? "" },
                { "@Term3Formula", config.Term3Formula ?? "" },
                { "@Percent60Formula", config.Percent60Formula ?? "" },
                { "@AnnualMeanFormula", config.AnnualMeanFormula ?? "" },
                { "@FinalScoreFormula", config.FinalScoreFormula ?? "" },
                { "@IsSSSI", config.IsSSSI },
                { "@ResultType", config.ResultType ?? "FiveTests" },
                { "@IsActive", config.IsActive },
                { "@ModifiedDate", DateTime.Now }
            };
        }

        #endregion

        #region BECE Candidate Records

        public void EnsureBECECandidateRecordsTable()
        {
            try
            {
                ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS BECECandidateRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentId INTEGER NOT NULL,
                    SchoolId INTEGER NOT NULL,
                    AcademicYear TEXT,
                    WaecCode TEXT,
                    SerialNumber TEXT,
                    IndexNumber TEXT,
                    ExamMonth TEXT,
                    ExamYear INTEGER,
                    CARTeacherName TEXT,
                    PrincipalName TEXT,
                    ParentName TEXT,
                    PhotoPath TEXT,
                    CoreSubjects TEXT,
                    OptionalSubjects TEXT,
                    SelectiveSubjects TEXT,
                    IsRemoved INTEGER DEFAULT 0,
                    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                )");

                ExecuteNonQuery(
                    "CREATE UNIQUE INDEX IF NOT EXISTS idx_bece_candidate_unique ON BECECandidateRecords(StudentId, AcademicYear)");

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_bece_candidate_school ON BECECandidateRecords(SchoolId, AcademicYear)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureBECECandidateRecordsTable error: {ex.Message}");
            }
        }

        public List<BECECandidateRecord> GetBECECandidateRecords(int schoolId, string academicYear)
        {
            var list = new List<BECECandidateRecord>();
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@AcademicYear", (academicYear ?? "").Trim() }
                };

                DataTable dt = ExecuteQuery(
                    @"SELECT * FROM BECECandidateRecords
                      WHERE SchoolId = @SchoolId
                        AND TRIM(IFNULL(AcademicYear,'')) = @AcademicYear
                        AND (IsRemoved = 0 OR IsRemoved IS NULL)",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    list.Add(MapToBECECandidateRecord(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetBECECandidateRecords error: {ex.Message}");
            }
            return list;
        }

        public bool SaveBECECandidateRecord(BECECandidateRecord record)
        {
            if (record == null) return false;

            try
            {
                object existing = ExecuteScalar(
                    @"SELECT Id FROM BECECandidateRecords
                      WHERE StudentId = @StudentId AND TRIM(IFNULL(AcademicYear,'')) = @AcademicYear",
                    new Dictionary<string, object>
                    {
                        { "@StudentId", record.StudentId },
                        { "@AcademicYear", (record.AcademicYear ?? "").Trim() }
                    });

                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", record.StudentId },
                    { "@SchoolId", record.SchoolId },
                    { "@AcademicYear", record.AcademicYear ?? "" },
                    { "@WaecCode", record.WaecCode ?? "" },
                    { "@SerialNumber", record.SerialNumber ?? "" },
                    { "@IndexNumber", record.IndexNumber ?? "" },
                    { "@ExamMonth", record.ExamMonth ?? "" },
                    { "@ExamYear", record.ExamYear },
                    { "@CARTeacherName", record.CARTeacherName ?? "" },
                    { "@PrincipalName", record.PrincipalName ?? "" },
                    { "@ParentName", record.ParentName ?? "" },
                    { "@PhotoPath", record.PhotoPath ?? "" },
                    { "@CoreSubjects", record.CoreSubjects ?? "" },
                    { "@OptionalSubjects", record.OptionalSubjects ?? "" },
                    { "@SelectiveSubjects", record.SelectiveSubjects ?? "" },
                    { "@IsRemoved", record.IsRemoved ? 1 : 0 },
                    { "@ModifiedDate", DateTime.Now }
                };

                if (existing != null && existing != DBNull.Value)
                {
                    parameters["@Id"] = Convert.ToInt32(existing);
                    return ExecuteNonQuery(@"UPDATE BECECandidateRecords SET
                        WaecCode = @WaecCode, SerialNumber = @SerialNumber, IndexNumber = @IndexNumber,
                        ExamMonth = @ExamMonth, ExamYear = @ExamYear,
                        CARTeacherName = @CARTeacherName, PrincipalName = @PrincipalName,
                        ParentName = @ParentName, PhotoPath = @PhotoPath,
                        CoreSubjects = @CoreSubjects, OptionalSubjects = @OptionalSubjects,
                        SelectiveSubjects = @SelectiveSubjects, IsRemoved = @IsRemoved,
                        ModifiedDate = @ModifiedDate
                        WHERE Id = @Id", parameters) > 0;
                }
                else
                {
                    parameters["@CreatedDate"] = DateTime.Now;
                    return ExecuteNonQuery(@"INSERT INTO BECECandidateRecords
                        (StudentId, SchoolId, AcademicYear, WaecCode, SerialNumber, IndexNumber,
                         ExamMonth, ExamYear, CARTeacherName, PrincipalName, ParentName, PhotoPath,
                         CoreSubjects, OptionalSubjects, SelectiveSubjects, IsRemoved,
                         CreatedDate, ModifiedDate)
                        VALUES
                        (@StudentId, @SchoolId, @AcademicYear, @WaecCode, @SerialNumber, @IndexNumber,
                         @ExamMonth, @ExamYear, @CARTeacherName, @PrincipalName, @ParentName, @PhotoPath,
                         @CoreSubjects, @OptionalSubjects, @SelectiveSubjects, @IsRemoved,
                         @CreatedDate, @ModifiedDate)", parameters) > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveBECECandidateRecord error: {ex.Message}");
                return false;
            }
        }

        public bool MarkBECECandidateRemoved(int studentId, string academicYear)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", (academicYear ?? "").Trim() },
                    { "@ModifiedDate", DateTime.Now }
                };
                return ExecuteNonQuery(
                    @"UPDATE BECECandidateRecords
                      SET IsRemoved = 1, ModifiedDate = @ModifiedDate
                      WHERE StudentId = @StudentId AND TRIM(IFNULL(AcademicYear,'')) = @AcademicYear",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarkBECECandidateRemoved error: {ex.Message}");
                return false;
            }
        }

        public bool RestoreBECECandidate(int studentId, string academicYear)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@AcademicYear", (academicYear ?? "").Trim() },
                    { "@ModifiedDate", DateTime.Now }
                };
                return ExecuteNonQuery(
                    @"UPDATE BECECandidateRecords
                      SET IsRemoved = 0, ModifiedDate = @ModifiedDate
                      WHERE StudentId = @StudentId AND TRIM(IFNULL(AcademicYear,'')) = @AcademicYear",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreBECECandidate error: {ex.Message}");
                return false;
            }
        }

        private BECECandidateRecord MapToBECECandidateRecord(DataRow row)
        {
            return new BECECandidateRecord
            {
                Id = ConvertToInt(row["Id"]),
                StudentId = ConvertToInt(row["StudentId"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                WaecCode = ConvertToString(row["WaecCode"]),
                SerialNumber = ConvertToString(row["SerialNumber"]),
                IndexNumber = ConvertToString(row["IndexNumber"]),
                ExamMonth = ConvertToString(row["ExamMonth"]),
                ExamYear = ConvertToInt(row["ExamYear"]),
                CARTeacherName = ConvertToString(row["CARTeacherName"]),
                PrincipalName = ConvertToString(row["PrincipalName"]),
                ParentName = ConvertToString(row["ParentName"]),
                PhotoPath = ConvertToString(row["PhotoPath"]),
                CoreSubjects = ConvertToString(row["CoreSubjects"]),
                OptionalSubjects = ConvertToString(row["OptionalSubjects"]),
                SelectiveSubjects = ConvertToString(row["SelectiveSubjects"]),
                IsRemoved = ConvertToBoolean(row["IsRemoved"])
            };
        }

        #endregion
        #region Score Audit Log Methods

        public bool InsertScoreAudit(ScoreAuditLog log)
        {
            if (log == null) return false;
            if (log.StudentId <= 0 || log.SubjectId <= 0) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", log.StudentId },
            { "@SubjectId", log.SubjectId },
            { "@Term", log.Term },
            { "@AcademicYear", log.AcademicYear?.Trim() ?? "" },
            { "@FieldName", log.FieldName ?? "" },
            { "@OldValue", (object)log.OldValue ?? DBNull.Value },
            { "@NewValue", (object)log.NewValue ?? DBNull.Value },
            { "@OperationType", log.OperationType ?? "Update" },
            { "@Source", (object)log.Source ?? DBNull.Value },
            { "@UserId", log.UserId },
            { "@UserName", (object)log.UserName ?? DBNull.Value },
            { "@UserRole", (object)log.UserRole ?? DBNull.Value },
            { "@ChangedAt", log.ChangedAt == default ? DateTime.UtcNow : log.ChangedAt },
            { "@Remarks", (object)log.Remarks ?? DBNull.Value }
        };

                string query = @"
            INSERT INTO ScoreAuditLog
                (StudentId, SubjectId, Term, AcademicYear, FieldName,
                 OldValue, NewValue, OperationType, Source,
                 UserId, UserName, UserRole, ChangedAt, Remarks)
            VALUES
                (@StudentId, @SubjectId, @Term, @AcademicYear, @FieldName,
                 @OldValue, @NewValue, @OperationType, @Source,
                 @UserId, @UserName, @UserRole, @ChangedAt, @Remarks)";

                return ExecuteNonQuery(query, parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertScoreAudit error (non-fatal): {ex.Message}");
                return false;
            }
        }

        public List<ScoreAuditLog> GetScoreAuditForStudent(int studentId, string academicYear = null, int limit = 500)
        {
            var results = new List<ScoreAuditLog>();
            if (studentId <= 0) return results;

            try
            {
                var parameters = new Dictionary<string, object> { { "@StudentId", studentId } };
                string query = "SELECT * FROM ScoreAuditLog WHERE StudentId = @StudentId";

                if (!string.IsNullOrWhiteSpace(academicYear))
                {
                    query += " AND AcademicYear = @AcademicYear";
                    parameters["@AcademicYear"] = academicYear.Trim();
                }

                query += " ORDER BY ChangedAt DESC LIMIT @Limit";
                parameters["@Limit"] = limit > 0 ? limit : 500;

                DataTable dt = ExecuteQuery(query, parameters);
                foreach (DataRow row in dt.Rows)
                    results.Add(MapScoreAuditRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScoreAuditForStudent error: {ex.Message}");
            }

            return results;
        }

        public List<ScoreAuditLog> GetScoreAuditForSubject(int subjectId, int term, string academicYear, int limit = 500)
        {
            var results = new List<ScoreAuditLog>();
            if (subjectId <= 0) return results;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SubjectId", subjectId },
            { "@Term", term },
            { "@AcademicYear", academicYear?.Trim() ?? "" },
            { "@Limit", limit > 0 ? limit : 500 }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM ScoreAuditLog WHERE SubjectId = @SubjectId AND Term = @Term AND AcademicYear = @AcademicYear ORDER BY ChangedAt DESC LIMIT @Limit",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapScoreAuditRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScoreAuditForSubject error: {ex.Message}");
            }

            return results;
        }

        public List<ScoreAuditLog> GetScoreAuditByDateRange(DateTime fromUtc, DateTime toUtc, int limit = 5000)
        {
            var results = new List<ScoreAuditLog>();

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@From", fromUtc },
            { "@To", toUtc },
            { "@Limit", limit > 0 ? limit : 5000 }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM ScoreAuditLog WHERE ChangedAt >= @From AND ChangedAt <= @To ORDER BY ChangedAt DESC LIMIT @Limit",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapScoreAuditRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScoreAuditByDateRange error: {ex.Message}");
            }

            return results;
        }

        public List<ScoreAuditLog> GetRecentScoreAudit(int limit = 200)
        {
            var results = new List<ScoreAuditLog>();

            try
            {
                var parameters = new Dictionary<string, object> { { "@Limit", limit > 0 ? limit : 200 } };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM ScoreAuditLog ORDER BY ChangedAt DESC LIMIT @Limit",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapScoreAuditRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetRecentScoreAudit error: {ex.Message}");
            }

            return results;
        }

        public int GetScoreAuditCountForStudent(int studentId, string academicYear = null)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@StudentId", studentId } };
                string query = "SELECT COUNT(*) FROM ScoreAuditLog WHERE StudentId = @StudentId";

                if (!string.IsNullOrWhiteSpace(academicYear))
                {
                    query += " AND AcademicYear = @AcademicYear";
                    parameters["@AcademicYear"] = academicYear.Trim();
                }

                object result = ExecuteScalar(query, parameters);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScoreAuditCountForStudent error: {ex.Message}");
                return 0;
            }
        }

        private ScoreAuditLog MapScoreAuditRow(DataRow row)
        {
            if (row == null) return null;

            return new ScoreAuditLog
            {
                Id = ConvertToInt(row["Id"]),
                StudentId = ConvertToInt(row["StudentId"]),
                SubjectId = ConvertToInt(row["SubjectId"]),
                Term = ConvertToInt(row["Term"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                FieldName = ConvertToString(row["FieldName"]),
                OldValue = row["OldValue"] == DBNull.Value ? null : ConvertToString(row["OldValue"]),
                NewValue = row["NewValue"] == DBNull.Value ? null : ConvertToString(row["NewValue"]),
                OperationType = ConvertToString(row["OperationType"]),
                Source = row["Source"] == DBNull.Value ? null : ConvertToString(row["Source"]),
                UserId = row["UserId"] == DBNull.Value ? (int?)null : ConvertToInt(row["UserId"]),
                UserName = row["UserName"] == DBNull.Value ? null : ConvertToString(row["UserName"]),
                UserRole = row["UserRole"] == DBNull.Value ? null : ConvertToString(row["UserRole"]),
                ChangedAt = ConvertToDateTime(row["ChangedAt"]),
                Remarks = row["Remarks"] == DBNull.Value ? null : ConvertToString(row["Remarks"])
            };
        }

        #endregion
        #region Scope-Specific Settings Methods

        public string GetStudentSetting(int studentId, string settingKey)
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return "";

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SettingKey", settingKey }
        };

                object result = ExecuteScalar(
                    "SELECT SettingValue FROM StudentSettings WHERE StudentId = @StudentId AND SettingKey = @SettingKey LIMIT 1",
                    parameters);

                return result == null || result == DBNull.Value ? "" : result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentSetting error: {ex.Message}");
                return "";
            }
        }

        public bool SetStudentSetting(int studentId, string settingKey, string settingValue)
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SettingKey", settingKey },
            { "@SettingValue", settingValue ?? "" },
            { "@Now", DateTime.Now }
        };

                int affected = ExecuteNonQuery(
                    @"INSERT INTO StudentSettings (StudentId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
              VALUES (@StudentId, @SettingKey, @SettingValue, @Now, @Now)
              ON CONFLICT(StudentId, SettingKey)
              DO UPDATE SET SettingValue = @SettingValue, ModifiedDate = @Now",
                    parameters);

                return affected > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetStudentSetting error: {ex.Message}");
                return false;
            }
        }

        public bool DeleteStudentSetting(int studentId, string settingKey)
        {
            if (studentId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@StudentId", studentId },
            { "@SettingKey", settingKey }
        };

                return ExecuteNonQuery(
                    "DELETE FROM StudentSettings WHERE StudentId = @StudentId AND SettingKey = @SettingKey",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteStudentSetting error: {ex.Message}");
                return false;
            }
        }

        public Dictionary<string, string> GetAllStudentSettings(int studentId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (studentId <= 0) return result;

            try
            {
                var parameters = new Dictionary<string, object> { { "@StudentId", studentId } };
                DataTable dt = ExecuteQuery(
                    "SELECT SettingKey, SettingValue FROM StudentSettings WHERE StudentId = @StudentId",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    string key = ConvertToString(row["SettingKey"]);
                    string val = row["SettingValue"] == DBNull.Value ? "" : ConvertToString(row["SettingValue"]);
                    if (!string.IsNullOrEmpty(key))
                        result[key] = val;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllStudentSettings error: {ex.Message}");
            }

            return result;
        }

        public string GetSchoolScopedSetting(int schoolId, string settingKey)
        {
            if (schoolId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return "";

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@SettingKey", settingKey }
        };

                object result = ExecuteScalar(
                    "SELECT SettingValue FROM SchoolSettings WHERE SchoolId = @SchoolId AND SettingKey = @SettingKey LIMIT 1",
                    parameters);

                return result == null || result == DBNull.Value ? "" : result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetSchoolScopedSetting error: {ex.Message}");
                return "";
            }
        }

        public bool SetSchoolScopedSetting(int schoolId, string settingKey, string settingValue)
        {
            if (schoolId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@SettingKey", settingKey },
            { "@SettingValue", settingValue ?? "" },
            { "@Now", DateTime.Now }
        };

                int affected = ExecuteNonQuery(
                    @"INSERT INTO SchoolSettings (SchoolId, SettingKey, SettingValue, CreatedDate, ModifiedDate)
              VALUES (@SchoolId, @SettingKey, @SettingValue, @Now, @Now)
              ON CONFLICT(SchoolId, SettingKey)
              DO UPDATE SET SettingValue = @SettingValue, ModifiedDate = @Now",
                    parameters);

                return affected > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetSchoolScopedSetting error: {ex.Message}");
                return false;
            }
        }

        public bool DeleteSchoolScopedSetting(int schoolId, string settingKey)
        {
            if (schoolId <= 0 || string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@SettingKey", settingKey }
        };

                return ExecuteNonQuery(
                    "DELETE FROM SchoolSettings WHERE SchoolId = @SchoolId AND SettingKey = @SettingKey",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteSchoolScopedSetting error: {ex.Message}");
                return false;
            }
        }

        public Dictionary<string, string> GetAllSchoolScopedSettings(int schoolId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (schoolId <= 0) return result;

            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
                DataTable dt = ExecuteQuery(
                    "SELECT SettingKey, SettingValue FROM SchoolSettings WHERE SchoolId = @SchoolId",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    string key = ConvertToString(row["SettingKey"]);
                    string val = row["SettingValue"] == DBNull.Value ? "" : ConvertToString(row["SettingValue"]);
                    if (!string.IsNullOrEmpty(key))
                        result[key] = val;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllSchoolScopedSettings error: {ex.Message}");
            }

            return result;
        }

        public int MigrateActiveLicensesToSubscriptions()
        {
            try
            {
                var schools = ExecuteQuery(@"
SELECT Id, LicenseKey, LicenseExpiryDate
FROM Schools
WHERE LicenseKey IS NOT NULL AND TRIM(LicenseKey) <> ''");
                int migrated = 0;
                foreach (DataRow row in schools.Rows)
                {
                    int schoolId = Convert.ToInt32(row["Id"]);
                    object existing = ExecuteScalar(
                        "SELECT Id FROM Subscriptions WHERE SchoolId = @SchoolId LIMIT 1",
                        new Dictionary<string, object> { { "@SchoolId", schoolId } });
                    if (existing != null && existing != DBNull.Value) continue;

                    DateTime? expiry = row["LicenseExpiryDate"] == DBNull.Value
                        ? (DateTime?)null
                        : Convert.ToDateTime(row["LicenseExpiryDate"]).ToUniversalTime();
                    DateTime now = DateTime.UtcNow;
                    ExecuteNonQuery(@"
INSERT INTO Subscriptions
(SchoolId, PlanCode, Status, StartedUtc, ExpiresUtc, GraceUntilUtc, Source, LegacyLicenseKey, UpdatedUtc)
VALUES (@SchoolId, 'legacy', 'Active', @StartedUtc, @ExpiresUtc, @GraceUntilUtc, 'legacy-license', @LegacyLicenseKey, @UpdatedUtc)",
                        new Dictionary<string, object>
                        {
                            { "@SchoolId", schoolId },
                            { "@StartedUtc", now },
                            { "@ExpiresUtc", (object)expiry ?? DBNull.Value },
                            { "@GraceUntilUtc", (object)(expiry?.AddDays(7)) ?? DBNull.Value },
                            { "@LegacyLicenseKey", row["LicenseKey"]?.ToString() },
                            { "@UpdatedUtc", now }
                        });
                    migrated++;
                }
                return migrated;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Subscription migration failed: {ex.Message}");
                return 0;
            }
        }

        public string GetAppSetting(string settingKey)
        {
            if (string.IsNullOrWhiteSpace(settingKey)) return "";

            try
            {
                var parameters = new Dictionary<string, object> { { "@SettingKey", settingKey } };

                object result = ExecuteScalar(
                    "SELECT SettingValue FROM AppSettings WHERE SettingKey = @SettingKey LIMIT 1",
                    parameters);

                return result == null || result == DBNull.Value ? "" : result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAppSetting error: {ex.Message}");
                return "";
            }
        }

        public bool SetAppSetting(string settingKey, string settingValue)
        {
            if (string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SettingKey", settingKey },
            { "@SettingValue", settingValue ?? "" },
            { "@Now", DateTime.Now }
        };

                int affected = ExecuteNonQuery(
                    @"INSERT INTO AppSettings (SettingKey, SettingValue, CreatedDate, ModifiedDate)
              VALUES (@SettingKey, @SettingValue, @Now, @Now)
              ON CONFLICT(SettingKey)
              DO UPDATE SET SettingValue = @SettingValue, ModifiedDate = @Now",
                    parameters);

                return affected > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetAppSetting error: {ex.Message}");
                return false;
            }
        }

        public bool DeleteAppSetting(string settingKey)
        {
            if (string.IsNullOrWhiteSpace(settingKey)) return false;

            try
            {
                if (string.Equals(settingKey, "__legacy_settings_migrated", StringComparison.OrdinalIgnoreCase))
                    return false;

                var parameters = new Dictionary<string, object> { { "@SettingKey", settingKey } };
                return ExecuteNonQuery("DELETE FROM AppSettings WHERE SettingKey = @SettingKey", parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteAppSetting error: {ex.Message}");
                return false;
            }
        }

        public Dictionary<string, string> GetAllAppSettings()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                DataTable dt = ExecuteQuery("SELECT SettingKey, SettingValue FROM AppSettings");
                foreach (DataRow row in dt.Rows)
                {
                    string key = ConvertToString(row["SettingKey"]);
                    string val = row["SettingValue"] == DBNull.Value ? "" : ConvertToString(row["SettingValue"]);
                    if (!string.IsNullOrEmpty(key) && !key.StartsWith("__"))
                        result[key] = val;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllAppSettings error: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Promotion Paths Methods

        public List<PromotionPath> GetPromotionPaths(int schoolId)
        {
            var results = new List<PromotionPath>();

            try
            {
                var parameters = new Dictionary<string, object> { { "@SchoolId", schoolId } };
                DataTable dt = ExecuteQuery(
                    "SELECT * FROM PromotionPaths WHERE SchoolId = @SchoolId ORDER BY DisplayOrder, FromClassName",
                    parameters);

                foreach (DataRow row in dt.Rows)
                    results.Add(MapPromotionPathRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionPaths error: {ex.Message}");
            }

            return results;
        }

        public PromotionPath GetPromotionPath(int schoolId, string fromClassName)
        {
            if (string.IsNullOrWhiteSpace(fromClassName)) return null;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@From", fromClassName.Trim() }
        };

                DataTable dt = ExecuteQuery(
                    "SELECT * FROM PromotionPaths WHERE SchoolId = @SchoolId AND FromClassName = @From LIMIT 1",
                    parameters);

                if (dt.Rows.Count == 0) return null;
                return MapPromotionPathRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionPath error: {ex.Message}");
                return null;
            }
        }

        public bool SavePromotionPath(PromotionPath path)
        {
            if (path == null || string.IsNullOrWhiteSpace(path.FromClassName)) return false;
            if (string.IsNullOrWhiteSpace(path.ToClassName)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", path.SchoolId },
            { "@From", path.FromClassName.Trim() },
            { "@To", path.ToClassName.Trim() },
            { "@Terminal", path.IsTerminal },
            { "@Order", path.DisplayOrder },
            { "@Active", path.IsActive },
            { "@Now", DateTime.Now }
        };

                int affected = ExecuteNonQuery(
                    @"INSERT INTO PromotionPaths
                (SchoolId, FromClassName, ToClassName, IsTerminal, DisplayOrder, IsActive, CreatedDate, ModifiedDate)
              VALUES
                (@SchoolId, @From, @To, @Terminal, @Order, @Active, @Now, @Now)
              ON CONFLICT(SchoolId, FromClassName)
              DO UPDATE SET
                ToClassName = @To,
                IsTerminal = @Terminal,
                DisplayOrder = @Order,
                IsActive = @Active,
                ModifiedDate = @Now",
                    parameters);

                return affected > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SavePromotionPath error: {ex.Message}");
                return false;
            }
        }

        public bool DeletePromotionPath(int schoolId, string fromClassName)
        {
            if (string.IsNullOrWhiteSpace(fromClassName)) return false;

            try
            {
                var parameters = new Dictionary<string, object>
        {
            { "@SchoolId", schoolId },
            { "@From", fromClassName.Trim() }
        };

                return ExecuteNonQuery(
                    "DELETE FROM PromotionPaths WHERE SchoolId = @SchoolId AND FromClassName = @From",
                    parameters) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeletePromotionPath error: {ex.Message}");
                return false;
            }
        }

        private PromotionPath MapPromotionPathRow(DataRow row)
        {
            if (row == null) return null;

            return new PromotionPath
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                FromClassName = ConvertToString(row["FromClassName"]),
                ToClassName = ConvertToString(row["ToClassName"]),
                IsTerminal = ConvertToBoolean(row["IsTerminal"]),
                DisplayOrder = ConvertToInt(row["DisplayOrder"]),
                IsActive = ConvertToBoolean(row["IsActive"]),
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"])
            };
        }

        #endregion


        #region Student Enrollments, Batches & Promotion (append-only ledger)

        private void EnsureStudentEnrollmentsTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS StudentEnrollments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                ClassId INTEGER NOT NULL,
                AcademicYear TEXT NOT NULL,
                Term INTEGER,
                EnrollmentDate DATETIME NOT NULL,
                ExitDate DATETIME,
                Status TEXT NOT NULL DEFAULT 'Active',
                ExitReason TEXT,
                IsCurrent BOOLEAN DEFAULT 1,
                BatchId INTEGER,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId)   REFERENCES Classes(Id)  ON DELETE CASCADE
            )", null, connection);

                TryAddColumn(connection, "StudentEnrollments", "BatchId", "INTEGER");

                ExecuteNonQuery(
                    "CREATE UNIQUE INDEX IF NOT EXISTS idx_enrollments_unique ON StudentEnrollments(StudentId, ClassId, AcademicYear);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_enrollments_student ON StudentEnrollments(StudentId, AcademicYear);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_enrollments_class ON StudentEnrollments(ClassId, AcademicYear);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_enrollments_current ON StudentEnrollments(StudentId, IsCurrent);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_enrollments_batch ON StudentEnrollments(BatchId);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureStudentEnrollmentsTable error: {ex.Message}");
            }
        }

        private void EnsurePromotionBatchesTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
            CREATE TABLE IF NOT EXISTS PromotionBatches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolId INTEGER NOT NULL,
                SourceClassId INTEGER NOT NULL,
                DestinationClassId INTEGER NOT NULL,
                SourceAcademicYear TEXT NOT NULL,
                DestinationAcademicYear TEXT NOT NULL,
                StudentCount INTEGER NOT NULL,
                PerformedByUserId INTEGER,
                PerformedByName TEXT,
                PerformedByRole TEXT,
                BatchTimestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                RevertedAt DATETIME,
                RevertedByUserId INTEGER,
                RevertedByName TEXT
            )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_batches_school ON PromotionBatches(SchoolId, BatchTimestamp DESC);",
                    null, connection);
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_batches_dest ON PromotionBatches(DestinationClassId, DestinationAcademicYear);",
                    null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsurePromotionBatchesTable error: {ex.Message}");
            }
        }

        private void TryAddColumn(SqliteConnection connection, string tableName,
                                  string columnName, string columnType)
        {
            try
            {
                if (!Regex.IsMatch(tableName, "^[A-Za-z0-9_]+$")) return;
                if (!Regex.IsMatch(columnName, "^[A-Za-z0-9_]+$")) return;

                var cols = GetTableColumnNames(connection, tableName);
                if (cols.Contains(columnName))
                    return;

                ExecuteNonQuery($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}", null, connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryAddColumn({tableName}.{columnName}) error: {ex.Message}");
            }
        }

        private void BackfillStudentEnrollments(SqliteConnection connection)
        {
            try
            {
                object existing = ExecuteScalar("SELECT COUNT(*) FROM StudentEnrollments", null, connection);
                int count = existing == null || existing == DBNull.Value ? 0 : Convert.ToInt32(existing);
                if (count > 0) return;

                ExecuteNonQuery(@"
            INSERT INTO StudentEnrollments
                (StudentId, ClassId, AcademicYear, EnrollmentDate,
                 IsCurrent, Status, CreatedDate, ModifiedDate)
            SELECT
                s.Id,
                s.ClassId,
                IFNULL(c.AcademicYear, ''),
                IFNULL(s.CreatedDate, CURRENT_TIMESTAMP),
                1,
                'Active',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            FROM Students s
            INNER JOIN Classes c ON s.ClassId = c.Id
            WHERE (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
              AND s.ClassId IS NOT NULL",
                    null, connection);

                Debug.WriteLine("StudentEnrollments backfilled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BackfillStudentEnrollments error: {ex.Message}");
            }
        }

        public Dictionary<int, (string Status, string Year)> GetPromotionStatuses(
            IEnumerable<int> studentIds)
        {
            var result = new Dictionary<int, (string, string)>();
            var ids = studentIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return result;

            try
            {
                foreach (var chunk in ChunkList(ids, 500))
                {
                    string inClause = string.Join(",", chunk);

                    try
                    {
                        DataTable dt = ExecuteQuery($@"
                    SELECT StudentId, SettingKey, SettingValue
                    FROM StudentSettings
                    WHERE StudentId IN ({inClause})
                      AND SettingKey IN ('PromotionStatus', 'PromotionYear')");

                        foreach (DataRow row in dt.Rows)
                        {
                            int sid = Convert.ToInt32(row["StudentId"]);
                            if (!result.ContainsKey(sid)) result[sid] = ("", "");
                            var (s, y) = result[sid];
                            string key = row["SettingKey"]?.ToString() ?? "";
                            string val = row["SettingValue"]?.ToString() ?? "";
                            result[sid] = key == "PromotionStatus" ? (val, y) : (s, val);
                        }
                    }
                    catch { }

                    var missing = chunk.Where(id => !result.ContainsKey(id)).ToList();
                    if (missing.Count > 0)
                    {
                        try
                        {
                            string missingIn = string.Join(",", missing);
                            DataTable dt = ExecuteQuery($@"
                        SELECT StudentId, SettingKey, SettingValue
                        FROM Settings
                        WHERE StudentId IN ({missingIn})
                          AND SettingKey IN ('PromotionStatus', 'PromotionYear')");

                            foreach (DataRow row in dt.Rows)
                            {
                                int sid = Convert.ToInt32(row["StudentId"]);
                                if (!result.ContainsKey(sid)) result[sid] = ("", "");
                                var (s, y) = result[sid];
                                string key = row["SettingKey"]?.ToString() ?? "";
                                string val = row["SettingValue"]?.ToString() ?? "";
                                result[sid] = key == "PromotionStatus" ? (val, y) : (s, val);
                            }
                        }
                        catch { }
                    }

                    foreach (int id in chunk)
                        if (!result.ContainsKey(id))
                            result[id] = ("", "");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPromotionStatuses error: {ex.Message}");
            }

            return result;
        }

        public Dictionary<int, (int ClassId, string AcademicYear, int EnrollmentId)>
            GetCurrentEnrollments(IEnumerable<int> studentIds)
        {
            var result = new Dictionary<int, (int, string, int)>();
            var ids = studentIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return result;

            try
            {
                foreach (var chunk in ChunkList(ids, 500))
                {
                    string inClause = string.Join(",", chunk);
                    DataTable dt = ExecuteQuery($@"
                SELECT StudentId, Id, ClassId, AcademicYear
                FROM StudentEnrollments
                WHERE StudentId IN ({inClause}) AND IsCurrent = 1");

                    foreach (DataRow row in dt.Rows)
                    {
                        int sid = Convert.ToInt32(row["StudentId"]);
                        int eid = Convert.ToInt32(row["Id"]);
                        int cid = Convert.ToInt32(row["ClassId"]);
                        string yr = row["AcademicYear"]?.ToString() ?? "";
                        result[sid] = (cid, yr, eid);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetCurrentEnrollments error: {ex.Message}");
            }
            return result;
        }

        public HashSet<int> GetStudentIdsEnrolledInClass(int classId, string academicYear)
        {
            var result = new HashSet<int>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return result;

            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT StudentId FROM StudentEnrollments
            WHERE ClassId = @ClassId AND AcademicYear = @AcademicYear",
                    new Dictionary<string, object>
                    {
                { "@ClassId", classId },
                { "@AcademicYear", academicYear.Trim() }
                    });

                foreach (DataRow row in dt.Rows)
                    result.Add(Convert.ToInt32(row["StudentId"]));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentIdsEnrolledInClass error: {ex.Message}");
            }
            return result;
        }

        public PromotionPreview BuildPromotionPreview(
            List<int> studentIds,
            int destinationClassId,
            string destinationAcademicYear,
            Dictionary<int, Student> studentLookup)
        {
            var preview = new PromotionPreview
            {
                DestinationClassId = destinationClassId,
                DestinationAcademicYear = destinationAcademicYear?.Trim() ?? ""
            };

            if (studentIds == null || studentIds.Count == 0) return preview;

            var currentEnrollments = GetCurrentEnrollments(studentIds);
            var alreadyInDest = GetStudentIdsEnrolledInClass(destinationClassId, destinationAcademicYear);

            foreach (int sid in studentIds)
            {
                Student s = studentLookup != null && studentLookup.ContainsKey(sid)
                    ? studentLookup[sid] : GetStudentById(sid);
                if (s == null) continue;

                var entry = new PreviewStudent
                {
                    StudentId = sid,
                    AdmissionNumber = s.AdmissionNumber,
                    FullName = s.FullName
                };

                if (alreadyInDest.Contains(sid))
                {
                    entry.Reason = "Already enrolled in destination";
                    preview.AlreadyInDestination.Add(entry);
                    continue;
                }

                if (currentEnrollments.ContainsKey(sid))
                {
                    var (cid, yr, _) = currentEnrollments[sid];
                    if (cid == destinationClassId &&
                        string.Equals(yr, destinationAcademicYear, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Reason = "Already in destination class";
                        preview.AlreadyInDestination.Add(entry);
                        continue;
                    }
                }

                preview.ToPromote.Add(entry);
            }

            return preview;
        }

        public AssignStudentsResult AssignStudentsToClass(
    List<int> studentIds,
    int destinationClassId,
    string destinationAcademicYear,
    int schoolId,
    int? performedByUserId = null,
    string performedByName = null,
    string performedByRole = null,
    Action<int, int> onProgress = null)
        {
            var result = new AssignStudentsResult();

            if (studentIds == null || studentIds.Count == 0) return result;
            if (destinationClassId <= 0 || schoolId <= 0) return result;

            string destYear = (destinationAcademicYear ?? "").Trim();
            if (destYear.Length == 0) return result;

            int affected = 0;
            int batchId = 0;
            SqliteConnection connection = null;

            try
            {
                connection = GetConnection();

                object destSchoolId = ExecuteScalar(
                    "SELECT SchoolId FROM Classes WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", destinationClassId } },
                    connection);

                if (destSchoolId == null || destSchoolId == DBNull.Value ||
                    Convert.ToInt32(destSchoolId) != schoolId)
                {
                    Debug.WriteLine("AssignStudentsToClass: destination class not in this school.");
                    return result;
                }

                var validStudentIds = new List<int>();
                foreach (var chunk in ChunkList(studentIds, 500))
                {
                    string inClause = string.Join(",", chunk);
                    DataTable dt = ExecuteQuery(
                        $"SELECT Id FROM Students WHERE SchoolId = @SchoolId AND Id IN ({inClause})",
                        new Dictionary<string, object> { { "@SchoolId", schoolId } },
                        connection);

                    foreach (DataRow row in dt.Rows)
                        validStudentIds.Add(Convert.ToInt32(row["Id"]));
                }

                if (validStudentIds.Count == 0) return result;

                int sourceClassId = 0;
                string sourceYear = "";
                var firstEnr = ExecuteQuery(@"
            SELECT ClassId, AcademicYear FROM StudentEnrollments
            WHERE StudentId = @Sid AND IsCurrent = 1 LIMIT 1",
                    new Dictionary<string, object> { { "@Sid", validStudentIds[0] } },
                    connection);

                if (firstEnr.Rows.Count > 0)
                {
                    sourceClassId = Convert.ToInt32(firstEnr.Rows[0]["ClassId"]);
                    sourceYear = firstEnr.Rows[0]["AcademicYear"]?.ToString() ?? "";
                }

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        ExecuteNonQuery(@"
                    INSERT INTO PromotionBatches
                        (SchoolId, SourceClassId, DestinationClassId,
                         SourceAcademicYear, DestinationAcademicYear, StudentCount,
                         PerformedByUserId, PerformedByName, PerformedByRole,
                         BatchTimestamp)
                    VALUES
                        (@SchoolId, @SrcId, @DstId,
                         @SrcYear, @DstYear, @Count,
                         @UserId, @Name, @Role, @Now)",
                            new Dictionary<string, object>
                            {
                        { "@SchoolId", schoolId },
                        { "@SrcId", sourceClassId },
                        { "@DstId", destinationClassId },
                        { "@SrcYear", sourceYear },
                        { "@DstYear", destYear },
                        { "@Count", validStudentIds.Count },
                        { "@UserId", (object)performedByUserId ?? DBNull.Value },
                        { "@Name", performedByName ?? "" },
                        { "@Role", performedByRole ?? "" },
                        { "@Now", DateTime.Now }
                            }, connection);

                        object batchIdObj = ExecuteScalar("SELECT last_insert_rowid()", null, connection);
                        batchId = batchIdObj == null || batchIdObj == DBNull.Value
                            ? 0 : Convert.ToInt32(batchIdObj);

                        for (int i = 0; i < validStudentIds.Count; i++)
                        {
                            int studentId = validStudentIds[i];
                            DateTime now = DateTime.Now;

                            DataTable curDt = ExecuteQuery(@"
                        SELECT Id, ClassId, AcademicYear FROM StudentEnrollments
                        WHERE StudentId = @Sid AND IsCurrent = 1
                        ORDER BY Id DESC LIMIT 1",
                                new Dictionary<string, object> { { "@Sid", studentId } },
                                connection);

                            int? currentEnrollmentId = null;
                            int? currentClassId = null;
                            string currentYear = "";

                            if (curDt.Rows.Count > 0)
                            {
                                currentEnrollmentId = Convert.ToInt32(curDt.Rows[0]["Id"]);
                                currentClassId = Convert.ToInt32(curDt.Rows[0]["ClassId"]);
                                currentYear = curDt.Rows[0]["AcademicYear"]?.ToString() ?? "";
                            }

                            bool alreadyThere = currentClassId.HasValue
                                && currentClassId.Value == destinationClassId
                                && string.Equals(currentYear, destYear, StringComparison.OrdinalIgnoreCase);

                            if (alreadyThere)
                            {
                                onProgress?.Invoke(i + 1, validStudentIds.Count);
                                continue;
                            }

                            if (currentEnrollmentId.HasValue)
                            {
                                ExecuteNonQuery(@"
                            UPDATE StudentEnrollments
                            SET IsCurrent = 0,
                                ExitDate = @Now,
                                Status = @Status,
                                ExitReason = @Reason,
                                ModifiedDate = @Now
                            WHERE Id = @Id",
                                    new Dictionary<string, object>
                                    {
                                { "@Id", currentEnrollmentId.Value },
                                { "@Now", now },
                                { "@Status", string.Equals(currentYear, destYear, StringComparison.OrdinalIgnoreCase)
                                             ? "Corrected" : "Promoted" },
                                { "@Reason", $"? Class {destinationClassId} ({destYear})" }
                                    }, connection);
                            }

                            ExecuteNonQuery(@"
                        INSERT INTO StudentEnrollments
                            (StudentId, ClassId, AcademicYear, EnrollmentDate,
                             IsCurrent, Status, BatchId, CreatedDate, ModifiedDate)
                        VALUES
                            (@Sid, @Cid, @Year, @Now,
                             1, 'Active', @BatchId, @Now, @Now)",
                                new Dictionary<string, object>
                                {
                            { "@Sid", studentId },
                            { "@Cid", destinationClassId },
                            { "@Year", destYear },
                            { "@Now", now },
                            { "@BatchId", batchId == 0 ? (object)DBNull.Value : batchId }
                                }, connection);

                            ExecuteNonQuery(@"
                        UPDATE Students SET ClassId = @Cid, ModifiedDate = @Now
                        WHERE Id = @Sid",
                                new Dictionary<string, object>
                                {
                            { "@Sid", studentId },
                            { "@Cid", destinationClassId },
                            { "@Now", now }
                                }, connection);

                            ExecuteNonQuery(@"
                        INSERT INTO StudentMoveAudit
                            (StudentId, FromClassId, ToClassId, PerformedByUserId,
                             PerformedByName, PerformedByRole, AcademicYear, Details)
                        VALUES
                            (@Sid, @From, @To, @UserId, @Name, @Role, @Year, @Details)",
                                new Dictionary<string, object>
                                {
                            { "@Sid", studentId },
                            { "@From", (object)currentClassId ?? DBNull.Value },
                            { "@To", destinationClassId },
                            { "@UserId", (object)performedByUserId ?? DBNull.Value },
                            { "@Name", performedByName ?? "" },
                            { "@Role", performedByRole ?? "" },
                            { "@Year", destYear },
                            { "@Details", $"Batch {batchId}" }
                                }, connection);

                            affected++;
                            onProgress?.Invoke(i + 1, validStudentIds.Count);
                        }

                        if (batchId > 0)
                        {
                            ExecuteNonQuery(
                                "UPDATE PromotionBatches SET StudentCount = @C WHERE Id = @Id",
                                new Dictionary<string, object>
                                {
                            { "@C", affected },
                            { "@Id", batchId }
                                }, connection);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AssignStudentsToClass error: {ex.Message}");
                return result;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }

            result.MovedCount = affected;
            result.BatchId = batchId;
            return result;
        }

        public bool RevertPromotionBatch(int batchId, int? revertedByUserId, string revertedByName)
        {
            if (batchId <= 0) return false;

            SqliteConnection connection = null;
            try
            {
                connection = GetConnection();

                DataTable batchDt = ExecuteQuery(
                    "SELECT * FROM PromotionBatches WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", batchId } },
                    connection);

                if (batchDt.Rows.Count == 0) return false;
                DataRow batch = batchDt.Rows[0];

                if (batch["RevertedAt"] != DBNull.Value)
                {
                    Debug.WriteLine("Batch already reverted");
                    return false;
                }

                int srcClassId = Convert.ToInt32(batch["SourceClassId"]);
                string srcYear = batch["SourceAcademicYear"]?.ToString() ?? "";

                DataTable newEnr = ExecuteQuery(@"
            SELECT Id, StudentId FROM StudentEnrollments
            WHERE BatchId = @Bid AND IsCurrent = 1",
                    new Dictionary<string, object> { { "@Bid", batchId } },
                    connection);

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (DataRow row in newEnr.Rows)
                        {
                            int studentId = Convert.ToInt32(row["StudentId"]);
                            int enrollId = Convert.ToInt32(row["Id"]);
                            DateTime now = DateTime.Now;

                            ExecuteNonQuery(@"
                        UPDATE StudentEnrollments
                        SET IsCurrent = 0, ExitDate = @Now,
                            Status = 'Reverted', ModifiedDate = @Now
                        WHERE Id = @Id",
                                new Dictionary<string, object>
                                {
                            { "@Id", enrollId }, { "@Now", now }
                                }, connection);

                            DataTable prevDt = ExecuteQuery(@"
                        SELECT Id FROM StudentEnrollments
                        WHERE StudentId = @Sid AND IsCurrent = 0
                          AND Id <> @This
                        ORDER BY Id DESC LIMIT 1",
                                new Dictionary<string, object>
                                {
                            { "@Sid", studentId },
                            { "@This", enrollId }
                                }, connection);

                            if (prevDt.Rows.Count > 0)
                            {
                                int prevId = Convert.ToInt32(prevDt.Rows[0]["Id"]);
                                ExecuteNonQuery(@"
                            UPDATE StudentEnrollments
                            SET IsCurrent = 1, ExitDate = NULL,
                                Status = 'Active', ModifiedDate = @Now
                            WHERE Id = @Id",
                                    new Dictionary<string, object>
                                    {
                                { "@Id", prevId }, { "@Now", now }
                                    }, connection);
                            }
                            else if (srcClassId > 0)
                            {
                                ExecuteNonQuery(@"
                            INSERT INTO StudentEnrollments
                                (StudentId, ClassId, AcademicYear, EnrollmentDate,
                                 IsCurrent, Status, CreatedDate, ModifiedDate)
                            VALUES
                                (@Sid, @Cid, @Year, @Now, 1, 'Active', @Now, @Now)",
                                    new Dictionary<string, object>
                                    {
                                { "@Sid", studentId },
                                { "@Cid", srcClassId },
                                { "@Year", srcYear },
                                { "@Now", now }
                                    }, connection);
                            }

                            ExecuteNonQuery(@"
                        UPDATE Students SET
                            ClassId = (SELECT ClassId FROM StudentEnrollments
                                       WHERE StudentId = @Sid AND IsCurrent = 1 LIMIT 1),
                            ModifiedDate = @Now
                        WHERE Id = @Sid",
                                new Dictionary<string, object>
                                {
                            { "@Sid", studentId }, { "@Now", now }
                                }, connection);
                        }

                        ExecuteNonQuery(@"
                    UPDATE PromotionBatches SET
                        RevertedAt = @Now,
                        RevertedByUserId = @UserId,
                        RevertedByName = @Name
                    WHERE Id = @Id",
                            new Dictionary<string, object>
                            {
                        { "@Id", batchId },
                        { "@Now", DateTime.Now },
                        { "@UserId", (object)revertedByUserId ?? DBNull.Value },
                        { "@Name", revertedByName ?? "" }
                            }, connection);

                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevertPromotionBatch error: {ex.Message}");
                return false;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }
        }

        public PromotionBatch GetLastRevertableBatch(int schoolId)
        {
            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT * FROM PromotionBatches
            WHERE SchoolId = @SchoolId AND RevertedAt IS NULL
            ORDER BY BatchTimestamp DESC LIMIT 1",
                    new Dictionary<string, object> { { "@SchoolId", schoolId } });

                if (dt.Rows.Count == 0) return null;
                return MapPromotionBatchRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLastRevertableBatch error: {ex.Message}");
                return null;
            }
        }

        public List<PromotionBatch> GetRecentBatches(int schoolId, int limit = 50)
        {
            var list = new List<PromotionBatch>();
            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT b.*, 
                   c1.ClassName AS SourceClassName,
                   c2.ClassName AS DestinationClassName
            FROM PromotionBatches b
            LEFT JOIN Classes c1 ON b.SourceClassId = c1.Id
            LEFT JOIN Classes c2 ON b.DestinationClassId = c2.Id
            WHERE b.SchoolId = @SchoolId
            ORDER BY b.BatchTimestamp DESC
            LIMIT @Limit",
                    new Dictionary<string, object>
                    {
                { "@SchoolId", schoolId },
                { "@Limit", limit }
                    });

                foreach (DataRow row in dt.Rows)
                    list.Add(MapPromotionBatchRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetRecentBatches error: {ex.Message}");
            }
            return list;
        }

        private PromotionBatch MapPromotionBatchRow(DataRow row)
        {
            if (row == null) return null;
            return new PromotionBatch
            {
                Id = ConvertToInt(row["Id"]),
                SchoolId = ConvertToInt(row["SchoolId"]),
                SourceClassId = ConvertToInt(row["SourceClassId"]),
                DestinationClassId = ConvertToInt(row["DestinationClassId"]),
                SourceAcademicYear = ConvertToString(row["SourceAcademicYear"]),
                DestinationAcademicYear = ConvertToString(row["DestinationAcademicYear"]),
                StudentCount = ConvertToInt(row["StudentCount"]),
                PerformedByUserId = row["PerformedByUserId"] == DBNull.Value
                    ? (int?)null : ConvertToInt(row["PerformedByUserId"]),
                PerformedByName = ConvertToString(row["PerformedByName"]),
                PerformedByRole = ConvertToString(row["PerformedByRole"]),
                BatchTimestamp = ConvertToDateTime(row["BatchTimestamp"]),
                RevertedAt = row["RevertedAt"] == DBNull.Value
                    ? (DateTime?)null : ConvertToDateTime(row["RevertedAt"]),
                RevertedByUserId = row["RevertedByUserId"] == DBNull.Value
                    ? (int?)null : ConvertToInt(row["RevertedByUserId"]),
                RevertedByName = ConvertToString(row["RevertedByName"]),
                SourceClassName = row.Table.Columns.Contains("SourceClassName")
                    ? ConvertToString(row["SourceClassName"]) : null,
                DestinationClassName = row.Table.Columns.Contains("DestinationClassName")
                    ? ConvertToString(row["DestinationClassName"]) : null
            };
        }

        public List<Student> GetStudentsInClassForYear(int classId, string academicYear)
        {
            var students = new List<Student>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return students;

            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT s.* FROM Students s
            INNER JOIN StudentEnrollments e ON s.Id = e.StudentId
            WHERE e.ClassId = @ClassId AND e.AcademicYear = @AcademicYear
              AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
            ORDER BY s.FullName",
                    new Dictionary<string, object>
                    {
                { "@ClassId", classId },
                { "@AcademicYear", academicYear.Trim() }
                    });

                foreach (DataRow row in dt.Rows)
                    students.Add(MapStudentRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentsInClassForYear error: {ex.Message}");
            }
            return students;
        }

        public List<StudentEnrollment> GetStudentEnrollmentHistory(int studentId)
        {
            var results = new List<StudentEnrollment>();
            if (studentId <= 0) return results;

            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT e.*, c.ClassName
            FROM StudentEnrollments e
            LEFT JOIN Classes c ON e.ClassId = c.Id
            WHERE e.StudentId = @StudentId
            ORDER BY e.EnrollmentDate DESC, e.Id DESC",
                    new Dictionary<string, object> { { "@StudentId", studentId } });

                foreach (DataRow row in dt.Rows)
                    results.Add(MapEnrollmentRow(row));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetStudentEnrollmentHistory error: {ex.Message}");
            }
            return results;
        }

        public StudentEnrollment GetCurrentEnrollment(int studentId)
        {
            if (studentId <= 0) return null;

            try
            {
                DataTable dt = ExecuteQuery(@"
            SELECT e.*, c.ClassName FROM StudentEnrollments e
            LEFT JOIN Classes c ON e.ClassId = c.Id
            WHERE e.StudentId = @StudentId AND e.IsCurrent = 1
            ORDER BY e.Id DESC LIMIT 1",
                    new Dictionary<string, object> { { "@StudentId", studentId } });

                if (dt.Rows.Count == 0) return null;
                return MapEnrollmentRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetCurrentEnrollment error: {ex.Message}");
                return null;
            }
        }

        private StudentEnrollment MapEnrollmentRow(DataRow row)
        {
            if (row == null) return null;
            return new StudentEnrollment
            {
                Id = ConvertToInt(row["Id"]),
                StudentId = ConvertToInt(row["StudentId"]),
                ClassId = ConvertToInt(row["ClassId"]),
                AcademicYear = ConvertToString(row["AcademicYear"]),
                Term = row["Term"] == DBNull.Value ? (int?)null : ConvertToInt(row["Term"]),
                EnrollmentDate = ConvertToDateTime(row["EnrollmentDate"]),
                ExitDate = row["ExitDate"] == DBNull.Value ? (DateTime?)null : ConvertToDateTime(row["ExitDate"]),
                Status = ConvertToString(row["Status"]),
                ExitReason = ConvertToString(row["ExitReason"]),
                IsCurrent = ConvertToBoolean(row["IsCurrent"]),
                BatchId = row.Table.Columns.Contains("BatchId") && row["BatchId"] != DBNull.Value
                    ? (int?)ConvertToInt(row["BatchId"]) : null,
                CreatedDate = ConvertToDateTime(row["CreatedDate"]),
                ModifiedDate = ConvertToDateTime(row["ModifiedDate"]),
                ClassName = row.Table.Columns.Contains("ClassName")
                    ? ConvertToString(row["ClassName"]) : null
            };
        }

        public static IEnumerable<List<T>> ChunkList<T>(IEnumerable<T> source, int size)
        {
            var list = source?.ToList() ?? new List<T>();
            for (int i = 0; i < list.Count; i += size)
                yield return list.GetRange(i, Math.Min(size, list.Count - i));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing) CloseAllConnections();
            _disposed = true;
        }
        #region Published Reports

        public (bool IsPublished, int? PublishedReportId, DateTime? PublishedDateUtc, string PublishedByName)
            GetPublishStatus(int schoolId, int classId, string academicYear, int term)
        {
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT Id, PublishedDateUtc, PublishedByName, IsRevoked
                    FROM PublishedReports
                    WHERE SchoolId=@SchoolId AND ClassId=@ClassId
                      AND AcademicYear=@AcademicYear AND Term=@Term
                    LIMIT 1",
                    new Dictionary<string, object>
                    {
                        { "@SchoolId", schoolId },
                        { "@ClassId", classId },
                        { "@AcademicYear", academicYear ?? "" },
                        { "@Term", term }
                    });

                if (dt.Rows.Count == 0) return (false, null, null, null);

                bool revoked = dt.Rows[0]["IsRevoked"] != DBNull.Value
                               && Convert.ToBoolean(dt.Rows[0]["IsRevoked"]);

                if (revoked) return (false, null, null, null);

                return (true,
                        Convert.ToInt32(dt.Rows[0]["Id"]),
                        Convert.ToDateTime(dt.Rows[0]["PublishedDateUtc"]),
                        dt.Rows[0]["PublishedByName"]?.ToString() ?? "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPublishStatus error: {ex.Message}");
                return (false, null, null, null);
            }
        }

        public DataRow GetPublishedReportSnapshot(int studentId, string academicYear, int term)
        {
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT prd.* FROM PublishedReportData prd
                    INNER JOIN PublishedReports pr ON prd.PublishedReportId = pr.Id
                    WHERE prd.StudentId = @StudentId
                      AND pr.AcademicYear = @AcademicYear
                      AND pr.Term = @Term
                      AND pr.IsRevoked = 0
                    ORDER BY pr.PublishedDateUtc DESC
                    LIMIT 1",
                    new Dictionary<string, object>
                    {
                        { "@StudentId", studentId },
                        { "@AcademicYear", academicYear ?? "" },
                        { "@Term", term }
                    });

                return dt.Rows.Count == 0 ? null : dt.Rows[0];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPublishedReportSnapshot error: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Notifications

        public int InsertNotification(int schoolId, int userId, string type,
                                      string title, string body, string payloadJson = null)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@SyncId", Guid.NewGuid().ToString("N") },
                    { "@SchoolId", schoolId },
                    { "@UserId", userId },
                    { "@Type", type ?? "" },
                    { "@Title", title ?? "" },
                    { "@Body", body ?? "" },
                    { "@Payload", (object)payloadJson ?? DBNull.Value },
                    { "@Now", DateTime.UtcNow }
                };

                ExecuteNonQuery(@"
                    INSERT INTO Notifications
                        (SyncId, SchoolId, UserId, Type, Title, Body, PayloadJson, CreatedDateUtc, IsRead, IsPushed)
                    VALUES
                        (@SyncId, @SchoolId, @UserId, @Type, @Title, @Body, @Payload, @Now, 0, 0)", parameters);

                var idObj = ExecuteScalar("SELECT last_insert_rowid()");
                return idObj == null || idObj == DBNull.Value ? 0 : Convert.ToInt32(idObj);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertNotification error: {ex.Message}");
                return 0;
            }
        }

        public List<DataRow> GetUnreadNotificationsForUser(int userId, int limit = 50)
        {
            var result = new List<DataRow>();
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT * FROM Notifications
                    WHERE UserId = @UserId AND IsRead = 0
                    ORDER BY CreatedDateUtc DESC
                    LIMIT @Limit",
                    new Dictionary<string, object>
                    {
                        { "@UserId", userId },
                        { "@Limit", limit }
                    });

                foreach (DataRow row in dt.Rows) result.Add(row);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUnreadNotificationsForUser error: {ex.Message}");
            }
            return result;
        }

        public bool MarkNotificationRead(int notificationId)
        {
            try
            {
                return ExecuteNonQuery(
                    "UPDATE Notifications SET IsRead = 1 WHERE Id = @Id",
                    new Dictionary<string, object> { { "@Id", notificationId } }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarkNotificationRead error: {ex.Message}");
                return false;
            }
        }

        public List<DataRow> GetPendingPushNotifications(int schoolId, int limit = 200)
        {
            var result = new List<DataRow>();
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT * FROM Notifications
                    WHERE SchoolId = @SchoolId AND IsPushed = 0
                    ORDER BY CreatedDateUtc
                    LIMIT @Limit",
                    new Dictionary<string, object>
                    {
                        { "@SchoolId", schoolId },
                        { "@Limit", limit }
                    });

                foreach (DataRow row in dt.Rows) result.Add(row);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPendingPushNotifications error: {ex.Message}");
            }
            return result;
        }

        public bool MarkNotificationPushed(int notificationId)
        {
            try
            {
                return ExecuteNonQuery(
                    "UPDATE Notifications SET IsPushed = 1, PushedUtc = @Now WHERE Id = @Id",
                    new Dictionary<string, object>
                    {
                        { "@Id", notificationId },
                        { "@Now", DateTime.UtcNow }
                    }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarkNotificationPushed error: {ex.Message}");
                return false;
            }
        }

        #endregion
        #endregion

        private void EnsureSubscriptionPlansTable(SqliteConnection connection)
        {
            try
            {
                ExecuteNonQuery(@"
                    CREATE TABLE IF NOT EXISTS SubscriptionPlans (
                        Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        Audience        TEXT    NOT NULL,
                        PlanCode        TEXT    NOT NULL,
                        DisplayName     TEXT    NOT NULL,
                        Amount          DECIMAL(18,2) NOT NULL,
                        Currency        TEXT    NOT NULL DEFAULT 'SLL',
                        DurationDays    INTEGER NOT NULL,
                        GraceDays       INTEGER NOT NULL DEFAULT 7,
                        IsActive        INTEGER NOT NULL DEFAULT 1,
                        SortOrder       INTEGER NOT NULL DEFAULT 0,
                        UpdatedUtc      DATETIME NOT NULL,
                        UpdatedByUserId INTEGER,
                        UNIQUE(Audience, PlanCode)
                    )", null, connection);

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_plans_audience ON SubscriptionPlans(Audience, IsActive, SortOrder);",
                    null, connection);

                object existing = ExecuteScalar(
                    "SELECT COUNT(*) FROM SubscriptionPlans", null, connection);
                int count = existing == null || existing == DBNull.Value
                    ? 0 : Convert.ToInt32(existing);

                if (count == 0)
                {
                    var seeds = new[]
                    {
                        new { Audience = "school", PlanCode = "monthly", DisplayName = "Monthly — Le 50",  Amount = 50m,  Currency = "SLL", DurationDays = 30,  GraceDays = 7, SortOrder = 10 },
                        new { Audience = "school", PlanCode = "annual",  DisplayName = "Annual — Le 500",  Amount = 500m, Currency = "SLL", DurationDays = 365, GraceDays = 7, SortOrder = 20 },
                        new { Audience = "parent", PlanCode = "monthly", DisplayName = "Monthly — Le 50",  Amount = 50m,  Currency = "SLL", DurationDays = 30,  GraceDays = 7, SortOrder = 10 },
                        new { Audience = "parent", PlanCode = "annual",  DisplayName = "Annual — Le 500",  Amount = 500m, Currency = "SLL", DurationDays = 365, GraceDays = 7, SortOrder = 20 },
                    };

                    DateTime now = DateTime.UtcNow;
                    foreach (var p in seeds)
                    {
                        ExecuteNonQuery(@"
                            INSERT INTO SubscriptionPlans
                                (Audience, PlanCode, DisplayName, Amount, Currency,
                                 DurationDays, GraceDays, IsActive, SortOrder, UpdatedUtc)
                            VALUES
                                (@A, @P, @D, @Amt, @Cur, @Dur, @G, 1, @Sort, @Now)",
                            new Dictionary<string, object>
                            {
                                { "@A", p.Audience },
                                { "@P", p.PlanCode },
                                { "@D", p.DisplayName },
                                { "@Amt", p.Amount },
                                { "@Cur", p.Currency },
                                { "@Dur", p.DurationDays },
                                { "@G", p.GraceDays },
                                { "@Sort", p.SortOrder },
                                { "@Now", now }
                            }, connection);
                    }

                    Debug.WriteLine("SubscriptionPlans seeded with default school + parent plans.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSubscriptionPlansTable error: {ex.Message}");
            }
        }

    }
    public class SchoolResultTypeChangedEventArgs : EventArgs
    {
        public int SchoolId { get; set; }
        public string OldResultType { get; set; }
        public string NewResultType { get; set; }
        public DateTime ChangedAt { get; set; } = DateTime.Now;
    }
    public class TeacherAccessRow
    {
        public int Id { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public string Username { get; set; }
        public string Phone { get; set; }
        public string ClassName { get; set; }
        public string SubjectName { get; set; }
        public string AccessLevel { get; set; }
        public bool IsActive { get; set; }
        public DateTime AssignedDate { get; set; }
    }
}
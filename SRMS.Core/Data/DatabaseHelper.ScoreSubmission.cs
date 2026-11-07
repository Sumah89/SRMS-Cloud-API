using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;

namespace SRMS.Desktop.Data
{
    public partial class DatabaseHelper
    {
        public void EnsureScoreSubmissionSchema()
        {
            try
            {
                ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS ScoreSubmissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SyncId TEXT NOT NULL,
                    SchoolId INTEGER NOT NULL,
                    TeacherId INTEGER NOT NULL,
                    ClassId INTEGER NOT NULL,
                    SubjectId INTEGER NOT NULL,
                    Term INTEGER NOT NULL,
                    AcademicYear TEXT NOT NULL,
                    SubmittedUtc DATETIME NOT NULL,
                    UNIQUE(SchoolId, TeacherId, ClassId, SubjectId, Term, AcademicYear))");

                // Idempotent migration for pre-existing DBs
                try
                {
                    var cols = ExecuteQuery("PRAGMA table_info('ScoreSubmissions')");
                    bool hasSyncId = false;
                    foreach (DataRow r in cols.Rows)
                    {
                        if (string.Equals(r["name"]?.ToString(), "SyncId", StringComparison.OrdinalIgnoreCase))
                        { hasSyncId = true; break; }
                    }
                    if (!hasSyncId)
                    {
                        ExecuteNonQuery("ALTER TABLE ScoreSubmissions ADD COLUMN SyncId TEXT");
                        ExecuteNonQuery("UPDATE ScoreSubmissions SET SyncId = lower(hex(randomblob(16))) WHERE SyncId IS NULL");
                    }
                    ExecuteNonQuery("CREATE UNIQUE INDEX IF NOT EXISTS idx_scoresubmission_syncid ON ScoreSubmissions(SyncId) WHERE SyncId IS NOT NULL");
                }
                catch (Exception migEx)
                {
                    Debug.WriteLine($"ScoreSubmissions.SyncId migration: {migEx.Message}");
                }
            }
            catch (Exception ex) { Debug.WriteLine($"EnsureScoreSubmissionSchema error: {ex.Message}"); }
        }

        public bool IsScoreSubmitted(int schoolId, int teacherId, int classId, int subjectId, int term, string academicYear)
        {
            EnsureScoreSubmissionSchema();
            object value = ExecuteScalar(@"SELECT COUNT(*) FROM ScoreSubmissions
                WHERE SchoolId=@SchoolId AND TeacherId=@TeacherId AND ClassId=@ClassId
                AND SubjectId=@SubjectId AND Term=@Term AND AcademicYear=@AcademicYear", Values(
                ("@SchoolId", schoolId), ("@TeacherId", teacherId), ("@ClassId", classId),
                ("@SubjectId", subjectId), ("@Term", term), ("@AcademicYear", academicYear ?? "")));
            return Convert.ToInt32(value ?? 0) > 0;
        }

        public bool RecordScoreSubmission(int schoolId, int teacherId, int classId, int subjectId, int term, string academicYear)
        {
            EnsureScoreSubmissionSchema();
            return ExecuteNonQuery(@"INSERT OR IGNORE INTO ScoreSubmissions
                (SyncId, SchoolId, TeacherId, ClassId, SubjectId, Term, AcademicYear, SubmittedUtc)
                VALUES (@SyncId,@SchoolId,@TeacherId,@ClassId,@SubjectId,@Term,@AcademicYear,@SubmittedUtc)", Values(
                ("@SyncId", Guid.NewGuid().ToString("N")),
                ("@SchoolId", schoolId), ("@TeacherId", teacherId), ("@ClassId", classId),
                ("@SubjectId", subjectId), ("@Term", term), ("@AcademicYear", academicYear ?? ""),
                ("@SubmittedUtc", DateTime.UtcNow))) > 0;
        }

        public (bool IsSubmitted, int? SubmittedByTeacherId, DateTime? SubmittedUtc)
           GetScoreSubmissionStatus(int schoolId, int classId, int subjectId, int term, string academicYear)
        {
            EnsureScoreSubmissionSchema();
            try
            {
                var dt = ExecuteQuery(@"
                    SELECT TeacherId, SubmittedUtc FROM ScoreSubmissions
                    WHERE SchoolId=@SchoolId AND ClassId=@ClassId
                      AND SubjectId=@SubjectId AND Term=@Term AND AcademicYear=@AcademicYear
                    LIMIT 1", Values(
                        ("@SchoolId", schoolId), ("@ClassId", classId),
                        ("@SubjectId", subjectId), ("@Term", term),
                        ("@AcademicYear", academicYear ?? "")));

                if (dt.Rows.Count == 0)
                    return (false, null, null);

                return (true,
                        Convert.ToInt32(dt.Rows[0]["TeacherId"]),
                        Convert.ToDateTime(dt.Rows[0]["SubmittedUtc"]));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetScoreSubmissionStatus error: {ex.Message}");
                return (false, null, null);
            }
        }

        public bool RecordAdminProxySubmission(int schoolId, int teacherId, int classId,
                                               int subjectId, int term, string academicYear)
        {
            return RecordScoreSubmission(schoolId, teacherId, classId, subjectId, term, academicYear);
        }

        public bool RevokeScoreSubmission(int schoolId, int teacherId, int classId,
                                          int subjectId, int term, string academicYear)
        {
            EnsureScoreSubmissionSchema();
            return ExecuteNonQuery(@"DELETE FROM ScoreSubmissions
                WHERE SchoolId=@SchoolId AND TeacherId=@TeacherId AND ClassId=@ClassId
                AND SubjectId=@SubjectId AND Term=@Term AND AcademicYear=@AcademicYear",
                Values(
                    ("@SchoolId", schoolId), ("@TeacherId", teacherId), ("@ClassId", classId),
                    ("@SubjectId", subjectId), ("@Term", term), ("@AcademicYear", academicYear ?? ""))) > 0;
        }
        private static Dictionary<string, object> Values(params (string Key, object Value)[] values)
        {
            var result = new Dictionary<string, object>();
            foreach (var value in values) result[value.Key] = value.Value ?? DBNull.Value;
            return result;
        }
    }
}

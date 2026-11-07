using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Data
{
    /// <summary>
    /// Partial extension of DatabaseHelper that adds DailyAttendance (roll-call)
    /// helper methods. Kept in a separate file so the main DatabaseHelper.cs
    /// doesn't have to be touched.
    /// </summary>
    public partial class DatabaseHelper
    {
        // -----------------------------------------------------------------
        //  DAILY ATTENDANCE � SCHEMA SAFETY
        // -----------------------------------------------------------------

        /// <summary>
        /// Ensures the DailyAttendance table has everything we expect. Safe to
        /// call every startup � the table is already created by CreateAllTables,
        /// so this mostly acts as a defensive guard.
        /// </summary>
        public void EnsureDailyAttendanceSchema()
        {
            try
            {
                var cols = ExecuteQuery("PRAGMA table_info('DailyAttendance')");
                if (cols.Rows.Count == 0)
                {
                    ExecuteNonQuery(@"
                        CREATE TABLE IF NOT EXISTS DailyAttendance (
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
                            UNIQUE(StudentId, Date, AcademicYear))");
                }

                // Indexes (idempotent)
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_dailyattendance_student ON DailyAttendance(StudentId, AcademicYear);");
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_dailyattendance_date ON DailyAttendance(Date);");
                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_dailyattendance_status ON DailyAttendance(AcademicYear, Status);");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureDailyAttendanceSchema error: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        //  WRITE: UPSERT A SINGLE DAY FOR A SINGLE STUDENT
        // -----------------------------------------------------------------

        /// <summary>
        /// Upserts one attendance row. Uses the UNIQUE(StudentId, Date, AcademicYear)
        /// constraint to decide insert vs update.
        /// </summary>
        public bool SaveDailyAttendance(
            int studentId,
            DateTime date,
            string status,
            string academicYear,
            int? term = null,
            string remarks = null)
        {
            if (studentId <= 0) return false;
            if (string.IsNullOrWhiteSpace(status)) return false;
            if (string.IsNullOrWhiteSpace(academicYear)) return false;

            // Normalise to date-only so two calls on the same day always collide.
            DateTime day = date.Date;

            try
            {
                var existing = ExecuteScalar(
                    @"SELECT Id FROM DailyAttendance
                      WHERE StudentId = @Sid
                        AND DATE(Date) = DATE(@Day)
                        AND AcademicYear = @Year
                      LIMIT 1",
                    new Dictionary<string, object>
                    {
                        { "@Sid",  studentId },
                        { "@Day",  day.ToString("yyyy-MM-dd") },
                        { "@Year", academicYear.Trim() }
                    });

                if (existing != null && existing != DBNull.Value)
                {
                    return ExecuteNonQuery(
                        @"UPDATE DailyAttendance
                          SET Status = @Status, Term = @Term, Remarks = @Remarks,
                              ModifiedDate = @Now
                          WHERE Id = @Id",
                        new Dictionary<string, object>
                        {
                            { "@Id",      Convert.ToInt32(existing) },
                            { "@Status",  status },
                            { "@Term",    (object)term ?? DBNull.Value },
                            { "@Remarks", (object)remarks ?? DBNull.Value },
                            { "@Now",     DateTime.Now }
                        }) > 0;
                }

                return ExecuteNonQuery(
                    @"INSERT INTO DailyAttendance
                        (StudentId, Date, Status, AcademicYear, Term, Remarks,
                         CreatedDate, ModifiedDate)
                      VALUES
                        (@Sid, @Day, @Status, @Year, @Term, @Remarks, @Now, @Now)",
                    new Dictionary<string, object>
                    {
                        { "@Sid",     studentId },
                        { "@Day",     day.ToString("yyyy-MM-dd") },
                        { "@Status",  status },
                        { "@Year",    academicYear.Trim() },
                        { "@Term",    (object)term ?? DBNull.Value },
                        { "@Remarks", (object)remarks ?? DBNull.Value },
                        { "@Now",     DateTime.Now }
                    }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveDailyAttendance error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Upserts a whole day's roll-call in a single transaction. Much faster
        /// than calling SaveDailyAttendance N times.
        /// Key = studentId, Value = status string ("Present", "Absent", "Late").
        /// </summary>
        public int SaveDailyAttendanceBatch(
            IEnumerable<int> studentIds,
            DateTime date,
            IDictionary<int, string> statuses,
            string academicYear,
            int? term = null,
            string remarks = null)
        {
            if (studentIds == null || statuses == null) return 0;
            if (string.IsNullOrWhiteSpace(academicYear)) return 0;

            DateTime day = date.Date;
            int saved = 0;
            SqliteConnection connection = null;

            try
            {
                connection = GetConnection();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var sid in studentIds.Distinct())
                        {
                            if (sid <= 0) continue;
                            if (!statuses.TryGetValue(sid, out var status) ||
                                string.IsNullOrWhiteSpace(status))
                                continue;

                            object existing = ExecuteScalar(
                                @"SELECT Id FROM DailyAttendance
                                  WHERE StudentId = @Sid
                                    AND DATE(Date) = DATE(@Day)
                                    AND AcademicYear = @Year
                                  LIMIT 1",
                                new Dictionary<string, object>
                                {
                                    { "@Sid",  sid },
                                    { "@Day",  day.ToString("yyyy-MM-dd") },
                                    { "@Year", academicYear.Trim() }
                                },
                                connection);

                            if (existing != null && existing != DBNull.Value)
                            {
                                ExecuteNonQuery(
                                    @"UPDATE DailyAttendance
                                      SET Status = @Status, Term = @Term, Remarks = @Remarks,
                                          ModifiedDate = @Now
                                      WHERE Id = @Id",
                                    new Dictionary<string, object>
                                    {
                                        { "@Id",      Convert.ToInt32(existing) },
                                        { "@Status",  status },
                                        { "@Term",    (object)term ?? DBNull.Value },
                                        { "@Remarks", (object)remarks ?? DBNull.Value },
                                        { "@Now",     DateTime.Now }
                                    },
                                    connection);
                            }
                            else
                            {
                                ExecuteNonQuery(
                                    @"INSERT INTO DailyAttendance
                                        (StudentId, Date, Status, AcademicYear, Term, Remarks,
                                         CreatedDate, ModifiedDate)
                                      VALUES
                                        (@Sid, @Day, @Status, @Year, @Term, @Remarks, @Now, @Now)",
                                    new Dictionary<string, object>
                                    {
                                        { "@Sid",     sid },
                                        { "@Day",     day.ToString("yyyy-MM-dd") },
                                        { "@Status",  status },
                                        { "@Year",    academicYear.Trim() },
                                        { "@Term",    (object)term ?? DBNull.Value },
                                        { "@Remarks", (object)remarks ?? DBNull.Value },
                                        { "@Now",     DateTime.Now }
                                    },
                                    connection);
                            }

                            saved++;
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
                Debug.WriteLine($"SaveDailyAttendanceBatch error: {ex.Message}");
                return 0;
            }
            finally
            {
                if (connection != null) CloseConnection(connection);
            }

            return saved;
        }

        // -----------------------------------------------------------------
        //  READ: A SINGLE DAY FOR A CLASS
        // -----------------------------------------------------------------

        /// <summary>
        /// Loads the day's roll-call for every student in a class.
        /// Returns StudentId ? Status string ("Present", "Absent", "Late", or "").
        /// </summary>
        public Dictionary<int, string> GetDailyAttendanceForClass(
            int classId, DateTime date, string academicYear)
        {
            var result = new Dictionary<int, string>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear))
                return result;

            try
            {
                DateTime day = date.Date;

                DataTable dt = ExecuteQuery(@"
                    SELECT s.Id AS StudentId, IFNULL(d.Status,'') AS Status
                    FROM Students s
                    LEFT JOIN DailyAttendance d
                      ON d.StudentId = s.Id
                     AND DATE(d.Date) = DATE(@Day)
                     AND d.AcademicYear = @Year
                    WHERE s.ClassId = @Cid
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                    ORDER BY s.FullName",
                    new Dictionary<string, object>
                    {
                        { "@Cid",  classId },
                        { "@Day",  day.ToString("yyyy-MM-dd") },
                        { "@Year", academicYear.Trim() }
                    });

                foreach (DataRow row in dt.Rows)
                {
                    int sid = Convert.ToInt32(row["StudentId"]);
                    string status = row["Status"] == DBNull.Value ? "" : row["Status"].ToString();
                    result[sid] = status;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDailyAttendanceForClass error: {ex.Message}");
            }

            return result;
        }

        // -----------------------------------------------------------------
        //  READ: WEEK VIEW (rows = students, cols = 7 days)
        // -----------------------------------------------------------------

        /// <summary>
        /// Returns a class's attendance for a 7-day window.
        /// Outer key = studentId, inner key = date (yyyy-MM-dd), value = status.
        /// </summary>
        public Dictionary<int, Dictionary<string, string>> GetDailyAttendanceForWeek(
            int classId, DateTime weekStart, string academicYear)
        {
            var result = new Dictionary<int, Dictionary<string, string>>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear))
                return result;

            DateTime start = weekStart.Date;
            DateTime end = start.AddDays(6);

            try
            {
                DataTable dt = ExecuteQuery(@"
                    SELECT s.Id AS StudentId, d.Date AS AttendanceDate, d.Status
                    FROM Students s
                    LEFT JOIN DailyAttendance d
                      ON d.StudentId = s.Id
                     AND DATE(d.Date) BETWEEN DATE(@Start) AND DATE(@End)
                     AND d.AcademicYear = @Year
                    WHERE s.ClassId = @Cid
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                    ORDER BY s.FullName, d.Date",
                    new Dictionary<string, object>
                    {
                        { "@Cid",   classId },
                        { "@Start", start.ToString("yyyy-MM-dd") },
                        { "@End",   end.ToString("yyyy-MM-dd") },
                        { "@Year",  academicYear.Trim() }
                    });

                foreach (DataRow row in dt.Rows)
                {
                    int sid = Convert.ToInt32(row["StudentId"]);
                    if (!result.ContainsKey(sid))
                        result[sid] = new Dictionary<string, string>();

                    if (row["AttendanceDate"] == DBNull.Value) continue;

                    DateTime d = Convert.ToDateTime(row["AttendanceDate"]);
                    string key = d.ToString("yyyy-MM-dd");
                    string status = row["Status"] == DBNull.Value ? "" : row["Status"].ToString();
                    result[sid][key] = status;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDailyAttendanceForWeek error: {ex.Message}");
            }

            return result;
        }

        // -----------------------------------------------------------------
        //  READ: MONTH SUMMARY (per-student counts)
        // -----------------------------------------------------------------

        public sealed class DailyAttendanceSummaryRow
        {
            public int StudentId { get; set; }
            public string AdmissionNumber { get; set; }
            public string FullName { get; set; }
            public int PresentCount { get; set; }
            public int AbsentCount { get; set; }
            public int LateCount { get; set; }
            public int TotalMarked { get { return PresentCount + AbsentCount + LateCount; } }
            public decimal PresentPercent
            {
                get
                {
                    int total = TotalMarked;
                    return total == 0 ? 0m : (PresentCount + LateCount) * 100m / total;
                }
            }
        }

        /// <summary>
        /// Returns a per-student summary of Present/Absent/Late counts between
        /// <paramref name="from"/> and <paramref name="to"/> (inclusive).
        /// </summary>
        public List<DailyAttendanceSummaryRow> GetDailyAttendanceSummary(
            int classId, DateTime from, DateTime to, string academicYear)
        {
            var list = new List<DailyAttendanceSummaryRow>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear))
                return list;

            try
            {
                DataTable dt = ExecuteQuery(@"
                    SELECT s.Id   AS StudentId,
                           s.AdmissionNumber,
                           s.FullName,
                           SUM(CASE WHEN UPPER(d.Status) = 'PRESENT' THEN 1 ELSE 0 END) AS PresentCount,
                           SUM(CASE WHEN UPPER(d.Status) = 'ABSENT'  THEN 1 ELSE 0 END) AS AbsentCount,
                           SUM(CASE WHEN UPPER(d.Status) = 'LATE'    THEN 1 ELSE 0 END) AS LateCount
                    FROM Students s
                    LEFT JOIN DailyAttendance d
                      ON d.StudentId = s.Id
                     AND DATE(d.Date) BETWEEN DATE(@From) AND DATE(@To)
                     AND d.AcademicYear = @Year
                    WHERE s.ClassId = @Cid
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                    GROUP BY s.Id, s.AdmissionNumber, s.FullName
                    ORDER BY s.FullName",
                    new Dictionary<string, object>
                    {
                        { "@Cid",  classId },
                        { "@From", from.Date.ToString("yyyy-MM-dd") },
                        { "@To",   to.Date.ToString("yyyy-MM-dd") },
                        { "@Year", academicYear.Trim() }
                    });

                foreach (DataRow row in dt.Rows)
                {
                    list.Add(new DailyAttendanceSummaryRow
                    {
                        StudentId = ConvertToInt(row["StudentId"]),
                        AdmissionNumber = ConvertToString(row["AdmissionNumber"]),
                        FullName = ConvertToString(row["FullName"]),
                        PresentCount = ConvertToInt(row["PresentCount"]),
                        AbsentCount = ConvertToInt(row["AbsentCount"]),
                        LateCount = ConvertToInt(row["LateCount"])
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDailyAttendanceSummary error: {ex.Message}");
            }

            return list;
        }

        /// <summary>Deletes a single day's roll-call for one student.</summary>
        public bool ClearDailyAttendance(int studentId, DateTime date, string academicYear)
        {
            try
            {
                return ExecuteNonQuery(
                    @"DELETE FROM DailyAttendance
                      WHERE StudentId = @Sid
                        AND DATE(Date) = DATE(@Day)
                        AND AcademicYear = @Year",
                    new Dictionary<string, object>
                    {
                        { "@Sid",  studentId },
                        { "@Day",  date.Date.ToString("yyyy-MM-dd") },
                        { "@Year", academicYear.Trim() }
                    }) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClearDailyAttendance error: {ex.Message}");
                return false;
            }
        }

        // -----------------------------------------------------------------
        //  AGGREGATION: DailyAttendance ? per-student term totals
        // -----------------------------------------------------------------

        /// <summary>
        /// Aggregates DailyAttendance into Present/Absent/Late counts per student
        /// over a date range and term. Used by AttendanceForm's "Sync from Roll Call".
        /// </summary>
        public Dictionary<int, (int Present, int Absent, int Late)> GetDailyAttendanceTotals(
            int classId,
            DateTime from,
            DateTime to,
            string academicYear,
            int? term = null)
        {
            var result = new Dictionary<int, (int, int, int)>();
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear))
                return result;

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@Cid",   classId },
                    { "@From",  from.Date.ToString("yyyy-MM-dd") },
                    { "@To",    to.Date.ToString("yyyy-MM-dd") },
                    { "@Year",  academicYear.Trim() }
                };

                string termFilter = "";
                if (term.HasValue && term.Value > 0)
                {
                    termFilter = " AND (d.Term IS NULL OR d.Term = @Term)";
                    parameters["@Term"] = term.Value;
                }

                DataTable dt = ExecuteQuery($@"
                    SELECT s.Id AS StudentId,
                           SUM(CASE WHEN UPPER(d.Status) = 'PRESENT' THEN 1 ELSE 0 END) AS P,
                           SUM(CASE WHEN UPPER(d.Status) = 'ABSENT'  THEN 1 ELSE 0 END) AS A,
                           SUM(CASE WHEN UPPER(d.Status) = 'LATE'    THEN 1 ELSE 0 END) AS L
                    FROM Students s
                    LEFT JOIN DailyAttendance d
                      ON d.StudentId = s.Id
                     AND DATE(d.Date) BETWEEN DATE(@From) AND DATE(@To)
                     AND d.AcademicYear = @Year
                     {termFilter}
                    WHERE s.ClassId = @Cid
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                    GROUP BY s.Id",
                    parameters);

                foreach (DataRow row in dt.Rows)
                {
                    int sid = Convert.ToInt32(row["StudentId"]);
                    int p = row["P"] == DBNull.Value ? 0 : Convert.ToInt32(row["P"]);
                    int a = row["A"] == DBNull.Value ? 0 : Convert.ToInt32(row["A"]);
                    int l = row["L"] == DBNull.Value ? 0 : Convert.ToInt32(row["L"]);
                    result[sid] = (p, a, l);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDailyAttendanceTotals error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Total number of distinct days that ANY student in the class has a
        /// daily mark for, within the date range and optional term filter.
        /// Handy for cross-checking the "sessions" NumericUpDown against reality.
        /// </summary>
        public int GetDistinctMarkedDays(int classId, DateTime from, DateTime to,
                                          string academicYear, int? term = null)
        {
            if (classId <= 0 || string.IsNullOrWhiteSpace(academicYear)) return 0;

            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@Cid",  classId },
                    { "@From", from.Date.ToString("yyyy-MM-dd") },
                    { "@To",   to.Date.ToString("yyyy-MM-dd") },
                    { "@Year", academicYear.Trim() }
                };

                string termFilter = "";
                if (term.HasValue && term.Value > 0)
                {
                    termFilter = " AND (d.Term IS NULL OR d.Term = @Term)";
                    parameters["@Term"] = term.Value;
                }

                object r = ExecuteScalar($@"
                    SELECT COUNT(DISTINCT DATE(d.Date))
                    FROM DailyAttendance d
                    INNER JOIN Students s ON s.Id = d.StudentId
                    WHERE s.ClassId = @Cid
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND DATE(d.Date) BETWEEN DATE(@From) AND DATE(@To)
                      AND d.AcademicYear = @Year
                      {termFilter}",
                    parameters);

                return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDistinctMarkedDays error: {ex.Message}");
                return 0;
            }
        }
    }
}

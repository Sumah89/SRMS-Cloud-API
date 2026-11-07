using System;
using System.Collections.Generic;
using System.Data;
using SRMS.Desktop.Sync.Grants;

namespace SRMS.Desktop.Data
{
    public partial class DatabaseHelper
    {
        public static string DatabasePath => SRMS.Desktop.Helpers.ApplicationPathManager.DatabasePath;

        public void RemoveDeprecatedFinanceAndEmisTables()
        {
            // ── The DROP order and foreign-key handling matters here ──
            // PaymentDetails, PaymentReceipts and FeeAssignments all have FK
            // references to PaymentTransactions or FeeStructures. If we try to
            // drop the parent before the children, SQLite (with FK enforcement on)
            // refuses because the FK target vanishes mid-schema-recompute on a
            // brand-new database where the tables were just created seconds ago.
            //
            // Solution: turn FK enforcement off for the duration of this cleanup,
            // drop children before parents, then restore FK enforcement.
            try
            {
                ExecuteNonQuery("PRAGMA foreign_keys = OFF;");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveDeprecatedFinanceAndEmisTables: PRAGMA FK off failed: {ex.Message}");
            }

            try
            {
                // Children first
                ExecuteNonQuery("DROP TABLE IF EXISTS PaymentDetails");
                ExecuteNonQuery("DROP TABLE IF EXISTS PaymentReceipts");
                ExecuteNonQuery("DROP TABLE IF EXISTS FeeAssignments");
                ExecuteNonQuery("DROP TABLE IF EXISTS FeeStructures");
                ExecuteNonQuery("DROP TABLE IF EXISTS PaymentTransactions");
                ExecuteNonQuery("DROP TABLE IF EXISTS Expenses");

                // EMIS / trial artifacts
                ExecuteNonQuery("DROP TABLE IF EXISTS EMISStaff");
                ExecuteNonQuery("DROP TABLE IF EXISTS EMISTeacherLoads");
                ExecuteNonQuery("DROP TABLE IF EXISTS EMISTimelines");
                ExecuteNonQuery("DROP TABLE IF EXISTS EMISTimetables");
                ExecuteNonQuery("DROP TABLE IF EXISTS EMISClassPerformance");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveDeprecatedFinanceAndEmisTables error: {ex.Message}");
            }
            finally
            {
                try { ExecuteNonQuery("PRAGMA foreign_keys = ON;"); }
                catch { }
            }
        }

        public TeacherCodeLookupResult FindTeacherByCode(string phoneE164, string code)
        {
            var dt = ExecuteQuery(@"SELECT TeacherUserId, SchoolId FROM TeacherCodesV2
WHERE PhoneE164 = @Phone AND Code = @Code AND IsActive = 1 LIMIT 1",
                new Dictionary<string, object> { { "@Phone", phoneE164 }, { "@Code", code } });
            if (dt.Rows.Count == 0) return null;
            var row = dt.Rows[0];
            int userId = Convert.ToInt32(row["TeacherUserId"]);
            var user = GetUserByIdForCloud(userId);
            return new TeacherCodeLookupResult
            {
                Success = user != null,
                UserId = userId,
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                FullName = user?.FullName ?? "",
                Username = user?.Username ?? "",
                SchoolName = GetSchoolById(Convert.ToInt32(row["SchoolId"]))?.SchoolName ?? ""
            };
        }

        public ParentCodeLookupResult FindParentByCode(string phoneE164, string code)
        {
            var result = new ParentCodeLookupResult { Success = false, Children = new List<LinkedChild>() };
            var dt = ExecuteQuery(@"SELECT ParentUserId, SchoolId, StudentId FROM ParentCodesV2
WHERE PhoneE164 = @Phone AND Code = @Code AND IsActive = 1",
                new Dictionary<string, object> { { "@Phone", phoneE164 }, { "@Code", code } });
            if (dt.Rows.Count == 0) return result;

            int userId = Convert.ToInt32(dt.Rows[0]["ParentUserId"]);
            var user = GetUserByIdForCloud(userId);
            result.UserId = userId;
            result.FullName = user?.FullName ?? "";
            result.Success = user != null;
            foreach (DataRow row in dt.Rows)
            {
                var student = GetStudentById(Convert.ToInt32(row["StudentId"]));
                if (student == null) continue;
                var school = GetSchoolById(student.SchoolId);
                result.Children.Add(new LinkedChild
                {
                    StudentId = student.Id,
                    SchoolId = student.SchoolId,
                    StudentName = student.FullName,
                    AdmissionNumber = student.AdmissionNumber,
                    ClassName = GetClassById(student.ClassId)?.ClassName ?? "",
                    SchoolName = school?.SchoolName ?? ""
                });
            }
            return result;
        }

        public SRMS.Desktop.Models.User GetUserByIdForCloud(int userId)
        {
            var dt = ExecuteQuery("SELECT * FROM Users WHERE Id = @Id LIMIT 1",
                new Dictionary<string, object> { { "@Id", userId } });
            if (dt.Rows.Count == 0) return null;
            var row = dt.Rows[0];
            return new SRMS.Desktop.Models.User
            {
                Id = userId,
                Username = row["Username"]?.ToString(),
                FullName = row["FullName"]?.ToString(),
                Role = row["Role"]?.ToString(),
                SchoolId = row["SchoolId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["SchoolId"])
            };
        }
    }
}

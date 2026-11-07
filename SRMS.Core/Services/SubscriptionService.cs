using System;
using System.Collections.Generic;
using System.Data;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public sealed class SubscriptionService
    {
        private const int OfflineGraceDays = 7;
        private readonly DatabaseHelper _db;

        public SubscriptionService(DatabaseHelper db)
        {
            _db = db;
            _db.RemoveDeprecatedFinanceAndEmisTables();
            _db.MigrateActiveLicensesToSubscriptions();
        }

        public Subscription Get(int schoolId)
        {
            if (schoolId <= 0) return null;
            var table = _db.ExecuteQuery(
                "SELECT * FROM Subscriptions WHERE SchoolId = @SchoolId LIMIT 1",
                new Dictionary<string, object> { { "@SchoolId", schoolId } });
            if (table.Rows.Count == 0) return null;
            return Map(table.Rows[0]);
        }

        public Subscription GetOrCreateTrial(int schoolId)
        {
            return Get(schoolId);
        }

        public bool IsUsable(int schoolId, DateTime utcNow)
        {
            return Get(schoolId)?.IsUsable(utcNow) == true;
        }

        public void Upsert(Subscription subscription)
        {
            if (subscription == null || subscription.SchoolId <= 0) return;
            var now = DateTime.UtcNow;
            _db.ExecuteNonQuery(@"
INSERT INTO Subscriptions
(SchoolId, PlanCode, Status, StartedUtc, ExpiresUtc, GraceUntilUtc, Source, LegacyLicenseKey, UpdatedUtc)
VALUES (@SchoolId, @PlanCode, @Status, @StartedUtc, @ExpiresUtc, @GraceUntilUtc, @Source, @LegacyLicenseKey, @UpdatedUtc)
ON CONFLICT(SchoolId) DO UPDATE SET
PlanCode=@PlanCode, Status=@Status, StartedUtc=@StartedUtc, ExpiresUtc=@ExpiresUtc,
GraceUntilUtc=@GraceUntilUtc, Source=@Source, LegacyLicenseKey=@LegacyLicenseKey, UpdatedUtc=@UpdatedUtc",
                new Dictionary<string, object>
                {
                    { "@SchoolId", subscription.SchoolId },
                    { "@PlanCode", subscription.PlanCode ?? "standard" },
                    { "@Status", subscription.Status ?? "Active" },
                    { "@StartedUtc", subscription.StartedUtc == default ? now : subscription.StartedUtc },
                    { "@ExpiresUtc", (object)subscription.ExpiresUtc ?? DBNull.Value },
                    { "@GraceUntilUtc", (object)subscription.GraceUntilUtc ?? DBNull.Value },
                    { "@Source", subscription.Source ?? "admin" },
                    { "@LegacyLicenseKey", (object)subscription.LegacyLicenseKey ?? DBNull.Value },
                    { "@UpdatedUtc", now }
                });
        }

        private static Subscription Map(DataRow row)
        {
            return new Subscription
            {
                Id = Convert.ToInt32(row["Id"]),
                SchoolId = Convert.ToInt32(row["SchoolId"]),
                PlanCode = row["PlanCode"]?.ToString(),
                Status = row["Status"]?.ToString(),
                StartedUtc = Convert.ToDateTime(row["StartedUtc"]).ToUniversalTime(),
                ExpiresUtc = ToNullableDate(row["ExpiresUtc"]),
                GraceUntilUtc = ToNullableDate(row["GraceUntilUtc"]),
                Source = row["Source"]?.ToString(),
                LegacyLicenseKey = row["LegacyLicenseKey"] == DBNull.Value ? null : row["LegacyLicenseKey"]?.ToString(),
                UpdatedUtc = Convert.ToDateTime(row["UpdatedUtc"]).ToUniversalTime()
            };
        }

        private static DateTime? ToNullableDate(object value)
        {
            return value == null || value == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(value).ToUniversalTime();
        }
    }
}

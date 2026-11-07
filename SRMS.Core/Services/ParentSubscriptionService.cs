using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public sealed class ParentSubscriptionService
    {
        private readonly DatabaseHelper _db;

        public ParentSubscriptionService(DatabaseHelper db) { _db = db; }

        public ParentSubscription Get(int parentUserId)
        {
            if (parentUserId <= 0) return null;
            try
            {
                var dt = _db.ExecuteQuery(
                    "SELECT * FROM ParentSubscriptions WHERE ParentUserId = @P LIMIT 1",
                    new Dictionary<string, object> { { "@P", parentUserId } });
                return dt.Rows.Count == 0 ? null : Map(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParentSubscriptionService.Get error: {ex.Message}");
                return null;
            }
        }

        public ParentSubscription GetByReference(string paymentReference)
        {
            if (string.IsNullOrWhiteSpace(paymentReference)) return null;
            try
            {
                var dt = _db.ExecuteQuery(
                    "SELECT * FROM ParentSubscriptions WHERE PaymentReference = @R LIMIT 1",
                    new Dictionary<string, object> { { "@R", paymentReference.Trim() } });
                return dt.Rows.Count == 0 ? null : Map(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParentSubscriptionService.GetByReference error: {ex.Message}");
                return null;
            }
        }

        public bool IsUsable(int parentUserId, DateTime utcNow)
        {
            var sub = Get(parentUserId);
            return sub != null && sub.IsUsable(utcNow);
        }

        /// <summary>Insert or update the parent's subscription row. Returns the row Id.</summary>
        public int Upsert(ParentSubscription sub)
        {
            if (sub == null || sub.ParentUserId <= 0) return 0;

            var existing = Get(sub.ParentUserId);
            var now = DateTime.UtcNow;

            if (existing != null)
            {
                _db.ExecuteNonQuery(@"
                    UPDATE ParentSubscriptions SET
                        SchoolId = @SchoolId,
                        StudentId = @StudentId,
                        PlanCode = @PlanCode,
                        Amount = @Amount,
                        Currency = @Currency,
                        Provider = @Provider,
                        Status = @Status,
                        PaymentReference = @PaymentReference,
                        ProviderTransactionId = @ProviderTransactionId,
                        StartedUtc = @StartedUtc,
                        ExpiresUtc = @ExpiresUtc,
                        GraceUntilUtc = @GraceUntilUtc,
                        Source = @Source,
                        UpdatedUtc = @UpdatedUtc
                    WHERE ParentUserId = @ParentUserId",
                    new Dictionary<string, object>
                    {
                        { "@ParentUserId", sub.ParentUserId },
                        { "@SchoolId", sub.SchoolId },
                        { "@StudentId", (object)sub.StudentId ?? DBNull.Value },
                        { "@PlanCode", sub.PlanCode ?? "monthly" },
                        { "@Amount", sub.Amount },
                        { "@Currency", sub.Currency ?? "SLL" },
                        { "@Provider", sub.Provider ?? "OrangeMoney" },
                        { "@Status", sub.Status ?? "Pending" },
                        { "@PaymentReference", (object)sub.PaymentReference ?? DBNull.Value },
                        { "@ProviderTransactionId", (object)sub.ProviderTransactionId ?? DBNull.Value },
                        { "@StartedUtc", (object)sub.StartedUtc ?? DBNull.Value },
                        { "@ExpiresUtc", (object)sub.ExpiresUtc ?? DBNull.Value },
                        { "@GraceUntilUtc", (object)sub.GraceUntilUtc ?? DBNull.Value },
                        { "@Source", sub.Source ?? "mobile" },
                        { "@UpdatedUtc", now }
                    });
                return existing.Id;
            }

            var syncId = Guid.NewGuid().ToString("N");
            _db.ExecuteNonQuery(@"
                INSERT INTO ParentSubscriptions
                    (SyncId, SchoolId, ParentUserId, StudentId, PlanCode, Amount, Currency, Provider,
                     Status, PaymentReference, ProviderTransactionId,
                     StartedUtc, ExpiresUtc, GraceUntilUtc, Source, CreatedUtc, UpdatedUtc)
                VALUES
                    (@SyncId, @SchoolId, @ParentUserId, @StudentId, @PlanCode, @Amount, @Currency, @Provider,
                     @Status, @PaymentReference, @ProviderTransactionId,
                     @StartedUtc, @ExpiresUtc, @GraceUntilUtc, @Source, @CreatedUtc, @UpdatedUtc)",
                new Dictionary<string, object>
                {
                    { "@SyncId", syncId },
                    { "@SchoolId", sub.SchoolId },
                    { "@ParentUserId", sub.ParentUserId },
                    { "@StudentId", (object)sub.StudentId ?? DBNull.Value },
                    { "@PlanCode", sub.PlanCode ?? "monthly" },
                    { "@Amount", sub.Amount },
                    { "@Currency", sub.Currency ?? "SLL" },
                    { "@Provider", sub.Provider ?? "OrangeMoney" },
                    { "@Status", sub.Status ?? "Pending" },
                    { "@PaymentReference", (object)sub.PaymentReference ?? DBNull.Value },
                    { "@ProviderTransactionId", (object)sub.ProviderTransactionId ?? DBNull.Value },
                    { "@StartedUtc", (object)sub.StartedUtc ?? DBNull.Value },
                    { "@ExpiresUtc", (object)sub.ExpiresUtc ?? DBNull.Value },
                    { "@GraceUntilUtc", (object)sub.GraceUntilUtc ?? DBNull.Value },
                    { "@Source", sub.Source ?? "mobile" },
                    { "@CreatedUtc", now },
                    { "@UpdatedUtc", now }
                });

            var idObj = _db.ExecuteScalar("SELECT last_insert_rowid()");
            return idObj == null || idObj == DBNull.Value ? 0 : Convert.ToInt32(idObj);
        }

        /// <summary>
        /// Marks a Pending parent subscription as Active and stamps the plan's
        /// Start/Expiry dates. Called by the payment webhook.
        /// </summary>
        public ParentSubscription ConfirmPayment(string paymentReference, string providerTransactionId)
        {
            if (string.IsNullOrWhiteSpace(paymentReference)) return null;

            var sub = GetByReference(paymentReference);
            if (sub == null) return null;

            var now = DateTime.UtcNow;
            DateTime expires = string.Equals(sub.PlanCode, "annual", StringComparison.OrdinalIgnoreCase)
                ? now.AddYears(1)
                : now.AddMonths(1);

            _db.ExecuteNonQuery(@"
                UPDATE ParentSubscriptions SET
                    Status = 'Active',
                    ProviderTransactionId = @Tid,
                    StartedUtc = @Now,
                    ExpiresUtc = @Expires,
                    GraceUntilUtc = @Grace,
                    UpdatedUtc = @Now
                WHERE Id = @Id",
                new Dictionary<string, object>
                {
                    { "@Id", sub.Id },
                    { "@Tid", providerTransactionId ?? "placeholder-txn" },
                    { "@Now", now },
                    { "@Expires", expires },
                    { "@Grace", expires.AddDays(7) }
                });

            return Get(sub.ParentUserId);
        }

        private static ParentSubscription Map(DataRow r)
        {
            return new ParentSubscription
            {
                Id = Convert.ToInt32(r["Id"]),
                SyncId = r["SyncId"]?.ToString(),
                SchoolId = Convert.ToInt32(r["SchoolId"]),
                ParentUserId = Convert.ToInt32(r["ParentUserId"]),
                StudentId = r["StudentId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["StudentId"]),
                PlanCode = r["PlanCode"]?.ToString(),
                Amount = r["Amount"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Amount"]),
                Currency = r["Currency"]?.ToString(),
                Provider = r["Provider"]?.ToString(),
                Status = r["Status"]?.ToString(),
                PaymentReference = r["PaymentReference"] == DBNull.Value ? null : r["PaymentReference"].ToString(),
                ProviderTransactionId = r["ProviderTransactionId"] == DBNull.Value ? null : r["ProviderTransactionId"].ToString(),
                StartedUtc = r["StartedUtc"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["StartedUtc"]),
                ExpiresUtc = r["ExpiresUtc"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["ExpiresUtc"]),
                GraceUntilUtc = r["GraceUntilUtc"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["GraceUntilUtc"]),
                Source = r["Source"]?.ToString(),
                CreatedUtc = r["CreatedUtc"] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(r["CreatedUtc"]),
                UpdatedUtc = r["UpdatedUtc"] == DBNull.Value ? DateTime.UtcNow : Convert.ToDateTime(r["UpdatedUtc"])
            };
        }
    }
}
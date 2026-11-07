using System;
using System.Collections.Generic;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;
using SRMS.Desktop.Security;

namespace SRMS.Desktop.Services
{
    public sealed class SubscriptionRegistrationService
    {
        private readonly DatabaseHelper _db;

        public SubscriptionRegistrationService(DatabaseHelper db)
        {
            _db = db;
            EnsurePaymentTable();
        }

        public bool ConfirmPayment(string paymentReference, string providerTransactionId)
        {
            if (string.IsNullOrWhiteSpace(paymentReference)) return false;
            int updated = _db.ExecuteNonQuery(@"
UPDATE SubscriptionPayments
SET Status = 'Confirmed', ProviderTransactionId = @TransactionId, ConfirmedUtc = @Now
WHERE PaymentReference = @Reference AND Status = 'Pending'",
                new Dictionary<string, object>
                {
                    { "@Reference", paymentReference.Trim() },
                    { "@TransactionId", providerTransactionId ?? "placeholder-transaction" },
                    { "@Now", DateTime.UtcNow }
                });
            if (updated <= 0) return false;

            _db.ExecuteNonQuery(@"
UPDATE Subscriptions
SET Status = 'Active', StartedUtc = @Now, ExpiresUtc = CASE WHEN PlanCode = 'annual' THEN @Annual ELSE @Monthly END,
    GraceUntilUtc = CASE WHEN PlanCode = 'annual' THEN @AnnualGrace ELSE @MonthlyGrace END,
    Source = 'mobile-money-confirmed', UpdatedUtc = @Now
WHERE SchoolId = (SELECT SchoolId FROM SubscriptionPayments WHERE PaymentReference = @Reference)",
                new Dictionary<string, object>
                {
                    { "@Reference", paymentReference.Trim() },
                    { "@Now", DateTime.UtcNow },
                    { "@Monthly", DateTime.UtcNow.AddMonths(1) },
                    { "@Annual", DateTime.UtcNow.AddYears(1) },
                    { "@MonthlyGrace", DateTime.UtcNow.AddMonths(1).AddDays(7) },
                    { "@AnnualGrace", DateTime.UtcNow.AddYears(1).AddDays(7) }
                });
            _db.ExecuteNonQuery(@"
UPDATE Schools SET IsActive = 1, ModifiedDate = @Now
WHERE Id = (SELECT SchoolId FROM SubscriptionPayments WHERE PaymentReference = @Reference)",
                new Dictionary<string, object>
                {
                    { "@Reference", paymentReference.Trim() },
                    { "@Now", DateTime.UtcNow }
                });
            _db.ExecuteNonQuery(@"
UPDATE Users SET IsActive = 1, ModifiedDate = @Now
WHERE SchoolId = (SELECT SchoolId FROM SubscriptionPayments WHERE PaymentReference = @Reference)
  AND Role = 'Admin'",
                new Dictionary<string, object>
                {
                    { "@Reference", paymentReference.Trim() },
                    { "@Now", DateTime.UtcNow }
                });
            return true;
        }

        public SubscriptionRegistrationResult RegisterPending(SubscriptionRegistrationRequest request)
        {
            Validate(request);
            var now = DateTime.UtcNow;
            string paymentReference = "SRMS-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

            int schoolId = _db.ExecuteNonQueryWithId(@"
INSERT INTO Schools
(SchoolName, District, Town, PrincipalName, Phone, IsActive, ResultType, CreatedDate, ModifiedDate)
VALUES (@SchoolName, @District, @Town, @PrincipalName, @Contact, 0, 'FiveTests', @Now, @Now)",
                new Dictionary<string, object>
                {
                    { "@SchoolName", request.SchoolName.Trim() },
                    { "@District", request.District.Trim() },
                    { "@Town", request.Location.Trim() },
                    { "@PrincipalName", request.PrincipalName.Trim() },
                    { "@Contact", request.Contact.Trim() },
                    { "@Now", now }
                });

            if (schoolId <= 0) throw new InvalidOperationException("School registration could not be saved.");

            int userId = _db.ExecuteNonQueryWithId(@"
INSERT INTO Users
(SchoolId, Username, PasswordHash, FullName, Role, Phone, Email, IsActive, CreatedDate, ModifiedDate)
VALUES (@SchoolId, @Username, @PasswordHash, @FullName, 'Admin', @Contact, '', 0, @Now, @Now)",
                new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@Username", request.AdminUsername.Trim() },
                    { "@PasswordHash", PasswordHasher.HashPassword(request.AdminPassword) },
                    { "@FullName", request.AdminFullName.Trim() },
                    { "@Contact", request.Contact.Trim() },
                    { "@Now", now }
                });

            _db.ExecuteNonQuery(@"
INSERT INTO Subscriptions
(SchoolId, PlanCode, Status, StartedUtc, ExpiresUtc, GraceUntilUtc, Source, UpdatedUtc)
VALUES (@SchoolId, @PlanCode, 'Pending', @Now, NULL, NULL, 'mobile-money-pending', @Now)",
                new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@PlanCode", request.PlanCode },
                    { "@Now", now }
                });

            _db.ExecuteNonQuery(@"
INSERT INTO SubscriptionPayments
(SchoolId, Provider, PlanCode, Amount, Currency, PaymentReference, Status, CreatedUtc)
VALUES (@SchoolId, @Provider, @PlanCode, @Amount, @Currency, @Reference, 'Pending', @Now)",
                new Dictionary<string, object>
                {
                    { "@SchoolId", schoolId },
                    { "@Provider", request.Provider },
                    { "@PlanCode", request.PlanCode },
                    { "@Amount", request.Amount },
                    { "@Currency", request.Currency ?? "SLL" },
                    { "@Reference", paymentReference },
                    { "@Now", now }
                });

            return new SubscriptionRegistrationResult
            {
                SchoolId = schoolId,
                UserId = userId,
                PaymentReference = paymentReference,
                Status = "Pending",
                Activated = false,
                CreatedUtc = now
            };
        }

        private void EnsurePaymentTable()
        {
            _db.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS SubscriptionPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SchoolId INTEGER NOT NULL,
    Provider TEXT NOT NULL,
    PlanCode TEXT NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Currency TEXT NOT NULL,
    PaymentReference TEXT NOT NULL UNIQUE,
    ProviderTransactionId TEXT,
    Status TEXT NOT NULL DEFAULT 'Pending',
    CreatedUtc DATETIME NOT NULL,
    ConfirmedUtc DATETIME
)");
        }

        private static void Validate(SubscriptionRegistrationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SchoolName) || string.IsNullOrWhiteSpace(request.District)
                || string.IsNullOrWhiteSpace(request.Location) || string.IsNullOrWhiteSpace(request.PrincipalName)
                || string.IsNullOrWhiteSpace(request.Contact) || string.IsNullOrWhiteSpace(request.AdminFullName)
                || string.IsNullOrWhiteSpace(request.AdminUsername) || string.IsNullOrWhiteSpace(request.AdminPassword))
                throw new ArgumentException("All school and administrator fields are required.");
            if (!string.Equals(request.Provider, "OrangeMoney", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Provider, "AfriMoney", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Provider must be OrangeMoney or AfriMoney.");
            if (!string.Equals(request.PlanCode, "monthly", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.PlanCode, "annual", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Plan must be monthly or annual.");
            if (request.Amount <= 0) throw new ArgumentException("A positive subscription amount is required.");
        }
    }
}

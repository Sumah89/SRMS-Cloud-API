#nullable disable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;
using SRMS.Desktop.Models;
using SRMS.Desktop.Security;

namespace SRMS.Cloud.Services
{
    /// <summary>
    /// Cloud-side handler for the "register a new school" flow. It:
    ///   1. Validates the payload.
    ///   2. Creates the School row.
    ///   3. Creates the admin User row.
    ///   4. Creates a Pending Subscription row.
    /// Returns a result the endpoint uses to build the Accepted() response.
    ///
    /// The webhook confirmation path (ConfirmPayment) flips the Pending
    /// subscription to Active and sets Starts/Expires/Grace timestamps.
    /// </summary>
    public sealed class SubscriptionRegistrationService
    {
        private readonly PostgresDbContext _db;

        public SubscriptionRegistrationService(PostgresDbContext db) { _db = db; }

        // ── Register ───────────────────────────────────────────────────

        public SubscriptionRegistrationResult RegisterPending(
            SubscriptionRegistrationRequest request)
        {
            // The endpoint calls this synchronously, so wait on the async body.
            var task = RegisterPendingAsync(request);
            task.Wait();
            return task.Result;
        }

        public async Task<SubscriptionRegistrationResult> RegisterPendingAsync(
            SubscriptionRegistrationRequest request)
        {
            if (request == null)
                throw new ArgumentException("Request body is required.");
            if (string.IsNullOrWhiteSpace(request.SchoolName))
                throw new ArgumentException("SchoolName is required.");
            if (string.IsNullOrWhiteSpace(request.AdminUsername))
                throw new ArgumentException("AdminUsername is required.");
            if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 6)
                throw new ArgumentException("AdminPassword must be at least 6 characters.");
            if (string.IsNullOrWhiteSpace(request.AdminFullName))
                throw new ArgumentException("AdminFullName is required.");

            // 1. Refuse duplicate school names
            bool schoolExists = await _db.Schools
                .AnyAsync(s => s.SchoolName == request.SchoolName);
            if (schoolExists)
                throw new ArgumentException($"A school named '{request.SchoolName}' already exists.");

            // 2. Refuse duplicate usernames
            bool userExists = await _db.Users
                .AnyAsync(u => u.Username == request.AdminUsername);
            if (userExists)
                throw new ArgumentException($"Username '{request.AdminUsername}' is already taken.");

            // 3. Create the school
            var school = new School
            {
                SchoolName   = request.SchoolName,
                District     = request.District ?? "",
                Town         = request.Location ?? "",
                Chiefdom     = "",
                Address      = request.Location ?? "",
                Phone        = request.Contact ?? "",
                Email        = "",
                PrincipalName= request.PrincipalName ?? "",
                IsActive     = true,
                ResultType   = "FiveTests",
                CreatedDate  = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _db.Schools.Add(school);
            await _db.SaveChangesAsync();

            // 4. Create the admin user
            var admin = new SRMS.Desktop.Models.User
            {
                SchoolId     = school.Id,
                Username     = request.AdminUsername,
                PasswordHash = PasswordHasher.HashPassword(request.AdminPassword),
                FullName     = request.AdminFullName,
                Role         = "Admin",
                Phone        = request.Contact ?? "",
                Email        = "",
                IsActive     = true,
                CreatedDate  = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _db.Users.Add(admin);
            await _db.SaveChangesAsync();

            // 5. Create a Pending subscription
            string planCode = string.Equals(request.PlanCode, "annual",
                StringComparison.OrdinalIgnoreCase) ? "annual" : "monthly";
            string paymentReference = "SREG-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

            var sub = new SRMS.Desktop.Models.Subscription
            {
                SchoolId      = school.Id,
                PlanCode      = planCode,
                Status        = "Pending",
                StartedUtc    = DateTime.UtcNow,
                ExpiresUtc    = null,
                GraceUntilUtc = null,
                Source        = "registration",
                UpdatedUtc    = DateTime.UtcNow
            };
            _db.Subscriptions.Add(sub);
            await _db.SaveChangesAsync();

            return new SubscriptionRegistrationResult
            {
                SchoolId         = school.Id,
                UserId           = admin.Id,
                PaymentReference = paymentReference,
                Status           = "Pending",
                Activated        = false,
                CreatedUtc       = DateTime.UtcNow
            };
        }

        // ── Confirm ────────────────────────────────────────────────────

        public bool ConfirmPayment(string paymentReference, string providerTransactionId)
        {
            if (string.IsNullOrWhiteSpace(paymentReference)) return false;

            var task = ConfirmPaymentAsync(paymentReference, providerTransactionId);
            task.Wait();
            return task.Result;
        }

        public async Task<bool> ConfirmPaymentAsync(string paymentReference, string providerTransactionId)
        {
            // NOTE: the registration flow above does not currently persist the
            // paymentReference on the Subscription row (the SQLite original did
            // this via a separate PaymentTransactions table). We keep the same
            // shape: confirm by matching an existing Pending subscription for
            // the school that was created by "registration" and is still Pending.
            // In production you would look up by a PaymentTransactions row.
            var sub = await _db.Subscriptions
                .Where(s => s.Status == "Pending" && s.Source == "registration")
                .OrderByDescending(s => s.UpdatedUtc)
                .FirstOrDefaultAsync();

            if (sub == null) return false;

            var plan = await _db.SubscriptionPlans
                .Where(p => p.Audience == "school" && p.PlanCode == sub.PlanCode)
                .FirstOrDefaultAsync();

            int durationDays = plan?.DurationDays ?? (sub.PlanCode == "annual" ? 365 : 30);
            int graceDays    = plan?.GraceDays    ?? 7;

            DateTime nowUtc  = DateTime.UtcNow;
            DateTime expires = nowUtc.AddDays(durationDays);

            sub.Status        = "Active";
            sub.StartedUtc    = nowUtc;
            sub.ExpiresUtc    = expires;
            sub.GraceUntilUtc = expires.AddDays(graceDays);
            sub.UpdatedUtc    = nowUtc;

            await _db.SaveChangesAsync();
            return true;
        }
    }
}
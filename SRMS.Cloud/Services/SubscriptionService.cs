#nullable disable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;

namespace SRMS.Cloud.Services
{
    /// <summary>
    /// Cloud-side school subscription read/upsert against PostgreSQL.
    /// Replaces the SQLite-backed SRMS.Desktop.Services.SubscriptionService
    /// in the Cloud project only. Desktop &amp; Mobile keep using the original.
    /// </summary>
    public sealed class SubscriptionService
    {
        private readonly PostgresDbContext _db;

        public SubscriptionService(PostgresDbContext db) { _db = db; }

        /// <summary>Returns the current school subscription, or null if none.</summary>
        public SRMS.Desktop.Models.Subscription Get(int schoolId)
        {
            if (schoolId <= 0) return null;
            try
            {
                return GetAsync(schoolId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubscriptionService.Get] {ex.Message}");
                return null;
            }
        }

        public async Task<SRMS.Desktop.Models.Subscription> GetAsync(int schoolId)
        {
            if (schoolId <= 0) return null;
            return await _db.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId);
        }

        /// <summary>Insert-or-update the school subscription row.</summary>
        public void Upsert(SRMS.Desktop.Models.Subscription subscription)
        {
            if (subscription == null || subscription.SchoolId <= 0) return;
            try
            {
                UpsertAsync(subscription).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubscriptionService.Upsert] {ex.Message}");
            }
        }

        public async Task UpsertAsync(SRMS.Desktop.Models.Subscription subscription)
        {
            if (subscription == null || subscription.SchoolId <= 0) return;

            var existing = await _db.Subscriptions
                .FirstOrDefaultAsync(s => s.SchoolId == subscription.SchoolId);

            if (existing == null)
            {
                subscription.UpdatedUtc = DateTime.UtcNow;
                _db.Subscriptions.Add(subscription);
            }
            else
            {
                existing.PlanCode         = subscription.PlanCode ?? existing.PlanCode;
                existing.Status           = subscription.Status   ?? existing.Status;
                existing.StartedUtc       = subscription.StartedUtc;
                existing.ExpiresUtc       = subscription.ExpiresUtc;
                existing.GraceUntilUtc    = subscription.GraceUntilUtc;
                existing.Source           = subscription.Source ?? "admin";
                existing.LegacyLicenseKey = subscription.LegacyLicenseKey;
                existing.UpdatedUtc       = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        /// <summary>True if the school currently has an active or in-grace subscription.</summary>
        public bool IsActive(int schoolId)
        {
            var sub = Get(schoolId);
            return sub != null && sub.IsUsable(DateTime.UtcNow);
        }
    }
}
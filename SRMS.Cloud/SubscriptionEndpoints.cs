#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SRMS.Cloud.Data;
using SRMS.Desktop.Models;
using SRMS.Desktop.Services;                       // for SubscriptionRegistrationRequest / Result
using CloudSubSvc = SRMS.Cloud.Services.SubscriptionService;
using CloudRegSvc = SRMS.Cloud.Services.SubscriptionRegistrationService;

namespace SRMS.Cloud
{
    public static class SubscriptionEndpoints
    {
        public static void Map(WebApplication app)
        {
            // ══════════════════════════════════════════════════════════
            //  SCHOOL SUBSCRIPTION — REGISTER
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/subscription/register",
                async (SubscriptionRegistrationRequest request,
                       CloudRegSvc registrations,
                       PostgresDbContext db,
                       ILoggerFactory logFactory) =>
                {
                    var log = logFactory.CreateLogger("Subscription.Register");
                    try
                    {
                        string reqPlan = string.Equals(request?.PlanCode, "annual",
                            StringComparison.OrdinalIgnoreCase) ? "annual" : "monthly";

                        var plan = await db.SubscriptionPlans
                            .Where(p => p.Audience == "school" && p.PlanCode == reqPlan && p.IsActive)
                            .FirstOrDefaultAsync();

                        if (plan == null)
                            return Results.BadRequest(new { error = "unknown_plan", planCode = reqPlan });

                        if (request != null)
                        {
                            request.Amount   = plan.Amount;
                            request.Currency = plan.Currency;
                        }

                        var result = registrations.RegisterPending(request);

                        var response = new
                        {
                            result.SchoolId,
                            result.UserId,
                            result.PaymentReference,
                            result.Status,
                            result.Activated,
                            result.CreatedUtc,
                            amount     = plan.Amount,
                            currency   = plan.Currency,
                            checkoutUrl= (string)null
                        };

                        return Results.Accepted(
                            $"/api/subscription/payment/{result.PaymentReference}", response);
                    }
                    catch (ArgumentException argEx)
                    {
                        log.LogWarning(argEx, "Registration validation failed for {School}",
                            request?.SchoolName);
                        return Results.BadRequest(new { error = argEx.Message });
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Registration failed for {School}", request?.SchoolName);
                        return Results.Problem(
                            title: "Registration failed",
                            detail: ex.Message,
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                });

            // ══════════════════════════════════════════════════════════
            //  PUBLIC — PRICE LIST
            // ══════════════════════════════════════════════════════════
            app.MapGet("/api/subscription/plans",
                async (string audience, PostgresDbContext db) =>
                {
                    var q = db.SubscriptionPlans.Where(p => p.IsActive);
                    if (!string.IsNullOrWhiteSpace(audience))
                    {
                        string aud = audience.ToLowerInvariant();
                        q = q.Where(p => p.Audience == aud);
                    }

                    var rows = await q
                        .OrderBy(p => p.Audience)
                        .ThenBy(p => p.SortOrder)
                        .ThenBy(p => p.PlanCode)
                        .ToListAsync();

                    var plans = rows.Select(p => new
                    {
                        planCode     = p.PlanCode ?? "",
                        audience     = p.Audience ?? "",
                        displayName  = p.DisplayName ?? "",
                        amount       = p.Amount,
                        currency     = p.Currency ?? "SLL",
                        durationDays = p.DurationDays,
                        graceDays    = p.GraceDays
                    }).ToList();

                    return Results.Ok(new { plans, count = plans.Count, fetchedAtUtc = DateTime.UtcNow });
                });

            // ══════════════════════════════════════════════════════════
            //  SCHOOL SUBSCRIPTION — CHECKOUT SESSION (stub)
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/subscription/checkout",
                async (CheckoutRequest req, PostgresDbContext db) =>
                {
                    if (req == null || req.SchoolId <= 0)
                        return Results.BadRequest(new { error = "schoolId_required" });

                    string planCode = string.IsNullOrWhiteSpace(req.PlanCode)
                        ? "monthly" : req.PlanCode.ToLowerInvariant();

                    var plan = await db.SubscriptionPlans
                        .Where(p => p.Audience == "school" && p.PlanCode == planCode && p.IsActive)
                        .FirstOrDefaultAsync();

                    if (plan == null)
                        return Results.BadRequest(new { error = "unknown_plan", planCode });

                    return Results.Ok(new
                    {
                        schoolId    = req.SchoolId,
                        planCode,
                        amount      = plan.Amount,
                        currency    = plan.Currency,
                        checkoutUrl = (string)null,
                        ussdCode    = "*144#",
                        message     = "Dial *144# to pay with Orange Money, then return to the app."
                    });
                });

            // ══════════════════════════════════════════════════════════
            //  SCHOOL SUBSCRIPTION — WEBHOOK
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/subscription/webhook",
                (HttpRequest http, SubscriptionWebhookRequest request,
                 CloudRegSvc registrations,
                 IConfiguration configuration) =>
                {
                    string expectedToken = configuration["Payments:WebhookToken"];
                    if (string.IsNullOrWhiteSpace(expectedToken))
                        return Results.Problem(
                            "Payment webhook validation is not configured.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    if (!string.Equals(http.Headers["X-SRMS-Webhook-Token"].ToString(),
                                       expectedToken, StringComparison.Ordinal))
                        return Results.Unauthorized();
                    if (!string.Equals(request.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(request.Status, "successful", StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { error = "payment_not_confirmed" });
                    return registrations.ConfirmPayment(request.PaymentReference, request.ProviderTransactionId)
                        ? Results.Ok(new { status = "activated" })
                        : Results.NotFound(new { error = "payment_reference_not_found" });
                });

            // ══════════════════════════════════════════════════════════
            //  SCHOOL SUBSCRIPTION — STATUS
            // ══════════════════════════════════════════════════════════
            app.MapGet("/api/subscription/status",
                (HttpContext context, CloudSubSvc subscriptions) =>
                {
                    var role = context.User.FindFirstValue("role");
                    if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(role, "parent", StringComparison.OrdinalIgnoreCase))
                        return Results.Forbid();
                    if (!int.TryParse(context.User.FindFirstValue("school_id"), out var schoolId)
                        || schoolId <= 0)
                        return Results.Unauthorized();

                    var subscription = subscriptions.Get(schoolId);
                    if (subscription == null)
                        return Results.NotFound(new { error = "subscription_not_found" });

                    return Results.Ok(new
                    {
                        subscription,
                        usable       = subscription.IsUsable(DateTime.UtcNow),
                        checkedAtUtc = DateTime.UtcNow
                    });
                }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════
            //  SCHOOL SUBSCRIPTION — ADMIN UPSERT
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/admin/subscription",
                (HttpContext context, SubscriptionRequest request,
                 CloudSubSvc subscriptions) =>
                {
                    if (!string.Equals(context.User.FindFirstValue("role"), "admin",
                                       StringComparison.OrdinalIgnoreCase))
                        return Results.Forbid();
                    if (request.SchoolId <= 0)
                        return Results.BadRequest(new { error = "school_id_required" });
                    if (!int.TryParse(context.User.FindFirstValue("school_id"), out var callerSchoolId)
                        || callerSchoolId != request.SchoolId)
                        return Results.Forbid();

                    subscriptions.Upsert(new Subscription
                    {
                        SchoolId      = request.SchoolId,
                        PlanCode      = request.PlanCode ?? "standard",
                        Status        = request.Status ?? "Active",
                        StartedUtc    = request.StartedUtc ?? DateTime.UtcNow,
                        ExpiresUtc    = request.ExpiresUtc,
                        GraceUntilUtc = request.ExpiresUtc?.AddDays(7),
                        Source        = "admin"
                    });
                    return Results.Ok(subscriptions.Get(request.SchoolId));
                }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════
            //  PARENT SUBSCRIPTION — REGISTER
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/parent/subscription/register",
                async (ParentSubscriptionRequest req, PostgresDbContext db) =>
                {
                    if (req == null || string.IsNullOrWhiteSpace(req.Phone))
                        return Results.BadRequest(new { error = "phone_required" });

                    string phoneE164 = NormalizePhone(req.Phone);
                    if (string.IsNullOrEmpty(phoneE164))
                        return Results.BadRequest(new { error = "invalid_phone" });

                    int childCount = await db.Students
                        .CountAsync(s => s.ParentPhone == phoneE164);

                    if (childCount == 0)
                        return Results.NotFound(new
                        {
                            error   = "no_children_found",
                            message = "No students are linked to this phone number in any SRMS school."
                        });

                    var parent = await db.Parents.FirstOrDefaultAsync(p => p.PhoneE164 == phoneE164);
                    if (parent == null)
                    {
                        parent = new Parent
                        {
                            PhoneE164    = phoneE164,
                            FullName     = "Parent",
                            IsSubscribed = false,
                            CreatedUtc   = DateTime.UtcNow,
                            UpdatedUtc   = DateTime.UtcNow
                        };
                        db.Parents.Add(parent);
                        await db.SaveChangesAsync();
                    }

                    string planCode = string.Equals(req.PlanCode, "annual",
                        StringComparison.OrdinalIgnoreCase) ? "annual" : "monthly";

                    var plan = await db.SubscriptionPlans
                        .Where(p => p.Audience == "parent" && p.PlanCode == planCode && p.IsActive)
                        .FirstOrDefaultAsync();

                    if (plan == null)
                        return Results.BadRequest(new { error = "unknown_plan", planCode });

                    string provider = string.IsNullOrWhiteSpace(req.Provider) ? "OrangeMoney" : req.Provider;
                    string paymentReference = "PSUB-" + Guid.NewGuid().ToString("N").ToUpperInvariant();

                    var existingSub = await db.ParentSubscriptions
                        .FirstOrDefaultAsync(s => s.ParentUserId == parent.Id);

                    if (existingSub != null)
                    {
                        existingSub.SyncId           = Guid.NewGuid().ToString("N");
                        existingSub.PlanCode         = planCode;
                        existingSub.Amount           = plan.Amount;
                        existingSub.Currency         = plan.Currency;
                        existingSub.Provider         = provider;
                        existingSub.Status           = "Pending";
                        existingSub.PaymentReference = paymentReference;
                        existingSub.StartedUtc       = null;
                        existingSub.ExpiresUtc       = null;
                        existingSub.GraceUntilUtc    = null;
                        existingSub.UpdatedUtc       = DateTime.UtcNow;
                    }
                    else
                    {
                        db.ParentSubscriptions.Add(new ParentSubscription
                        {
                            SyncId           = Guid.NewGuid().ToString("N"),
                            ParentUserId     = parent.Id,
                            PlanCode         = planCode,
                            Amount           = plan.Amount,
                            Currency         = plan.Currency,
                            Provider         = provider,
                            Status           = "Pending",
                            PaymentReference = paymentReference,
                            CreatedUtc       = DateTime.UtcNow,
                            UpdatedUtc       = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync();

                    return Results.Ok(new
                    {
                        status           = "pending",
                        paymentReference,
                        parentId         = parent.Id,
                        childCount,
                        planCode,
                        amount           = plan.Amount,
                        currency         = plan.Currency,
                        checkoutUrl      = (string)null,
                        ussdCode         = "*144#",
                        message          = "Dial *144# to pay with Orange Money, then return to the app."
                    });
                });

            // ══════════════════════════════════════════════════════════
            //  PARENT SUBSCRIPTION — CONFIRM (webhook, needs PIN)
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/parent/subscription/confirm",
                async (HttpRequest http, ParentSubConfirmRequest req,
                       PostgresDbContext db, IConfiguration config) =>
                {
                    string expectedToken = config["Payments:WebhookToken"];
                    if (string.IsNullOrWhiteSpace(expectedToken))
                        return Results.Problem(
                            "Payment webhook validation is not configured.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    if (!string.Equals(http.Headers["X-SRMS-Webhook-Token"].ToString(),
                                       expectedToken, StringComparison.Ordinal))
                        return Results.Unauthorized();

                    if (!string.Equals(req.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(req.Status, "successful", StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { error = "payment_not_confirmed" });

                    if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length != 4)
                        return Results.BadRequest(new { error = "pin_must_be_4_digits" });

                    var sub = await db.ParentSubscriptions
                        .FirstOrDefaultAsync(s => s.PaymentReference == req.PaymentReference);
                    if (sub == null)
                        return Results.NotFound(new { error = "payment_reference_not_found" });

                    var plan = await db.SubscriptionPlans
                        .Where(p => p.Audience == "parent" && p.PlanCode == sub.PlanCode)
                        .FirstOrDefaultAsync();

                    int durationDays = plan?.DurationDays ?? (sub.PlanCode == "annual" ? 365 : 30);
                    int graceDays    = plan?.GraceDays    ?? 7;

                    DateTime nowUtc   = DateTime.UtcNow;
                    DateTime expires  = nowUtc.AddDays(durationDays);
                    DateTime grace    = expires.AddDays(graceDays);

                    var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == sub.ParentUserId);
                    if (parent != null)
                    {
                        parent.IsSubscribed = true;
                        parent.PinHash      = SRMS.Desktop.Security.PasswordHasher.HashPassword(req.Pin);
                        parent.UpdatedUtc   = nowUtc;
                    }

                    sub.Status        = "Active";
                    sub.StartedUtc    = nowUtc;
                    sub.ExpiresUtc    = expires;
                    sub.GraceUntilUtc = grace;
                    sub.UpdatedUtc    = nowUtc;

                    await db.SaveChangesAsync();
                    return Results.Ok(new { status = "activated", parentId = sub.ParentUserId });
                });

            // ══════════════════════════════════════════════════════════
            //  PARENT SUBSCRIPTION — SET PIN
            // ══════════════════════════════════════════════════════════
            app.MapPost("/api/parent/subscription/set-pin",
                async (ParentSetPinRequest req, PostgresDbContext db) =>
                {
                    if (req == null
                        || string.IsNullOrWhiteSpace(req.PaymentReference)
                        || string.IsNullOrWhiteSpace(req.Pin)
                        || req.Pin.Length != 4)
                        return Results.BadRequest(new
                        {
                            error = "paymentReference_and_4digit_pin_required"
                        });

                    var sub = await db.ParentSubscriptions
                        .FirstOrDefaultAsync(s => s.PaymentReference == req.PaymentReference);
                    if (sub == null)
                        return Results.NotFound(new { error = "payment_reference_not_found" });

                    if (!string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(sub.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new
                        {
                            error = "payment_not_confirmed_yet",
                            currentStatus = sub.Status
                        });

                    var plan = await db.SubscriptionPlans
                        .Where(p => p.Audience == "parent" && p.PlanCode == sub.PlanCode)
                        .FirstOrDefaultAsync();

                    int durationDays = plan?.DurationDays ?? (sub.PlanCode == "annual" ? 365 : 30);
                    int graceDays    = plan?.GraceDays    ?? 7;

                    DateTime nowUtc   = DateTime.UtcNow;
                    DateTime expires  = nowUtc.AddDays(durationDays);
                    DateTime grace    = expires.AddDays(graceDays);

                    var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == sub.ParentUserId);
                    if (parent != null)
                    {
                        parent.PinHash      = SRMS.Desktop.Security.PasswordHasher.HashPassword(req.Pin);
                        parent.IsSubscribed = true;
                        parent.UpdatedUtc   = nowUtc;
                    }

                    sub.StartedUtc    = sub.StartedUtc    ?? nowUtc;
                    sub.ExpiresUtc    = sub.ExpiresUtc    ?? expires;
                    sub.GraceUntilUtc = sub.GraceUntilUtc ?? grace;
                    sub.UpdatedUtc    = nowUtc;

                    await db.SaveChangesAsync();
                    return Results.Ok(new { status = "pin_set", parentId = sub.ParentUserId });
                });

            // ══════════════════════════════════════════════════════════
            //  PARENT SUBSCRIPTION — STATUS (auth)
            // ══════════════════════════════════════════════════════════
            app.MapGet("/api/parent/subscription/status",
                async (HttpContext ctx, PostgresDbContext db) =>
                {
                    if (!int.TryParse(ctx.User.FindFirstValue("user_id"), out var userId)
                        || userId <= 0)
                        return Results.Unauthorized();

                    var sub = await db.ParentSubscriptions.FirstOrDefaultAsync(s => s.ParentUserId == userId);
                    if (sub == null) return Results.NotFound(new { error = "no_subscription" });

                    var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == userId);

                    return Results.Ok(new
                    {
                        subscription = new
                        {
                            planCode         = sub.PlanCode ?? "",
                            status           = sub.Status ?? "",
                            amount           = sub.Amount,
                            currency         = sub.Currency ?? "",
                            paymentReference = sub.PaymentReference ?? "",
                            startedUtc       = sub.StartedUtc,
                            expiresUtc       = sub.ExpiresUtc
                        },
                        isSubscribed = parent?.IsSubscribed ?? false,
                        checkedAtUtc = DateTime.UtcNow
                    });
                }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════
            //  PARENT SUBSCRIPTION — UNAUTHENTICATED CHECK
            // ══════════════════════════════════════════════════════════
            app.MapGet("/api/parent/subscription/check",
                async (string reference, PostgresDbContext db) =>
                {
                    if (string.IsNullOrWhiteSpace(reference))
                        return Results.BadRequest(new { error = "reference_required" });

                    var sub = await db.ParentSubscriptions
                        .FirstOrDefaultAsync(s => s.PaymentReference == reference);
                    if (sub == null)
                        return Results.NotFound(new { error = "payment_reference_not_found" });

                    var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == sub.ParentUserId);
                    bool isActive = string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase);
                    bool hasPin   = parent != null && !string.IsNullOrEmpty(parent.PinHash);

                    return Results.Ok(new
                    {
                        status       = sub.Status ?? "Pending",
                        isActive,
                        hasPin,
                        isSubscribed = parent?.IsSubscribed ?? false,
                        planCode     = sub.PlanCode ?? "",
                        expiresUtc   = sub.ExpiresUtc,
                        checkedAtUtc = DateTime.UtcNow
                    });
                });

            // ══════════════════════════════════════════════════════════
            //  ADMIN — PRICE LIST
            // ══════════════════════════════════════════════════════════
            app.MapGet("/api/admin/pricing",
                async (HttpContext ctx, PostgresDbContext db) =>
                {
                    if (!string.Equals(ctx.User.FindFirstValue("role"), "admin",
                                       StringComparison.OrdinalIgnoreCase))
                        return Results.Forbid();

                    var rows = await db.SubscriptionPlans
                        .OrderBy(p => p.Audience)
                        .ThenBy(p => p.SortOrder)
                        .ThenBy(p => p.PlanCode)
                        .ToListAsync();

                    var plans = rows.Select(r => new
                    {
                        id              = r.Id,
                        audience        = r.Audience ?? "",
                        planCode        = r.PlanCode ?? "",
                        displayName     = r.DisplayName ?? "",
                        amount          = r.Amount,
                        currency        = r.Currency ?? "SLL",
                        durationDays    = r.DurationDays,
                        graceDays       = r.GraceDays,
                        isActive        = r.IsActive,
                        sortOrder       = r.SortOrder,
                        updatedUtc      = (DateTime?)r.UpdatedUtc,
                        updatedByUserId = r.UpdatedByUserId
                    }).ToList();

                    return Results.Ok(new { plans });
                }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════
            //  ADMIN — BULK UPDATE PRICE LIST
            // ══════════════════════════════════════════════════════════
            app.MapPut("/api/admin/pricing",
                async (HttpContext ctx, List<PricingPlanInput> plans, PostgresDbContext db) =>
                {
                    if (!string.Equals(ctx.User.FindFirstValue("role"), "admin",
                                       StringComparison.OrdinalIgnoreCase))
                        return Results.Forbid();
                    if (plans == null || plans.Count == 0)
                        return Results.BadRequest(new { error = "no_plans_supplied" });

                    int uid = int.TryParse(ctx.User.FindFirstValue("user_id"), out var u) ? u : 0;
                    DateTime now = DateTime.UtcNow;
                    var errors = new List<string>();

                    foreach (var p in plans)
                    {
                        if (p == null) { errors.Add("null_plan"); continue; }
                        if (string.IsNullOrWhiteSpace(p.Audience) || string.IsNullOrWhiteSpace(p.PlanCode))
                        { errors.Add("audience_and_planCode_required"); continue; }
                        if (p.Amount < 0) { errors.Add($"{p.PlanCode}: amount_must_be_non_negative"); continue; }
                        if (p.DurationDays <= 0) { errors.Add($"{p.PlanCode}: durationDays_must_be_positive"); continue; }

                        string aud  = p.Audience.ToLowerInvariant();
                        string code = p.PlanCode.ToLowerInvariant();

                        var row = await db.SubscriptionPlans
                            .FirstOrDefaultAsync(x => x.Audience == aud && x.PlanCode == code);

                        if (row == null)
                        {
                            db.SubscriptionPlans.Add(new SubscriptionPlan
                            {
                                Audience        = aud,
                                PlanCode        = code,
                                DisplayName     = p.DisplayName ?? p.PlanCode,
                                Amount          = p.Amount,
                                Currency        = string.IsNullOrWhiteSpace(p.Currency) ? "SLL" : p.Currency.ToUpperInvariant(),
                                DurationDays    = p.DurationDays,
                                GraceDays       = p.GraceDays,
                                IsActive        = p.IsActive,
                                SortOrder       = p.SortOrder,
                                UpdatedUtc      = now,
                                UpdatedByUserId = uid
                            });
                        }
                        else
                        {
                            row.DisplayName     = p.DisplayName ?? p.PlanCode;
                            row.Amount          = p.Amount;
                            row.Currency        = string.IsNullOrWhiteSpace(p.Currency) ? "SLL" : p.Currency.ToUpperInvariant();
                            row.DurationDays    = p.DurationDays;
                            row.GraceDays       = p.GraceDays;
                            row.IsActive        = p.IsActive;
                            row.SortOrder       = p.SortOrder;
                            row.UpdatedUtc      = now;
                            row.UpdatedByUserId = uid;
                        }
                    }

                    await db.SaveChangesAsync();

                    return errors.Count == 0
                        ? Results.Ok(new { status = "updated", count = plans.Count, updatedUtc = now })
                        : Results.BadRequest(new { error = "validation_failed", details = errors });
                }).RequireAuthorization();
        }

        // ── helpers ─────────────────────────────────────────────────

        private static string NormalizePhone(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in input) if (char.IsDigit(c)) sb.Append(c);
            string d = sb.ToString();
            if (d.StartsWith("232") && d.Length > 3) d = d.Substring(3);
            while (d.Length > 0 && d[0] == '0') d = d.Substring(1);
            if (d.Length != 8) return "";
            return "+232" + d;
        }
    }

    // ── records ────────────────────────────────────────────────────
    public sealed record SubscriptionRequest(
        int SchoolId,
        string PlanCode,
        string Status,
        DateTime? StartedUtc,
        DateTime? ExpiresUtc);

    public sealed record SubscriptionWebhookRequest(
        string Provider,
        string PaymentReference,
        string ProviderTransactionId,
        string Status);

    public sealed record ParentSubscriptionRequest(
        string Phone,
        string Code,
        string PlanCode,
        decimal? Amount,
        string Currency,
        string Provider);

    public sealed record ParentSubConfirmRequest(
        string PaymentReference,
        string Status,
        string Pin);

    public sealed record ParentSetPinRequest(
        string PaymentReference,
        string Pin);

    public sealed record CheckoutRequest(
        int SchoolId,
        string PlanCode);

    public sealed record PricingPlanInput(
        string Audience,
        string PlanCode,
        string DisplayName,
        decimal Amount,
        string Currency,
        int DurationDays,
        int GraceDays,
        bool IsActive,
        int SortOrder);
}
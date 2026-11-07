using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SRMS.Cloud;
using SRMS.Cloud.Data;
using SRMS.Desktop.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();

// ── JWT ──────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret missing from configuration");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "srms-cloud";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "srms-app";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

// ── App paths (still needed for photos, backups, logs) ──────────
var dataRoot = builder.Configuration["Cloud:DataRoot"]
    ?? throw new InvalidOperationException("Cloud:DataRoot missing from configuration");
builder.Services.AddSingleton<IAppPaths>(_ => new ServerAppPaths(dataRoot));

// ── PostgreSQL ───────────────────────────────────────────────────
var pgConn = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres missing from configuration. " +
        "Add it to appsettings.Production.json or User Secrets.");

builder.Services.AddDbContext<PostgresDbContext>(options =>
    options.UseNpgsql(pgConn, npgsql =>
    {
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        npgsql.CommandTimeout(60);
    }));

// ── Services ─────────────────────────────────────────────────────
builder.Services.AddSingleton(new JwtIssuer(jwtSecret, jwtIssuer, jwtAudience));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ChangeApplier>();
builder.Services.AddScoped<SRMS.Cloud.Services.SubscriptionService>();
builder.Services.AddScoped<SRMS.Cloud.Services.SubscriptionRegistrationService>();

// ── Background services ─────────────────────────────────────────
builder.Services.AddHostedService<SRMS.Cloud.Services.BackupBackgroundService>();

// ── Build ───────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware ──────────────────────────────────────────────────
app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// ── Postgres bootstrap (schema + master admin rotate) ───────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Postgres.Bootstrap");

        await PostgresSchema.EnsureAsync(db, logger);
        Log.Information("Postgres schema + seed + master admin rotation complete.");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "PostgreSQL bootstrap failed — aborting startup.");
        throw;
    }
}

// ── Endpoints ───────────────────────────────────────────────────
AdminEndpoints.Map(app);
ParentEndpoints.Map(app);
TeacherEndpoints.Map(app);
SubscriptionEndpoints.Map(app);
AuthEndpoints.Map(app);
SyncEndpoints.Map(app);
ReportEndpoints.Map(app);

app.MapGet("/health", async (PostgresDbContext db) =>
{
    string dbStatus = "unknown";
    try
    {
        dbStatus = await db.Database.CanConnectAsync() ? "ok" : "unreachable";
    }
    catch (Exception ex)
    {
        dbStatus = $"error: {ex.Message}";
    }

    return Results.Ok(new
    {
        status = "ok",
        service = "srms-cloud",
        utc = DateTime.UtcNow,
        env = app.Environment.EnvironmentName,
        database = new { provider = "postgresql", state = dbStatus }
    });
});

app.Run();
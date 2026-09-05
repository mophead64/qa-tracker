using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Admin;
using QaTracker.Web.Attachments;
using QaTracker.Web.Auth;
using QaTracker.Web.Components;
using QaTracker.Web.Components.Account;
using QaTracker.Web.Dashboard;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Hosting;
using QaTracker.Web.Logging;
using QaTracker.Web.Notifications;
using QaTracker.Web.Projects;
using QaTracker.Web.Storage;
using QaTracker.Web.Telemetry;
using QaTracker.Web.TestCases;

// Load a local .env file when present (development convenience). In hosted
// environments configuration comes from real environment variables / Key Vault.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// One compact line per log entry, prefixed with a full UTC (Zulu) timestamp. The
// per-request summary line is emitted by RequestLoggingMiddleware; framework and EF
// INFO chatter is filtered out in appsettings*.json.
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
});

// Export traces/metrics/logs to Azure Monitor or an OTLP endpoint when
// QATRACKER_TELEMETRY_PROVIDER is set; a no-op otherwise. Static assets are filtered
// out of traces (see StaticAssetFilter).
var telemetrySummary = builder.AddTelemetry();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Identity cookies always; a generic OpenID Connect handler too when
// QATRACKER_AUTH_PROVIDER selects Entra or Keycloak (local accounts still work alongside).
var authSummary = builder.AddAppAuthentication();
builder.Services.AddAuthorization();

var connectionString = DatabaseOptions.ResolveConnectionString(builder.Configuration);

// A context factory backs the Blazor components (short-lived context per operation),
// while a scoped shim satisfies Identity / Data Protection which expect ApplicationDbContext.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        // Ride out transient network / failover blips against a shared database.
        npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
        npgsql.CommandTimeout(30);
    }));
builder.Services.AddScoped<ApplicationDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Configure reverse-proxy header handling (opt-in via QATRACKER_FORWARDED_HEADERS); the
// middleware itself is added near the top of the pipeline below.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ForwardedHeadersConfig.Apply(options, builder.Configuration));

// Liveness (process up, no dependencies) and readiness (database reachable) probes.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyHealthCheck>("db", tags: [HealthCheckEndpoints.ReadyTag]);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFileStorage(builder.Configuration);
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<TestScopeService>();
builder.Services.AddScoped<TestCaseService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<DefectService>();
builder.Services.AddScoped<UserDirectory>();
builder.Services.AddScoped<ProjectActionsService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SystemStatsService>();

// Persist Data Protection keys (antiforgery, auth cookies) in the database so they
// survive container restarts and are shared across instances.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("QaTracker");

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Phase 1: basic local accounts, no email confirmation flow yet.
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AdditionalUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

var app = builder.Build();

// `dotnet QaTracker.Web.dll --migrate-only` runs migrations + seeding (under the startup
// advisory lock) and exits — for a dedicated migration step ahead of the app instances.
if (args.Contains("--migrate-only"))
{
    await app.InitializeDatabaseAsync();
    return;
}

// Apply migrations and seed roles / bootstrap admin before the host starts, so the
// schema exists before Data Protection or any request touches the database.
await app.InitializeDatabaseAsync();

app.Logger.LogInformation("Authentication: {AuthSummary}", authSummary);
app.Logger.LogInformation("Telemetry export: {TelemetrySummary}", telemetrySummary);

// Instantiate the OpenTelemetry self-diagnostics listener (if telemetry is on) so SDK
// export failures are logged; the singleton registration keeps it alive.
app.Services.GetService<QaTracker.Web.Telemetry.OpenTelemetryDiagnostics>();

// Configure the HTTP request pipeline.

// Apply X-Forwarded-* from the platform's TLS-terminating proxy before anything reads the
// request scheme or client IP (HSTS, HTTPS redirection, OIDC callback URLs, request logging).
if (ForwardedHeadersConfig.IsEnabled(app.Configuration))
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Emit one summary line per request (method, path, status, latency). Sits outside the
// rest of the pipeline so it also catches and logs unhandled exceptions.
app.UseRequestLogging();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// TLS is normally terminated at a reverse proxy in front of the container, so
// in-app HTTPS redirection is opt-in via QATRACKER_HTTPS_REDIRECT=true.
if (app.Configuration.GetValue("QATRACKER_HTTPS_REDIRECT", false))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// Platform probes. /health/live = process up (no dependencies); /health/ready = database
// reachable. Both anonymous and kept out of HTTP metrics; request logging and telemetry
// filter the /health* prefix (see HealthCheckEndpoints).
app.MapHealthChecks(HealthCheckEndpoints.Live, new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous()
    .DisableHttpMetrics();
app.MapHealthChecks(HealthCheckEndpoints.Ready,
        new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckEndpoints.ReadyTag) })
    .AllowAnonymous()
    .DisableHttpMetrics();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapProjectEndpoints();
app.MapTestCaseEndpoints();
app.MapAttachmentEndpoints();

app.Run();

public partial class Program;

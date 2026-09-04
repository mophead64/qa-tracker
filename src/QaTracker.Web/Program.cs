using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Components;
using QaTracker.Web.Components.Account;
using QaTracker.Web.Data;
using QaTracker.Web.Defects;
using QaTracker.Web.Logging;
using QaTracker.Web.Projects;
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

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.AddAuthorization();

var connectionString = DatabaseOptions.ResolveConnectionString(builder.Configuration);

// A context factory backs the Blazor components (short-lived context per operation),
// while a scoped shim satisfies Identity / Data Protection which expect ApplicationDbContext.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<ApplicationDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<TestScopeService>();
builder.Services.AddScoped<TestCaseService>();
builder.Services.AddScoped<DefectService>();
builder.Services.AddScoped<UserDirectory>();

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

// Apply migrations and seed roles / bootstrap admin before the host starts, so the
// schema exists before Data Protection or any request touches the database.
await app.InitializeDatabaseAsync();

// Configure the HTTP request pipeline.
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapProjectEndpoints();
app.MapTestCaseEndpoints();

app.Run();

public partial class Program;

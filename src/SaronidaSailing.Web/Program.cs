using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using SaronidaSailing.Infrastructure;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Health;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Web;
using SaronidaSailing.Web.Caching;
using SaronidaSailing.Web.Cli;
using SaronidaSailing.Web.Content;
using SaronidaSailing.Web.Health;
using SaronidaSailing.Web.Identity;
using SaronidaSailing.Web.Localization;

var isAdminCommand = AdminCommandRunner.IsAdminCommand(args);
var builder = WebApplication.CreateBuilder(isAdminCommand ? [] : args);

var logsPath = builder.Configuration["Logs:Path"];
if (string.IsNullOrWhiteSpace(logsPath))
{
    throw new InvalidOperationException("Logs:Path is not configured.");
}

builder.Logging.ClearProviders();
LogManager.Setup().LoadConfigurationFromSection(builder.Configuration);
LogManager.Configuration!.Variables["logDir"] = Path.GetFullPath(logsPath, builder.Environment.ContentRootPath);
builder.Logging.AddNLog();

ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options => ForwardedHeadersSetup.Configure(options, builder.Configuration));
builder.Services.AddRateLimiter(AdminLoginRateLimit.Configure);
builder.Services.AddRazorPages(options => options.Conventions.Add(new LocalizedPageRouteConvention()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILocalizedUrlService, LocalizedUrlService>();
builder.Services.AddScoped<IPageContentAccessor, PageContentAccessor>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureIdentity()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddOutputCache();
builder.Services.AddScoped<IContentService, ContentService>();
builder.Services.AddSingleton<IPageCacheInvalidator, OutputCachePageInvalidator>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminPolicy.Name, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AdminPolicy.MfaClaimType, AdminPolicy.MfaClaimValue));
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
    options.OnRefreshingPrincipal = MfaClaimPreserver.PreserveAsync;
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "saronida.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.LoginPath = "/admin/login";
    options.LogoutPath = "/admin/logout";
    options.AccessDeniedPath = "/admin/two-factor/setup";
});
builder.Services.ConfigureOptions<ConfigureTwoFactorUserIdCookie>();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keysPath))
{
    throw new InvalidOperationException("DataProtection:KeysPath is not configured.");
}

var keysDirectory = Directory.CreateDirectory(Path.GetFullPath(keysPath, builder.Environment.ContentRootPath));
builder.Services.AddDataProtection()
    .SetApplicationName("SaronidaSailing")
    .PersistKeysToFileSystem(keysDirectory);

builder.Services.AddHttpClient(HomePageHealthCheck.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHealthChecks()
    .AddInfrastructureHealthChecks()
    .AddCheck<HomePageHealthCheck>("home-page");

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (isAdminCommand)
{
    int exitCode;
    using (var scope = app.Services.CreateScope())
    {
        var runner = new AdminCommandRunner(
            scope.ServiceProvider.GetRequiredService<IAdminAccountService>(),
            scope.ServiceProvider.GetRequiredService<IAuditLog>(),
            new ConsolePasswordPrompt(),
            new ConsoleLineReader(),
            Console.Out,
            Console.Error);
        exitCode = await runner.RunAsync(args);
    }

    LogManager.Shutdown();
    return exitCode;
}

if (app.Environment.IsDevelopment())
{
    MediaFolder.EnsureExists(app.Services.GetRequiredService<IOptions<MediaOptions>>().Value.RootPath);
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture(SiteCultures.Default);
    options.AddSupportedCultures([.. SiteCultures.Supported]);
    options.AddSupportedUICultures([.. SiteCultures.Supported]);
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new UrlPrefixRequestCultureProvider());
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHangfireDashboardWithAuthorizationPolicy(AdminPolicy.Name, "/admin/jobs", new DashboardOptions
{
    DashboardTitle = "Saronida Sailing jobs",
    AppPath = "/",
    DisplayStorageConnectionString = false,
    Authorization = [],
});

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();

return 0;

public partial class Program;

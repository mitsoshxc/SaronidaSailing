using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
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
using SaronidaSailing.Web.Diagnostics;
using SaronidaSailing.Web.Endpoints;
using SaronidaSailing.Web.Health;
using SaronidaSailing.Web.Identity;
using SaronidaSailing.Web.Instagram;
using SaronidaSailing.Web.Localization;

var isAdminCommand = AdminCommandRunner.IsAdminCommand(args);
var isMigrateCommand = MigrateCommand.IsMigrateCommand(args);
var builder = WebApplication.CreateBuilder(isAdminCommand || isMigrateCommand ? [] : args);

var logsPath = builder.Configuration["Logs:Path"];
if (string.IsNullOrWhiteSpace(logsPath))
{
    throw new InvalidOperationException("Logs:Path is not configured.");
}

var logInstance = LogInstance.Resolve(builder.Configuration);

builder.Logging.ClearProviders();
LogManager.Setup().LoadConfigurationFromSection(builder.Configuration);
LogManager.Configuration!.Variables["logDir"] = Path.GetFullPath(logsPath, builder.Environment.ContentRootPath);
LogManager.Configuration.Variables["instance"] = logInstance;
builder.Logging.AddNLog();

CrashLogging.Register();

LifecycleLogger? lifecycle = null;

try
{
    ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), builder.Configuration);
    builder.Services.Configure<ForwardedHeadersOptions>(options => ForwardedHeadersSetup.Configure(options, builder.Configuration));
    builder.Services.AddRateLimiter(AdminLoginRateLimit.Configure);
    builder.Services.AddRazorPages(options =>
    {
        options.Conventions.Add(new LocalizedPageRouteConvention());
        if (builder.Environment.IsDevelopment())
        {
            options.Conventions.AddPageRoute("/Dev/CmsSandbox", "en/dev/cms-sandbox");
        }
    });
    builder.Services.Configure<WebEncoderOptions>(options =>
        options.TextEncoderSettings = new TextEncoderSettings(
            UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.GreekandCoptic, UnicodeRanges.GeneralPunctuation));
    builder.Services.AddSiteOptions(builder.Configuration);
    builder.Services.AddRuntimeDiagnostics(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ILocalizedUrlService, LocalizedUrlService>();
    builder.Services.AddScoped<IPageContentAccessor, PageContentAccessor>();
    builder.Services.AddScoped<ICmsEditContext, CmsEditContext>();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddInfrastructureIdentity()
        .AddSignInManager()
        .AddDefaultTokenProviders();
    builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
    builder.Services.AddScoped<IInstagramConnectionService, InstagramConnectionService>();
    builder.Services.AddScoped<IInstagramFeedService, InstagramFeedService>();
    builder.Services.AddScoped<IInstagramScheduleSync, InstagramScheduleSync>();
    builder.Services.AddScoped<ISectionActivationHandler, InstagramSectionHandler>();
    builder.Services.AddSingleton<InstagramOAuthState>();
    builder.Services.AddOutputCache(options => options.AddPolicy(CacheTags.PolicyName, policy => policy
        .Expire(TimeSpan.FromHours(1))
        .Tag(CacheTags.AllPages)
        .SetVaryByQuery([])));
    builder.Services.AddScoped<IContentService, ContentService>();
    builder.Services.AddSingleton<ICacheTagEvictor, OutputCacheTagEvictor>();
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
        options.Events.OnRedirectToLogin = AdminApiCookieEvents.RedirectToLogin;
        options.Events.OnRedirectToAccessDenied = AdminApiCookieEvents.RedirectToAccessDenied;
    });
    builder.Services.ConfigureOptions<ConfigureTwoFactorUserIdCookie>();
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "RequestVerificationToken";
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

    if (isMigrateCommand)
    {
        var migrateExitCode = await new MigrateCommand(app.Services, Console.Out, Console.Error).RunAsync(args);

        LogManager.Shutdown();
        return migrateExitCode;
    }

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

    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<IInstagramScheduleSync>().SyncAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        MediaFolder.EnsureExists(app.Services.GetRequiredService<IOptions<MediaOptions>>().Value);
    }

    app.UseForwardedHeaders();
    app.UseRequestDiagnostics();

    if (app.Environment.IsDevelopment())
    {
        app.UseDevelopmentMediaFiles(app.Services.GetRequiredService<IOptions<MediaOptions>>().Value.RootPath);
    }

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

    app.UseOutputCache();

    app.MapHangfireDashboardWithAuthorizationPolicy(AdminPolicy.Name, "/admin/jobs", new DashboardOptions
    {
        DashboardTitle = "Saronida Sailing jobs",
        AppPath = "/",
        DisplayStorageConnectionString = false,
        Authorization = [],
    });

    app.MapHealthChecks("/health");
    app.MapStaticAssets();
    app.MapContentEndpoints();
    app.MapMediaEndpoints();
    app.MapAdminEndpoints();
    app.MapRazorPages()
       .WithStaticAssets();

    lifecycle = LifecycleLogger.Attach(
        app.Lifetime, app.Environment, app.Services.GetRequiredService<TimeProvider>(), typeof(Program).Assembly);

    await app.RunAsync();

    return 0;
}
catch (Exception ex)
{
    CrashLogging.LogTermination(ex, lifecycle?.HasStarted == true);
    throw;
}

public partial class Program;

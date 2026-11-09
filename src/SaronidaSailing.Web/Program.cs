using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using SaronidaSailing.Infrastructure;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Infrastructure.Health;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Web.Health;
using SaronidaSailing.Web.Localization;

var builder = WebApplication.CreateBuilder(args);

var logsPath = builder.Configuration["Logs:Path"];
if (string.IsNullOrWhiteSpace(logsPath))
{
    throw new InvalidOperationException("Logs:Path is not configured.");
}

builder.Logging.ClearProviders();
LogManager.Setup().LoadConfigurationFromSection(builder.Configuration);
LogManager.Configuration!.Variables["logDir"] = Path.GetFullPath(logsPath, builder.Environment.ContentRootPath);
builder.Logging.AddNLog();

builder.Services.AddRazorPages(options => options.Conventions.Add(new LocalizedPageRouteConvention()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILocalizedUrlService, LocalizedUrlService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpClient(HomePageHealthCheck.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHealthChecks()
    .AddInfrastructureHealthChecks()
    .AddCheck<HomePageHealthCheck>("home-page");

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    MediaFolder.EnsureExists(app.Services.GetRequiredService<IOptions<MediaOptions>>().Value.RootPath);
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

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();

public partial class Program;

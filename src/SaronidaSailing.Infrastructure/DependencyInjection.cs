using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Caching;
using SaronidaSailing.Infrastructure.Content;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Logging;
using SaronidaSailing.Infrastructure.Media;
using SaronidaSailing.Infrastructure.Repositories;

namespace SaronidaSailing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Content");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Content' is not configured.");
        }

        var settings = new DatabaseSettings(configuration.GetValue<bool>(DatabaseSettings.MigrateAtStartupKey));
        services.AddSingleton(settings);

        services.AddOptions<MediaOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.RootPath = ResolveFolder(configuration, nameof(MediaOptions.RootPath), environment);
                options.OriginalsPath = ResolveFolder(configuration, nameof(MediaOptions.OriginalsPath), environment);
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Media:RootPath is not configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.OriginalsPath), "Media:OriginalsPath is not configured.")
            .Validate(options => !MediaFolder.IsInside(options.OriginalsPath, options.RootPath), "Media:OriginalsPath must not be inside Media:RootPath.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IRichTextSanitizer, RichTextSanitizer>();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IContentItemRepository, ContentItemRepository>();
        services.AddScoped<IContentListItemRepository, ContentListItemRepository>();
        services.AddScoped<IContentSectionRepository, ContentSectionRepository>();
        services.AddScoped<IContentHistoryRepository, ContentHistoryRepository>();
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();

        services.AddSingleton<ICacheEvictionBus>(_ => new PostgresCacheEvictionBus(connectionString));
        services.AddHostedService(sp => new PostgresCacheEvictionListener(
            connectionString,
            sp.GetRequiredService<ICacheTagEvictor>(),
            sp.GetRequiredService<ILogger<PostgresCacheEvictionListener>>()));

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                storage => storage.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = settings.MigrateAtStartup }));
        services.AddHangfireServer(options => options.WorkerCount = 2);

        return services;
    }

    private static string ResolveFolder(IConfiguration configuration, string key, IHostEnvironment environment)
    {
        var value = configuration[$"{MediaOptions.SectionName}:{key}"];
        return string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value, environment.ContentRootPath);
    }

    public static IdentityBuilder AddInfrastructureIdentity(this IServiceCollection services)
    {
        return services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>();
    }
}

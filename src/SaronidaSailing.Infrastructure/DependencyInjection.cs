using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Infrastructure.Data;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Logging;
using SaronidaSailing.Infrastructure.Media;

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

        var hangfirePath = configuration.GetConnectionString("Hangfire");
        if (string.IsNullOrWhiteSpace(hangfirePath))
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Hangfire' is not configured.");
        }

        services.AddOptions<MediaOptions>()
            .Configure(options => options.RootPath = configuration[$"{MediaOptions.SectionName}:{nameof(MediaOptions.RootPath)}"] ?? "")
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Media:RootPath is not configured.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        DatabaseDirectory.EnsureExistsForFile(hangfirePath);
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSQLiteStorage(hangfirePath));
        services.AddHangfireServer(options => options.WorkerCount = 2);

        return services;
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

using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;
using SaronidaSailing.Infrastructure.Data;

namespace SaronidaSailing.Tests;

[Collection(WebHostCollection.Name)]
public class InstagramScheduleTests(TempDatabaseWebApplicationFactory factory) : IClassFixture<TempDatabaseWebApplicationFactory>
{
    private static readonly ContentEditor Editor = new("schedule@example.com", null);

    [Fact]
    public void Register_CreatesBothJobsWithTheCronAndTimeZone()
    {
        var scheduler = Scheduler();
        scheduler.Remove();

        scheduler.Register();

        var jobs = RecurringJobs();
        Assert.Equal(2, jobs.Count);
        var fetch = jobs.Single(j => j.Id == InstagramJobIds.Fetch);
        var refresh = jobs.Single(j => j.Id == InstagramJobIds.TokenRefresh);
        Assert.Equal("0 6,18 * * *", fetch.Cron);
        Assert.Equal("0 4 * * 1", refresh.Cron);
        Assert.Equal("Europe/Athens", fetch.TimeZoneId);
        Assert.Equal("Europe/Athens", refresh.TimeZoneId);
    }

    [Fact]
    public void Register_Twice_KeepsExactlyTwoJobs()
    {
        var scheduler = Scheduler();

        scheduler.Register();
        scheduler.Register();

        Assert.Equal(
            new[] { InstagramJobIds.Fetch, InstagramJobIds.TokenRefresh }.Order(),
            RecurringJobs().Select(j => j.Id).Order());
    }

    [Fact]
    public void Remove_DeletesBothJobs_AndIsHarmlessWhenNonePresent()
    {
        var scheduler = Scheduler();
        scheduler.Register();

        scheduler.Remove();
        var afterFirst = RecurringJobs();
        scheduler.Remove();

        Assert.Empty(afterFirst);
        Assert.Empty(RecurringJobs());
    }

    [Fact]
    public async Task HidingAndShowingTheSection_RemovesAndRegistersTheJobs()
    {
        await SetSectionAsync(true);
        Scheduler().Register();

        await SetSectionAsync(false);
        var afterHide = RecurringJobs();
        await SetSectionAsync(true);
        var afterShow = RecurringJobs();

        Assert.Empty(afterHide);
        Assert.Equal(2, afterShow.Count);
    }

    [Fact]
    public async Task SettingTheSectionToItsCurrentState_LeavesTheJobsAlone()
    {
        await SetSectionAsync(false);
        Scheduler().Register();

        await SetSectionAsync(false);

        Assert.Equal(2, RecurringJobs().Count);
        await SetSectionAsync(true);
    }

    [Fact]
    public async Task ChangingAnotherSection_LeavesTheJobsAlone()
    {
        await SetSectionAsync(true);
        Scheduler().Register();

        await ApplyAsync(new SetSectionActive("home.hero", false));
        await ApplyAsync(new SetSectionActive("home.hero", true));

        Assert.Equal(2, RecurringJobs().Count);
    }

    [Fact]
    public async Task Sync_NoSectionRow_Registers()
    {
        await DeleteSectionRowsAsync();
        Scheduler().Remove();

        var active = await SyncAsync();

        Assert.True(active);
        Assert.Equal(2, RecurringJobs().Count);
    }

    [Fact]
    public async Task Sync_InactiveRow_Removes()
    {
        await SetSectionAsync(false);
        Scheduler().Register();

        var active = await SyncAsync();

        Assert.False(active);
        Assert.Empty(RecurringJobs());
        await SetSectionAsync(true);
    }

    [Fact]
    public async Task Sync_ActiveRow_Registers()
    {
        await SetSectionAsync(true);
        Scheduler().Remove();

        var active = await SyncAsync();

        Assert.True(active);
        Assert.Equal(2, RecurringJobs().Count);
    }

    private IInstagramJobScheduler Scheduler() => factory.Services.GetRequiredService<IInstagramJobScheduler>();

    private List<RecurringJobDto> RecurringJobs()
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetRecurringJobs();
    }

    private Task SetSectionAsync(bool active) => ApplyAsync(new SetSectionActive(InstagramSection.Key, active));

    private async Task ApplyAsync(params ContentChange[] changes)
    {
        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IContentService>().ApplyChangesAsync(changes, Editor);
        Assert.True(result.Succeeded);
    }

    private async Task<bool> SyncAsync()
    {
        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInstagramScheduleSync>().SyncAsync();
    }

    private async Task DeleteSectionRowsAsync()
    {
        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ContentSection>().ExecuteDeleteAsync();
    }
}

[Collection(WebHostCollection.Name)]
public class InstagramScheduleStartupTests
{
    [Fact]
    public void Startup_WithNoSectionRow_LeavesTheJobsRegistered()
    {
        using var host = new TempDatabaseWebApplicationFactory();
        using var _ = host.CreateClient();

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        Assert.Equal(
            new[] { InstagramJobIds.Fetch, InstagramJobIds.TokenRefresh }.Order(),
            connection.GetRecurringJobs().Select(j => j.Id).Order());
    }

    [Fact]
    public async Task Startup_WithAnInactiveSection_RemovesTheJobs()
    {
        using var first = new TempDatabaseWebApplicationFactory();
        using var _ = first.CreateClient();
        await using (var scope = first.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<ContentSection>().Add(ContentSection.Create("home", "instagram", isActive: false));
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IInstagramJobScheduler>().Register();
        }

        using var second = new TempDatabaseWebApplicationFactory();
        second.UseDatabaseOf(first);
        using var __ = second.CreateClient();

        using var connection = second.Services.GetRequiredService<JobStorage>().GetConnection();
        Assert.Empty(connection.GetRecurringJobs());
    }
}

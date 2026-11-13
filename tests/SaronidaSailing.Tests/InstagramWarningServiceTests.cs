using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Tests;

public class InstagramWarningServiceTests
{
    [Theory]
    [InlineData(false, InstagramConnectionState.NotConnected, InstagramWarning.None)]
    [InlineData(false, InstagramConnectionState.Disconnected, InstagramWarning.None)]
    [InlineData(false, InstagramConnectionState.Connected, InstagramWarning.None)]
    [InlineData(true, InstagramConnectionState.NotConnected, InstagramWarning.NotConnected)]
    [InlineData(true, InstagramConnectionState.Disconnected, InstagramWarning.Disconnected)]
    [InlineData(true, InstagramConnectionState.Connected, InstagramWarning.None)]
    public async Task Warning_FollowsTheSectionAndTheConnectionState(
        bool sectionActive, InstagramConnectionState state, InstagramWarning expected)
    {
        var service = new InstagramWarningService(new FakeSchedule(sectionActive), new FakeConnection(state, canConnect: true));

        Assert.Equal(expected, await service.GetToolbarWarningAsync());
    }

    [Fact]
    public async Task Warning_IgnoresWhetherTheServerIsConfigured()
    {
        var service = new InstagramWarningService(
            new FakeSchedule(true), new FakeConnection(InstagramConnectionState.NotConnected, canConnect: false));

        Assert.Equal(InstagramWarning.NotConnected, await service.GetToolbarWarningAsync());
    }

    private sealed class FakeSchedule(bool active) : IInstagramScheduleSync
    {
        public Task<bool> IsSectionActiveAsync(CancellationToken ct = default) => Task.FromResult(active);

        public Task<bool> SyncAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeConnection(InstagramConnectionState state, bool canConnect) : IInstagramConnectionService
    {
        public Task<InstagramStatus> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new InstagramStatus(state, canConnect, null, null, null, null));

        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<InstagramConnectResult> CompleteConnectAsync(
            string code, string redirectUri, string adminEmail, string? ipAddress, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(string adminEmail, string? ipAddress, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkTokenInvalidAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<InstagramRefreshResult> RefreshTokenAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}

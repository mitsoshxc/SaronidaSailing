using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaronidaSailing.Core.Interfaces;
using SaronidaSailing.Infrastructure.Instagram;

namespace SaronidaSailing.Tests;

public sealed class InstagramOptionsTests
{
    [Fact]
    public void NoConfiguration_StartsFine_WithTheDefaultUrls()
    {
        using var provider = Build(new Dictionary<string, string?>());

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<InstagramOptions>>().Value;

        Assert.Equal("https://www.instagram.com/oauth/authorize", options.AuthorizeUrl);
        Assert.Equal("https://api.instagram.com", options.ApiBaseUrl);
        Assert.Equal("https://graph.instagram.com", options.GraphBaseUrl);
    }

    [Theory]
    [InlineData("Instagram:AuthorizeUrl", "not a url")]
    [InlineData("Instagram:AuthorizeUrl", "/relative/path")]
    [InlineData("Instagram:ApiBaseUrl", "ftp://api.example.test")]
    [InlineData("Instagram:GraphBaseUrl", "")]
    public void InvalidUrl_FailsValidationOnStart(string key, string value)
    {
        using var provider = Build(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void LocalHttpUrls_AreAccepted()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Instagram:AuthorizeUrl"] = "http://localhost:5001/fake/authorize",
            ["Instagram:ApiBaseUrl"] = "http://localhost:5001/fake",
            ["Instagram:GraphBaseUrl"] = "http://localhost:5001/fake",
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public async Task HttpClientLogs_NeverContainTheTokenOrTheSecret()
    {
        var logs = new CapturingProvider();
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddInstagram(configuration)
            .ConfigurePrimaryHttpMessageHandler(() => new InstagramApiClientTests.StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "access_token": "LONG", "expires_in": 60 }"""),
            }));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IInstagramApiClient>().ExchangeForLongLivedAsync(new InstagramAppCredentials("1234567890", "SECRET-xyz"), "TOKEN-abc-12345");

        var all = logs.Messages;
        Assert.Contains(all, m => m.Contains("graph.instagram.com"));
        Assert.DoesNotContain(all, m => m.Contains("TOKEN-abc-12345"));
        Assert.DoesNotContain(all, m => m.Contains("SECRET-xyz"));
        Assert.DoesNotContain(all, m => m.Contains("access_token="));
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInstagram(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }

        private sealed class CapturingLogger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Add(state.ToString() ?? "");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add(formatter(state, exception) + (exception is null ? "" : exception.ToString()));
        }
    }
}

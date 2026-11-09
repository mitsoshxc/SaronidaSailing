using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using SaronidaSailing.Core.Entities;
using SaronidaSailing.Core.Interfaces;

namespace SaronidaSailing.Tests;

public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Compute(string base32Key, DateTimeOffset time)
    {
        var key = Decode(base32Key);
        var counter = time.ToUnixTimeSeconds() / 30;
        var message = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public static string Current(string base32Key) => Compute(base32Key, DateTimeOffset.UtcNow);

    public static string Invalid(string base32Key)
    {
        var now = DateTimeOffset.UtcNow;
        var valid = Enumerable.Range(-5, 11).Select(step => Compute(base32Key, now.AddSeconds(step * 30))).ToHashSet();
        return Enumerable.Range(0, 1_000_000).Select(n => n.ToString("D6")).First(code => !valid.Contains(code));
    }

    private static byte[] Decode(string base32)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var c in base32.Replace(" ", "").ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }
}

public sealed record TwoFactorAdmin(string Email, string Key, IReadOnlyList<string> RecoveryCodes);

public static class AdminTestHelpers
{
    public const string Password = "correct-horse-battery";

    private static int _clientCounter;

    public static HttpClient NewClient(WebApplicationFactory<Program> factory)
    {
        var n = Interlocked.Increment(ref _clientCounter);
        return NewClientFromIp(factory, $"10.200.{n / 250}.{n % 250 + 1}");
    }

    public static HttpClient NewClientFromIp(WebApplicationFactory<Program> factory, string remoteIp, Uri? baseAddress = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = baseAddress ?? new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.HeaderName, remoteIp);
        return client;
    }

    public static async Task AddAdminAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAdminAccountService>().AddAsync(email, Password);
        Assert.True(result.Succeeded);
    }

    public static async Task<TwoFactorAdmin> AddTwoFactorAdminAsync(WebApplicationFactory<Program> factory, string email)
    {
        await AddAdminAsync(factory, email);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!;
        return new TwoFactorAdmin(email, key, [.. codes]);
    }

    public static string ExtractToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "antiforgery token not found");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, params (string Name, string Value)[] fields)
    {
        var form = await client.GetAsync(url);
        form.EnsureSuccessStatusCode();
        var data = fields.ToDictionary(f => f.Name, f => f.Value);
        data["__RequestVerificationToken"] = ExtractToken(await form.Content.ReadAsStringAsync());
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password, string url = "/admin/login") =>
        PostFormAsync(client, url, ("Email", email), ("Password", password));

    public static async Task LoginWithTotpAsync(HttpClient client, TwoFactorAdmin admin)
    {
        var password = await LoginAsync(client, admin.Email);
        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
        var code = await PostFormAsync(client, "/admin/login/code", ("Code", Totp.Current(admin.Key)));
        Assert.Equal(HttpStatusCode.Redirect, code.StatusCode);
    }

    public static string Location(HttpResponseMessage response) => response.Headers.Location!.OriginalString;

    public static string[] ReadLogLines(string logsPath, string prefix)
    {
        LogManager.Flush();
        var path = Path.Combine(logsPath, $"{prefix}-{DateTime.UtcNow:yyyy-MM-dd}.log");
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

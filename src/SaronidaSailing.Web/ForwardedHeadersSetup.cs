using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace SaronidaSailing.Web;

public static class ForwardedHeadersSetup
{
    public const string ConfigKey = "ForwardedHeaders:KnownNetworks";

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var cidr in configuration.GetSection(ConfigKey).Get<string[]>() ?? [])
        {
            if (!IPNetwork.TryParse(cidr, out var network))
            {
                throw new InvalidOperationException($"{ConfigKey} contains an invalid CIDR network: '{cidr}'.");
            }

            options.KnownIPNetworks.Add(network);
        }
    }
}

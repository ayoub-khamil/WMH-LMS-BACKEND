using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace WmhLms.Api.Services;

/// <summary>
/// Which proxies may tell the API the caller's real IP and scheme.
///
/// X-Forwarded-For is only honoured from addresses listed under
/// ForwardedHeaders:KnownProxies (single IPs) or ForwardedHeaders:KnownNetworks
/// (CIDR ranges, e.g. 10.0.0.0/8). With neither set, only loopback is trusted,
/// which is ASP.NET Core's default. Trusting every source, as before, let any
/// caller hand-write the header and get a fresh login rate-limit bucket on
/// every request.
///
/// When the API goes behind a load balancer or ingress, list its address
/// range here (ForwardedHeaders__KnownNetworks__0=... in the environment).
/// </summary>
public static class ForwardedHeadersSetup
{
    public static void Apply(ForwardedHeadersOptions options, IConfiguration config)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var proxies = config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0) return; // keep the loopback-only default

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy.Trim(), out var address))
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies has an invalid IP address: '{proxy}'.");
            options.KnownProxies.Add(address);
        }
        foreach (var network in networks)
            options.KnownNetworks.Add(ParseNetwork(network));
    }

    private static AspNetIPNetwork ParseNetwork(string value)
    {
        var parts = value.Trim().Split('/');
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length)
            && length >= 0
            && length <= (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
            return new AspNetIPNetwork(prefix, length);
        throw new InvalidOperationException(
            $"ForwardedHeaders:KnownNetworks has an invalid CIDR range: '{value}' (expected e.g. 10.0.0.0/8).");
    }
}

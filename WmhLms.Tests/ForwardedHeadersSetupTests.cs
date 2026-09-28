using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using WmhLms.Api.Services;

namespace WmhLms.Tests;

/// <summary>
/// Which proxies may set the client IP the login rate limiter keys on.
/// </summary>
public class ForwardedHeadersSetupTests
{
    private static ForwardedHeadersOptions Configure(Dictionary<string, string?> settings)
    {
        var options = new ForwardedHeadersOptions();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ForwardedHeadersSetup.Apply(options, config);
        return options;
    }

    [Fact]
    public void With_nothing_configured_only_loopback_is_trusted()
    {
        var options = Configure([]);

        // Loopback stays trusted (ASP.NET Core's default)...
        Assert.True(options.KnownProxies.Contains(IPAddress.IPv6Loopback)
            || options.KnownNetworks.Any(n => n.Contains(IPAddress.Loopback)));
        // ...but a direct caller from anywhere else can no longer invent its
        // own X-Forwarded-For.
        var outsider = IPAddress.Parse("198.51.100.23");
        Assert.DoesNotContain(outsider, options.KnownProxies);
        Assert.DoesNotContain(options.KnownNetworks, n => n.Contains(outsider));
    }

    [Fact]
    public void Configured_proxies_and_networks_replace_the_default()
    {
        var options = Configure(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.7",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8"
        });

        var proxy = Assert.Single(options.KnownProxies);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), proxy);
        var network = Assert.Single(options.KnownNetworks);
        Assert.Equal(8, network.PrefixLength);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/40")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    public void Invalid_entries_stop_startup(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Configure(new() { [key] = value }));
}

using Adventures.WebApi;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>The client site policy and loader (moved from the API host, R1 of the host reuse review); the end-to-end behaviour stays covered by the app's host tests.</summary>
public sealed class ClientSiteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N"));

    private static IConfiguration Config(string? serveClient = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(serveClient is null ? [] : new Dictionary<string, string?> { [ClientSite.SettingName] = serveClient }).Build();

    [Fact]
    public void The_policy_allows_each_inline_script_by_hash_and_nothing_else_inline()
    {
        var policy = ClientSite.PolicyFor("<script src=\"a.js\"></script><script>var x=1;</script>");
        Assert.Contains("script-src 'self' 'sha256-", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    [Fact]
    public void No_web_root_or_no_index_means_no_client()
    {
        Assert.Null(ClientSite.TryLoad(null, Config()));
        Directory.CreateDirectory(_root);
        Assert.Null(ClientSite.TryLoad(_root, Config()));
    }

    [Fact]
    public void A_build_in_the_web_root_is_served_unless_switched_off()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<html></html>");
        Assert.NotNull(ClientSite.TryLoad(_root, Config()));
        Assert.Null(ClientSite.TryLoad(_root, Config("false")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

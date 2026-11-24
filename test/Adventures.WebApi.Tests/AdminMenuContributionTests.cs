using System.Security.Claims;
using Adventures.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>R3 of the host reuse review: features contribute their own menu entries and counts; the framework orders, filters and caches them.</summary>
public sealed class AdminMenuContributionTests
{
    private sealed class Contributor(params AdminMenuItemDefinition[] items) : IAdminMenuContributor
    {
        public IEnumerable<AdminMenuItemDefinition> Items => items;
    }

    private sealed class Counter(string id, Func<int> next) : IAdminCountSource
    {
        public string Id => id;
        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(next());
    }

    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(WellKnownClaims.Roles, "TenantAdmin")], "Test"));

    [Fact]
    public void Sections_come_out_in_the_well_known_order_whatever_order_features_contribute_in()
    {
        var catalog = new AdminMenuCatalog([
            new Contributor(new AdminMenuItemDefinition("System", "exceptions", "Exceptions", "/e", RequiredRole: "TenantAdmin")),
            new Contributor(new AdminMenuItemDefinition("Users", "users", "Users", "/u", RequiredRole: "TenantAdmin")),
            new Contributor(new AdminMenuItemDefinition("Content", "posts", "Posts", "/p", RequiredRole: "TenantAdmin"), new AdminMenuItemDefinition("Dashboard", "dashboard", "Dashboard", "/d", RequiredRole: "TenantAdmin"))]);

        var model = AdminMenuBuilder.Build(catalog.Visible(new HashSet<string>()), Admin);

        Assert.Equal(["Dashboard", "Content", "Users", "System"], model.Sections.Select(s => s.Id));
    }

    [Fact]
    public void An_entry_that_names_plugins_is_listed_only_while_one_of_them_is_on()
    {
        var catalog = new AdminMenuCatalog([new Contributor(
            new AdminMenuItemDefinition("Content", "decisions", "Decisions", "/x", RequiredRole: "TenantAdmin", AnyOfPlugins: ["decisions"]),
            new AdminMenuItemDefinition("Content", "posts", "Posts", "/p", RequiredRole: "TenantAdmin"))]);

        Assert.Equal(["posts"], catalog.Visible(new HashSet<string>()).Select(i => i.Id));
        Assert.Equal(["decisions", "posts"], catalog.Visible(new HashSet<string> { "decisions" }).Select(i => i.Id).OrderBy(x => x));
    }

    [Fact]
    public void A_duplicate_entry_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new AdminMenuCatalog([
            new Contributor(new AdminMenuItemDefinition("Content", "posts", "Posts", "/p")), new Contributor(new AdminMenuItemDefinition("Content", "posts", "Posts again", "/p2"))]));
    }

    [Fact]
    public async Task A_count_is_remembered_briefly_per_tenant()
    {
        var calls = 0;
        var counts = new AdminCounts([new Counter("comments", () => ++calls)], new MemoryCache(new MemoryCacheOptions()), () => "tenant-a");

        Assert.Equal(1, await counts.GetAsync("comments", CancellationToken.None));
        Assert.Equal(1, await counts.GetAsync("comments", CancellationToken.None));
        Assert.Equal(1, calls);

        var other = new AdminCounts([new Counter("comments", () => ++calls)], new MemoryCache(new MemoryCacheOptions()), () => "tenant-b");
        Assert.Equal(2, await other.GetAsync("comments", CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_count_is_zero_and_all_counts_are_listed_by_id()
    {
        var counts = new AdminCounts([new Counter("a", () => 3), new Counter("b", () => 0)], new MemoryCache(new MemoryCacheOptions()), () => "t");
        Assert.Equal(0, await counts.GetAsync("nope", CancellationToken.None));
        var all = await counts.GetAllAsync(CancellationToken.None);
        Assert.Equal(3, all["a"]);
        Assert.Equal(0, all["b"]);
    }
}

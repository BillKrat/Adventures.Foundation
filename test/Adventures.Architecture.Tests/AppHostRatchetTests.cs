using System.Xml.Linq;
using Adventures.Architecture.Tests.Support;
using Xunit;

namespace Adventures.Architecture.Tests;

/// <summary>
/// The web hosts under <c>apps</c> stay thin: a host names features and settings, and anything generic belongs in a library. A budget is today's
/// size plus a small margin and may only be lowered. Each host lists the store projects it chooses.
/// </summary>
public sealed class AppHostRatchetTests
{
    public static TheoryData<string, int, string> Budgets => new()
    {
        { "Adventures.Browser.Host", 40, "" },
    };

    private static readonly string[] StoreProjects =
    [
        "Adventures.Data.NQuad", "Adventures.Data.NQuad.Postgres", "Adventures.Data.Postgres", "Adventures.Data.Sql",
        "Adventures.Blog.Postgres", "Adventures.Identity.Postgres", "Adventures.Blog.Legacy",
    ];

    private static string Folder(string host) => Path.Combine(ProjectGraph.Root, "apps", host);

    [Theory]
    [MemberData(nameof(Budgets))]
    public void A_host_stays_under_its_line_budget(string host, int budget, string _)
    {
        var lines = Directory.GetFiles(Folder(host), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Sum(f => File.ReadLines(f).Count());

        Assert.True(lines <= budget, $"{host} is {lines} lines, over its budget of {budget}: move the generic part into a library (and lower a budget, never raise one).");
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void A_host_references_only_the_store_projects_it_lists(string host, int _, string allowedStores)
    {
        var stores = XDocument.Load(Path.Combine(Folder(host), host + ".csproj")).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .Where(StoreProjects.Contains)
            .Order(StringComparer.Ordinal);

        Assert.Equal(allowedStores.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), stores);
    }
}

using Adventures.Tools;
using Xunit;

namespace Adventures.Tools.Tests;

/// <summary>The catalog lists what a caller may use: the scope on the token, and the plug-in (when the tool belongs to one) being on.</summary>
public class ToolCatalogTests
{
    private static AppTool Tool(string name, string scope, params string[] plugins) =>
        new(name, $"{name} description", "{}", scope, "GET", $"api/{name}", plugins);

    private static readonly AppTool Posts = Tool("list_my_posts", "content.draft");
    private static readonly AppTool Items = Tool("list_my_collaboration", "content.draft", "developer-collaboration", "research-collaboration");
    private static readonly AppTool Exceptions = Tool("recent_exceptions", "mcp.diagnostics.read");

    private static ToolCatalog Catalog() => new([Posts, Items, Exceptions]);

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values);

    [Fact]
    public void ACallerSeesOnlyTheToolsItsScopesAllow()
    {
        var names = Catalog().Available(Set("content.draft"), Set("developer-collaboration")).Select(t => t.Name);

        Assert.Equal(["list_my_posts", "list_my_collaboration"], names);
    }

    [Fact]
    public void ATokenWithNoMatchingScopeSeesNothing()
    {
        Assert.Empty(Catalog().Available(Set("something.else"), Set("developer-collaboration")));
    }

    [Fact]
    public void AToolOfADisabledPluginIsHiddenEvenWithTheScope()
    {
        var names = Catalog().Available(Set("content.draft"), Set()).Select(t => t.Name);

        Assert.Equal(["list_my_posts"], names);
    }

    [Fact]
    public void AToolIsShownWhenAnyOneOfItsPluginsIsOn()
    {
        var names = Catalog().Available(Set("content.draft"), Set("research-collaboration")).Select(t => t.Name);

        Assert.Contains("list_my_collaboration", names);
    }

    [Fact]
    public void ScopesAreMatchedExactlyAndCaseSensitively()
    {
        Assert.Empty(Catalog().Available(Set("Content.Draft"), Set()));
    }

    [Fact]
    public void FindByNameReturnsNullForAnUnknownTool()
    {
        Assert.Same(Posts, Catalog().Find("list_my_posts"));
        Assert.Null(Catalog().Find("nope"));
    }

    [Fact]
    public void TwoToolsWithTheSameNameAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new ToolCatalog([Posts, Tool("list_my_posts", "content.draft")]));
    }
}

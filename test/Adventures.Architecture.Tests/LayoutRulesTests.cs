using Adventures.Architecture.Tests.Support;
using Xunit;

namespace Adventures.Architecture.Tests;

/// <summary>
/// Proves that each layout rule notices the violation it exists for, on a small made-up repository. The same rules run over the real
/// repository in <see cref="RepositoryRulesTests"/>, where a rule that could never fail would look exactly like a rule that passes.
/// </summary>
public sealed class LayoutRulesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("layout-rules-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_source_file_at_the_repository_root_is_reported()
    {
        File("Stray.cs");
        File("src/Lib/Folder/Fine.cs");

        Assert.Equal(["Stray.cs"], LayoutRules.RootSourceFiles(_root));
    }

    [Fact]
    public void A_project_outside_src_apps_and_test_is_reported()
    {
        Project("src/Lib");
        Project("apps/App");
        Project("test/Lib.Tests");
        Project("tools/Odd");
        Project("src/Lib/obj/Generated");

        Assert.Equal(["tools/Odd/Odd.csproj"], LayoutRules.StrayProjects(_root));
    }

    [Fact]
    public void A_library_that_references_an_app_or_a_test_project_is_reported()
    {
        Project("apps/App");
        Project("test/Support");
        Project("src/Lib", references: ["../../apps/App/App.csproj", "../../test/Support/Support.csproj"]);
        Project("apps/Other", references: ["../../src/Lib/Lib.csproj"]);

        Assert.Equal(["Lib -> App", "Lib -> Support"], LayoutRules.LibraryReferencesOutsideSrc(_root));
    }

    [Fact]
    public void A_reference_that_leaves_the_repository_is_reported()
    {
        Project("src/Lib", references: ["../../../elsewhere/Other/Other.csproj"]);

        Assert.Equal(["Lib -> Other"], LayoutRules.ReferencesLeavingTheRepository(_root));
    }

    [Theory]
    [InlineData("<TargetFramework>net10.0-windows</TargetFramework>")]
    [InlineData("<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><UseWPF>true</UseWPF>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><UseWindowsForms>True</UseWindowsForms>")]
    public void A_windows_library_is_reported(string properties)
    {
        Project("src/Lib", properties);

        Assert.Equal(["src/Lib/Lib.csproj"], LayoutRules.MisplacedWindowsProjects(_root));
    }

    [Fact]
    public void A_windows_app_and_the_tests_of_it_are_allowed_but_no_other_windows_test_project()
    {
        const string windows = "<TargetFramework>net10.0-windows</TargetFramework>";
        Project("apps/Desktop", windows);
        Project("test/Desktop.Tests", windows, ["../../apps/Desktop/Desktop.csproj"]);
        Project("test/Lonely.Tests", windows);

        Assert.Equal(["test/Lonely.Tests/Lonely.Tests.csproj"], LayoutRules.MisplacedWindowsProjects(_root));
    }

    [Theory]
    [InlineData("Adventures.Blog")]
    [InlineData("Adventures.Blog.WebApi")]
    [InlineData("Adventures.Collaboration")]
    [InlineData("Adventures.Hosting")]
    public void An_app_that_reaches_a_feature_library_even_indirectly_is_reported(string feature)
    {
        Project($"src/{feature}");
        Project("src/Middle", references: [$"../{feature}/{feature}.csproj"]);
        Project("apps/App", references: ["../../src/Middle/Middle.csproj"]);

        Assert.Equal([$"App -> {feature}"], LayoutRules.AppsReachingFeatureLibraries(_root));
    }

    [Fact]
    public void An_app_keeps_only_its_start_up_files_at_the_project_root()
    {
        Project("apps/App");
        File("apps/App/Program.cs");
        File("apps/App/App.xaml.cs");
        File("apps/App/MainWindow.xaml.cs");
        File("apps/App/Helper.cs");
        File("apps/App/Views/Fine.cs");

        Assert.Equal(["apps/App/Helper.cs"], LayoutRules.AppRootSourceFiles(_root));
    }

    [Fact]
    public void A_committed_app_setting_with_a_real_address_is_reported_by_file_and_key_without_the_value()
    {
        File("apps/App/appsettings.json", """
            {
              // comments and trailing commas are allowed, as in any appsettings file
              "Api": { "BaseUrl": "https://api.some-real-site.net/" },
              "Sites": [ { "Origin": "http://localhost:5000" }, { "Api": "https://another-real-site.org" }, ],
            }
            """);

        var found = LayoutRules.RealAddressesInAppSettings(_root).ToList();

        Assert.Equal(["apps/App/appsettings.json: Api:BaseUrl", "apps/App/appsettings.json: Sites:1:Api"], found);
        Assert.DoesNotContain(found, line => line.Contains("real-site", StringComparison.Ordinal)); // CI logs are public
    }

    [Theory]
    [InlineData("https://localhost:7052/")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://[::1]:5000/")]
    [InlineData("https://one.localhost/")]
    [InlineData("https://api.example.com/")]
    [InlineData("https://api.example/")]
    [InlineData("https://site.test/")]
    [InlineData("https://site.invalid/")]
    public void Loopback_and_reserved_example_addresses_are_allowed(string address)
    {
        File("apps/App/wwwroot/appsettings.json", $$"""{ "Api": "{{address}}", "$schema": "https://json.schemastore.org/appsettings.json" }""");

        Assert.Empty(LayoutRules.RealAddressesInAppSettings(_root));
    }

    [Theory]
    [InlineData("https://localhost.some-real-site.net/")]
    [InlineData("https://notexample.com/")]
    [InlineData("Server=x;Endpoint=https://some-real-site.net")]
    public void A_look_alike_or_embedded_address_is_reported(string value)
    {
        File("apps/App/sites.json", $$"""{ "Api": "{{value}}" }""");

        Assert.Single(LayoutRules.RealAddressesInAppSettings(_root));
    }

    [Fact]
    public void A_solution_must_list_every_project_once_and_only_projects_that_exist_inside_the_repository()
    {
        Project("src/Lib");
        Project("apps/Unlisted");
        File("All.slnx", """
            <Solution>
              <Project Path="src/Lib/Lib.csproj" />
              <Project Path="src/Lib/Lib.csproj" />
              <Project Path="src/Gone/Gone.csproj" />
              <Project Path="../outside/Outside.csproj" />
            </Solution>
            """);

        Assert.Equal(
            [
                "listed twice: src/Lib/Lib.csproj",
                "missing: src/Gone/Gone.csproj",
                "not listed: apps/Unlisted/Unlisted.csproj",
                "outside the repository: ../outside/Outside.csproj",
            ],
            LayoutRules.SolutionProblems(_root, "All.slnx"));
    }

    [Fact]
    public void A_solution_file_that_is_not_one_document_is_reported()
    {
        File("All.slnx", "<Solution /><Solution />");

        Assert.StartsWith("not one XML document", Assert.Single(LayoutRules.SolutionProblems(_root, "All.slnx")));
    }

    [Fact]
    public void The_linux_filter_must_hold_exactly_the_projects_that_do_not_need_windows()
    {
        Project("src/Lib");
        Project("src/Forgotten");
        Project("apps/Desktop", "<TargetFramework>net10.0-windows</TargetFramework>");
        File("All.slnx", """
            <Solution>
              <Project Path="src/Lib/Lib.csproj" />
              <Project Path="src/Forgotten/Forgotten.csproj" />
              <Project Path="apps/Desktop/Desktop.csproj" />
            </Solution>
            """);
        File("Linux.slnf", """{ "solution": { "path": "All.slnx", "projects": [ "src\\Lib\\Lib.csproj", "apps\\Desktop\\Desktop.csproj" ] } }""");

        Assert.Equal(
            ["needs Windows: apps/Desktop/Desktop.csproj", "not in the filter: src/Forgotten/Forgotten.csproj"],
            LayoutRules.FilterProblems(_root, "All.slnx", "Linux.slnf"));
    }

    [Fact]
    public void A_missing_filter_is_reported()
    {
        File("All.slnx", "<Solution />");

        Assert.Equal(["missing: Linux.slnf"], LayoutRules.FilterProblems(_root, "All.slnx", "Linux.slnf"));
    }

    private void File(string relativePath, string text = "")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
    }

    private void Project(string folder, string properties = "<TargetFramework>net10.0</TargetFramework>", string[]? references = null)
    {
        var items = string.Concat((references ?? []).Select(r => $"<ProjectReference Include=\"{r.Replace('/', '\\')}\" />"));
        File($"{folder}/{Path.GetFileName(folder)}.csproj", $"<Project><PropertyGroup>{properties}</PropertyGroup><ItemGroup>{items}</ItemGroup></Project>");
    }
}

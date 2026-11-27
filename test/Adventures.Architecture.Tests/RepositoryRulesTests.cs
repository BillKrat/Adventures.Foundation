using Adventures.Architecture.Tests.Support;
using Xunit;

namespace Adventures.Architecture.Tests;

/// <summary>
/// The layout of this repository: libraries under <c>src</c>, applications under <c>apps</c>, tests under <c>test</c>. Libraries never
/// depend on applications, applications stay generic (no blog, collaboration or blog hosting library), nothing depends on another
/// repository, and CI builds every project on at least one operating system. Decision: ai-research-blog
/// <c>docs/Claude-decision-2026-11-foundation-apps.md</c>.
/// </summary>
public sealed class RepositoryRulesTests
{
    private const string Solution = "Adventures.Foundation.slnx";
    private const string LinuxFilter = "Adventures.Foundation.Linux.slnf";

    private static string Root => ProjectGraph.Root;

    [Fact]
    public void No_source_files_at_the_repository_root() => Assert.Empty(LayoutRules.RootSourceFiles(Root));

    [Fact]
    public void Every_project_lives_under_src_apps_or_test() => Assert.Empty(LayoutRules.StrayProjects(Root));

    [Fact]
    public void Libraries_reference_only_libraries() => Assert.Empty(LayoutRules.LibraryReferencesOutsideSrc(Root));

    [Fact]
    public void Nothing_depends_on_another_repository() => Assert.Empty(LayoutRules.ReferencesLeavingTheRepository(Root));

    [Fact]
    public void Only_apps_and_their_tests_need_Windows() => Assert.Empty(LayoutRules.MisplacedWindowsProjects(Root));

    [Fact]
    public void Apps_reach_no_feature_library() => Assert.Empty(LayoutRules.AppsReachingFeatureLibraries(Root));

    [Fact]
    public void Apps_keep_only_start_up_files_at_their_project_root() => Assert.Empty(LayoutRules.AppRootSourceFiles(Root));

    [Fact]
    public void Committed_app_settings_name_no_real_site() => Assert.Empty(LayoutRules.RealAddressesInAppSettings(Root));

    [Fact]
    public void The_solution_lists_every_project_once() => Assert.Empty(LayoutRules.SolutionProblems(Root, Solution));

    [Fact]
    public void The_linux_filter_is_the_solution_without_the_Windows_projects() => Assert.Empty(LayoutRules.FilterProblems(Root, Solution, LinuxFilter));

    /// <summary>The Windows job covers the whole solution and the Linux job the filter, so together they build every project.</summary>
    [Fact]
    public void CI_builds_the_whole_solution_on_Windows_and_the_filter_on_Linux()
    {
        var build = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "build.yml"));
        Assert.Contains("runs-on: windows-latest", build, StringComparison.Ordinal);
        Assert.Contains($"dotnet test {Solution}", build, StringComparison.Ordinal);
        Assert.Contains($"dotnet test {LinuxFilter}", build, StringComparison.Ordinal);

        var publish = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "publish.yml"));
        Assert.DoesNotContain(Solution, publish, StringComparison.Ordinal); // publish runs on Linux
        Assert.Contains($"dotnet test {LinuxFilter}", publish, StringComparison.Ordinal);
    }
}

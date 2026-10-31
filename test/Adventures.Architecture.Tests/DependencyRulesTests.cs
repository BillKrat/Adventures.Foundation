using Adventures.Architecture.Tests.Support;
using Xunit;

namespace Adventures.Architecture.Tests;

/// <summary>
/// Architecture guard for the 2026-10 decoupling refactor (rules in ai-research-blog <c>docs/Claude-architecture-reference.md</c>).
/// Every rule is a ratchet: today's violations are listed with the stage that removes them. A NEW violation fails, and so
/// does a listed violation that no longer exists - the stage that fixes it must delete its line here (red, then green).
/// </summary>
public class DependencyRulesTests
{
    private static ProjectGraph Graph => ProjectGraph.Current;

    /// <summary>The project-reference graph as it stands. Strict: adding or removing an edge must update this list.</summary>
    private static readonly string[] RecordedEdges =
    [
        "Adventures.Blog -> Adventures.Collaboration",
        "Adventures.Blog -> Adventures.Entities",
        "Adventures.Blog.Legacy -> Adventures.Blog",
        "Adventures.Blog.Legacy -> Adventures.Data.Sql",
        "Adventures.Blog.Postgres -> Adventures.Blog",
        "Adventures.Blog.WebApi -> Adventures.Blog",
        "Adventures.Blog.WebApi -> Adventures.Security",
        "Adventures.Blog.WebApi -> Adventures.WebApi",
        "Adventures.Collaboration -> Adventures.Entities",
        "Adventures.Collaboration -> Adventures.MEF",
        "Adventures.Common -> Adventures.Ioc",
        "Adventures.Data.NQuad -> Adventures.Entities",
        "Adventures.Data.Postgres -> Adventures.Data",
        "Adventures.Data.Sql -> Adventures.Common",
        "Adventures.Data.Sql -> Adventures.Data",
        "Adventures.Entities -> Adventures.Common",
        "Adventures.Identity -> Adventures.Data",
        "Adventures.Identity -> Adventures.Security",
        "Adventures.Identity.Postgres -> Adventures.Data",
        "Adventures.Identity.Postgres -> Adventures.Identity",
        "Adventures.MEF -> Adventures.Entities",
        "Adventures.Security.AspNetCore -> Adventures.Security",
        "Adventures.WebApi -> Adventures.Entities",
        "Adventures.WebApi -> Adventures.Security",
    ];

    [Fact]
    public void Project_reference_graph_matches_the_recorded_graph()
    {
        Assert.Equal(RecordedEdges, Graph.Edges);
    }

    [Fact]
    public void Project_references_have_no_cycles()
    {
        foreach (var start in Graph.Projects.Keys)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(Graph.Projects[start].ProjectReferences);
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                Assert.NotEqual(start, next);
                if (seen.Add(next) && Graph.Projects.TryGetValue(next, out var project))
                {
                    foreach (var reference in project.ProjectReferences)
                    {
                        stack.Push(reference);
                    }
                }
            }
        }
    }

    // Rule 1: Common references nothing of ours except its own Ioc; Entities references Common only.
    [Fact]
    public void Common_and_Entities_stay_at_the_bottom()
    {
        AssertRatchet(
            "Common/Entities may only reference Common/Ioc",
            Edges("Adventures.Common", "Adventures.Entities").Where(e => !AllowedBottom.Contains(e.To)),
            []);
    }

    private static readonly HashSet<string> AllowedBottom = ["Adventures.Common", "Adventures.Ioc"];

    // Rule 2: Security and Identity reference no storage project.
    [Fact]
    public void Security_and_Identity_reference_no_store()
    {
        AssertRatchet(
            "Security/Identity must not reference a store project",
            Edges("Adventures.Security", "Adventures.Identity").Where(e => IsStore(e.To)),
            []); // S5: the Postgres credential store moved to Adventures.Identity.Postgres; Adventures.Data is now contracts only
    }

    // Rule 3: each store project references the Data abstractions, not Entities alone.
    [Fact]
    public void Store_projects_build_on_Adventures_Data()
    {
        var missing = new[] { "Adventures.Data.Postgres", "Adventures.Identity.Postgres" }
            .Where(store => !Graph.Projects[store].ProjectReferences.Contains("Adventures.Data"))
            .Select(store => $"{store} -> Adventures.Data (missing)");
        AssertRatchet(
            "Postgres store projects must reference the Adventures.Data contracts",
            missing.Select(m => (From: m, To: string.Empty)),
            []);
    }

    // Rule 5: WebApi references no store project.
    [Fact]
    public void WebApi_references_no_store()
    {
        AssertRatchet(
            "WebApi must not reference a store project",
            Edges("Adventures.WebApi").Where(e => IsStore(e.To)),
            []); // S5: the store probe is an IStoreProbe the store registers
    }

    // Rule 6: Security core carries no ASP.NET Core package.
    [Fact]
    public void Security_core_has_no_AspNetCore_package()
    {
        var offenders = Graph.Projects["Adventures.Security"].PackageReferences
            .Where(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(p => (From: "Adventures.Security", To: p));
        AssertRatchet(
            "Security must not reference ASP.NET Core packages",
            offenders,
            []); // S6: the JwtBearer wiring moved to Adventures.Security.AspNetCore
    }

    // Rule R17: no source files at a project root (reached 0 in S1; any new file at a root fails).
    [Fact]
    public void No_source_files_at_project_roots()
    {
        var offenders = Graph.Projects.Where(p => p.Value.SourceFilesAtProjectRoot > 0).Select(p => $"{p.Key}: {p.Value.SourceFilesAtProjectRoot}");
        Assert.Empty(offenders);
    }

    // Layout (S1): every project lives under src or test; none sits beside the solution file.
    [Fact]
    public void Every_project_lives_under_src_or_test()
    {
        var stray = Directory.GetFiles(ProjectGraph.Root, "*.csproj", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(ProjectGraph.Root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("src/", StringComparison.Ordinal) && !f.StartsWith("test/", StringComparison.Ordinal))
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));
        Assert.Empty(stray);
    }

    // S3: the blog feature builds on the Entities model only. It names no store, no security project and no host (the NQuad browser must not pull it in, and it must not pull a store).
    [Fact]
    public void Blog_projects_reference_only_their_layer()
    {
        Assert.Equal(["Adventures.Collaboration", "Adventures.Entities"], Graph.Projects["Adventures.Blog"].ProjectReferences);
        Assert.Equal(["Adventures.Blog"], Graph.Projects["Adventures.Blog.Postgres"].ProjectReferences);
        Assert.Equal(["Adventures.Blog", "Adventures.Data.Sql"], Graph.Projects["Adventures.Blog.Legacy"].ProjectReferences);
        Assert.Equal(["Adventures.Blog", "Adventures.Security", "Adventures.WebApi"], Graph.Projects["Adventures.Blog.WebApi"].ProjectReferences);
    }

    // S3: nothing below the Blog (Entities and the other libraries) references it, so Entities holds no blog types.
    [Fact]
    public void Nothing_but_the_Blog_stack_references_Blog()
    {
        var referrers = Graph.Projects.Values
            .Where(p => p.ProjectReferences.Contains("Adventures.Blog"))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(["Adventures.Blog.Legacy", "Adventures.Blog.Postgres", "Adventures.Blog.WebApi"], referrers);
    }

    // S4: Collaboration builds on Entities and the plug-in seam only (no Blog, no store, no host); the plug-in seam builds on Entities only; only the Blog stack builds on Collaboration.
    [Fact]
    public void Collaboration_and_the_plugin_seam_reference_only_their_layer()
    {
        Assert.Equal(["Adventures.Entities", "Adventures.MEF"], Graph.Projects["Adventures.Collaboration"].ProjectReferences);
        Assert.Equal(["Adventures.Entities"], Graph.Projects["Adventures.MEF"].ProjectReferences);
        var referrers = Graph.Projects.Values.Where(p => p.ProjectReferences.Contains("Adventures.Collaboration")).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(["Adventures.Blog"], referrers);
    }

    // S5: Adventures.Data is contracts only - no database driver, no ASP.NET package; the implementations live in the store projects.
    [Fact]
    public void Data_contracts_project_carries_no_driver()
    {
        Assert.Empty(Graph.Projects["Adventures.Data"].PackageReferences);
        Assert.Empty(Graph.Projects["Adventures.Data"].ProjectReferences);
    }

    // S6: the security core is framework-free and a plain console-style project can use it alone - the Security tests reference nothing but the core.
    [Fact]
    public void Security_core_has_no_web_framework_and_is_usable_alone()
    {
        var core = System.Xml.Linq.XDocument.Load(Path.Combine(ProjectGraph.Root, "src", "Adventures.Security", "Adventures.Security.csproj"));
        Assert.Empty(core.Descendants("FrameworkReference"));
        Assert.Empty(core.Descendants("ProjectReference"));

        var alone = System.Xml.Linq.XDocument.Load(Path.Combine(ProjectGraph.Root, "test", "Adventures.Security.Tests", "Adventures.Security.Tests.csproj"));
        Assert.Empty(alone.Descendants("FrameworkReference"));
        Assert.Equal(["Adventures.Security"], alone.Descendants("ProjectReference").Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/'))).ToList());
    }

    // S7: the SQL Server executor builds on the contracts only (Data, Common); the legacy blog adapter on the Blog and that executor, never on another store.
    [Fact]
    public void Sql_store_projects_reference_only_contracts_and_the_blog()
    {
        Assert.Equal(["Adventures.Common", "Adventures.Data"], Graph.Projects["Adventures.Data.Sql"].ProjectReferences);
        Assert.DoesNotContain(Graph.Projects["Adventures.Blog.Legacy"].ProjectReferences, r => r is "Adventures.Data.NQuad" or "Adventures.Data.Postgres");
    }

    // Rule R16: one public type name per library (S1 renamed the Postgres-side IEntityRepository to IEntityRecordRepository).
    [Fact]
    public void Public_type_names_are_unique_across_libraries()
    {
        var pattern = new System.Text.RegularExpressions.Regex(@"^public\s+(?:(?:sealed|static|abstract|partial|readonly)\s+)*(?:class|interface|record(?:\s+struct)?|struct|enum)\s+(\w+)", System.Text.RegularExpressions.RegexOptions.Multiline);
        var duplicates = Graph.Projects.Values
            .SelectMany(p => Directory.GetFiles(p.Directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => (Name: m.Groups[1].Value, Project: p.Name))))
            .GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} in {string.Join(", ", g.Select(x => x.Project))}");
        Assert.Empty(duplicates);
    }

    // Rule R9 (S2): a Bll depends on the Dal, never on a repository. Only the default Dal (and the repository contract itself) names one.
    [Fact]
    public void Entities_code_other_than_the_Dal_names_no_repository()
    {
        var allowed = new[] { "Core/EntityDal.cs", "Core/IEntityRepository.cs" };
        var offenders = Directory.GetFiles(Graph.Projects["Adventures.Entities"].Directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(f => Path.GetRelativePath(Graph.Projects["Adventures.Entities"].Directory, f).Replace('/', '/').Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !allowed.Contains(f))
            .Where(f => File.ReadAllText(Path.Combine(Graph.Projects["Adventures.Entities"].Directory, f)).Contains("IEntityRepository<", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);
        Assert.Empty(offenders);
    }

    private static bool IsStore(string project) =>
        project is "Adventures.Data.NQuad" or "Adventures.Data.Postgres" or "Adventures.Data.Sql" or "Adventures.Blog.Postgres" or "Adventures.Identity.Postgres" or "Adventures.Blog.Legacy";

    private static IEnumerable<(string From, string To)> Edges(params string[] from) =>
        from.Where(Graph.Projects.ContainsKey)
            .SelectMany(f => Graph.Projects[f].ProjectReferences.Select(r => (From: f, To: r)));

    private static void AssertRatchet(string rule, IEnumerable<(string From, string To)> violations, string[] known)
    {
        var actual = violations
            .Select(v => v.To.Length == 0 ? v.From : $"{v.From} -> {v.To}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            actual.SequenceEqual(known.OrderBy(k => k, StringComparer.Ordinal)),
            $"{rule}.{Environment.NewLine}Actual violations: [{string.Join("; ", actual)}]{Environment.NewLine}Recorded: [{string.Join("; ", known)}]");
    }
}

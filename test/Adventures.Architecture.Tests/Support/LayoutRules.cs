using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Adventures.Architecture.Tests.Support;

/// <summary>The repository layout rules. Each takes a repository root and returns the violations, so a rule can be proved on a made-up repository.</summary>
public static partial class LayoutRules
{
    private static readonly string[] StartUpFiles = ["Program.cs", "App.xaml.cs", "MainWindow.xaml.cs"];

    /// <summary>Libraries an application must stay clear of: a client or browser app is generic and knows no product feature.</summary>
    private static readonly string[] FeatureLibraries = ["Adventures.Blog", "Adventures.Collaboration", "Adventures.Hosting"];

    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".git", ".vs"];

    public static IEnumerable<string> RootSourceFiles(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly).Select(f => Relative(root, f)).Order(StringComparer.Ordinal);

    public static IEnumerable<string> StrayProjects(string root) =>
        Projects(root).Select(p => p.Path).Where(p => !Under(p, "src") && !Under(p, "apps") && !Under(p, "test"));

    public static IEnumerable<string> LibraryReferencesOutsideSrc(string root) =>
        from project in Projects(root)
        where Under(project.Path, "src")
        from reference in project.References
        where Under(reference, "apps") || Under(reference, "test")
        select $"{project.Name} -> {NameOf(reference)}";

    public static IEnumerable<string> ReferencesLeavingTheRepository(string root) =>
        from project in Projects(root)
        from reference in project.References
        where LeavesRepository(reference)
        select $"{project.Name} -> {NameOf(reference)}";

    /// <summary>A project that needs Windows may only be an application, or a test project of such an application.</summary>
    public static IEnumerable<string> MisplacedWindowsProjects(string root)
    {
        var projects = Projects(root);
        var windowsApps = projects.Where(p => p.NeedsWindows && Under(p.Path, "apps")).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return projects
            .Where(p => p.NeedsWindows && !Under(p.Path, "apps"))
            .Where(p => !(Under(p.Path, "test") && p.References.Any(windowsApps.Contains)))
            .Select(p => p.Path);
    }

    public static IEnumerable<string> AppsReachingFeatureLibraries(string root)
    {
        var projects = Projects(root);
        var byPath = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var app in projects.Where(p => Under(p.Path, "apps")))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(app.References);
            var found = new SortedSet<string>(StringComparer.Ordinal);
            while (queue.TryDequeue(out var reference))
            {
                if (!seen.Add(reference))
                {
                    continue;
                }

                var name = NameOf(reference);
                if (FeatureLibraries.Any(f => name == f || name.StartsWith(f + ".", StringComparison.Ordinal)))
                {
                    found.Add(name); // report the first feature library on a path, not everything behind it
                    continue;
                }

                if (byPath.TryGetValue(reference, out var next))
                {
                    foreach (var further in next.References)
                    {
                        queue.Enqueue(further);
                    }
                }
            }

            foreach (var name in found)
            {
                yield return $"{app.Name} -> {name}";
            }
        }
    }

    public static IEnumerable<string> AppRootSourceFiles(string root) =>
        Projects(root)
            .Where(p => Under(p.Path, "apps"))
            .SelectMany(p => Directory.GetFiles(Path.GetDirectoryName(Path.Combine(root, p.Path))!, "*.cs", SearchOption.TopDirectoryOnly))
            .Where(f => !StartUpFiles.Contains(Path.GetFileName(f), StringComparer.Ordinal))
            .Select(f => Relative(root, f))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Every web address in a JSON file under <c>apps</c> must be this machine or a name reserved for examples: which sites a person connects to is
    /// their own configuration, never committed. A violation names the file and key only, because build logs of a public repository are public.
    /// </summary>
    public static IEnumerable<string> RealAddressesInAppSettings(string root)
    {
        var apps = Path.Combine(root, "apps");
        if (!Directory.Exists(apps))
        {
            return [];
        }

        var found = new List<string>();
        foreach (var file in Files(apps, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Scan(document.RootElement, string.Empty, Relative(root, file), found);
        }

        return found.Order(StringComparer.Ordinal);
    }

    public static IEnumerable<string> SolutionProblems(string root, string solution)
    {
        List<string> listed;
        try
        {
            listed = SolutionPaths(root, solution);
        }
        catch (System.Xml.XmlException error)
        {
            return [$"not one XML document: {error.Message}"];
        }

        var problems = new List<string>();
        problems.AddRange(listed.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"listed twice: {g.Key}"));
        var inside = listed.Where(p => !LeavesRepository(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        problems.AddRange(inside.Where(p => !File.Exists(Path.Combine(root, p))).Select(p => $"missing: {p}"));
        problems.AddRange(Projects(root).Select(p => p.Path).Except(inside, StringComparer.OrdinalIgnoreCase).Select(p => $"not listed: {p}"));
        problems.AddRange(listed.Where(LeavesRepository).Select(p => $"outside the repository: {p}"));
        return problems;
    }

    /// <summary>The solution filter the Linux jobs use holds exactly the solution's projects that do not need Windows, so none drops out of CI unnoticed.</summary>
    public static IEnumerable<string> FilterProblems(string root, string solution, string filter)
    {
        var path = Path.Combine(root, filter);
        if (!File.Exists(path))
        {
            return [$"missing: {filter}"];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var body = document.RootElement.GetProperty("solution");
        var filtered = body.GetProperty("projects").EnumerateArray().Select(p => p.GetString()!.Replace('\\', '/')).ToList();
        var windows = Projects(root).Where(p => p.NeedsWindows).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = SolutionPaths(root, solution).Where(p => !windows.Contains(p)).ToList();

        var problems = new List<string>();
        if (body.GetProperty("path").GetString() != solution)
        {
            problems.Add($"the filter is not over {solution}");
        }

        problems.AddRange(filtered.Where(windows.Contains).Select(p => $"needs Windows: {p}"));
        problems.AddRange(expected.Except(filtered, StringComparer.OrdinalIgnoreCase).Select(p => $"not in the filter: {p}"));
        problems.AddRange(filtered.Where(p => !windows.Contains(p)).Except(expected, StringComparer.OrdinalIgnoreCase).Select(p => $"not in the solution: {p}"));
        return problems;
    }

    private static List<string> SolutionPaths(string root, string solution) =>
        XDocument.Load(Path.Combine(root, solution)).Descendants("Project").Select(p => ((string)p.Attribute("Path")!).Replace('\\', '/')).ToList();

    private static void Scan(JsonElement element, string key, string file, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject().Where(p => p.Name != "$schema")) // an editor hint, not a site
                {
                    Scan(property.Value, key.Length == 0 ? property.Name : $"{key}:{property.Name}", file, found);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, $"{key}:{index++}", file, found);
                }

                break;
            case JsonValueKind.String:
                if (WebAddress().Matches(element.GetString()!).Any(m => !IsLocalOrExample(m.Value)))
                {
                    found.Add($"{file}: {key}");
                }

                break;
        }
    }

    private static bool IsLocalOrExample(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return uri.IsLoopback
            || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.EndsWith(".example", StringComparison.Ordinal)
            || host.EndsWith(".test", StringComparison.Ordinal)
            || host.EndsWith(".invalid", StringComparison.Ordinal)
            || new[] { "example.com", "example.net", "example.org" }.Any(e => host == e || host.EndsWith("." + e, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"https?://[^\s""';,<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();

    private sealed record Project(string Path, string Name, bool NeedsWindows, IReadOnlyList<string> References);

    /// <summary>Every C# project under the root, with paths and references relative to the root and written with forward slashes.</summary>
    private static List<Project> Projects(string root) =>
        Files(root, "*.csproj")
            .Select(file =>
            {
                var document = XDocument.Load(file);
                var windows = document.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks").Any(e => e.Value.Contains("-windows", StringComparison.OrdinalIgnoreCase))
                    || document.Descendants().Any(e => e.Name.LocalName is "UseWPF" or "UseWindowsForms" && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                var references = document.Descendants("ProjectReference")
                    .Select(e => ((string)e.Attribute("Include")!).Replace('\\', '/'))
                    .Select(include => Relative(root, System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, include))))
                    .ToList();
                return new Project(Relative(root, file), System.IO.Path.GetFileNameWithoutExtension(file), windows, references);
            })
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.GetFiles(folder, pattern)
            .Concat(Directory.GetDirectories(folder)
                .Where(d => !SkippedFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                .SelectMany(d => Files(d, pattern)));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string NameOf(string projectPath) => Path.GetFileNameWithoutExtension(projectPath);

    private static bool Under(string relativePath, string folder) => relativePath.StartsWith(folder + "/", StringComparison.Ordinal);

    private static bool LeavesRepository(string relativePath) => relativePath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativePath);
}

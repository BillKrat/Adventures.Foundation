using System.Xml.Linq;

namespace Adventures.Architecture.Tests.Support;

/// <summary>
/// Reads the library projects of this repository (the ones listed under <c>src</c> in the solution, plus Common and Ioc)
/// and exposes their project and package references, so the dependency rules in
/// <c>docs/Claude-architecture-reference.md</c> (ai-research-blog) can be tested without loading any assembly.
/// </summary>
public sealed class ProjectGraph
{
    private static readonly Lazy<ProjectGraph> Instance = new(Load);

    public static ProjectGraph Current => Instance.Value;

    private ProjectGraph(IReadOnlyDictionary<string, ProjectInfo> projects) => Projects = projects;

    public IReadOnlyDictionary<string, ProjectInfo> Projects { get; }

    /// <summary>Every "A -> B" project reference between library projects, sorted.</summary>
    public IReadOnlyList<string> Edges =>
        Projects.Values
            .SelectMany(p => p.ProjectReferences.Select(r => $"{p.Name} -> {r}"))
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();

    private static ProjectGraph Load()
    {
        var root = FindRepositoryRoot();
        var projectFiles = new[] { "Adventures.Common", "Adventures.Ioc" }
            .Select(name => Path.Combine(root, name, name + ".csproj"))
            .Concat(Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories));

        var projects = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
        foreach (var file in projectFiles)
        {
            var document = XDocument.Load(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var directory = Path.GetDirectoryName(file)!;
            var references = document.Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .Where(r => r.Length > 0)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            var packages = document.Descendants("PackageReference")
                .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                .Where(p => p.Length > 0)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            var rootFiles = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly).Length;
            projects[name] = new ProjectInfo(name, directory, references, packages, rootFiles);
        }

        return new ProjectGraph(projects);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Adventures.Foundation.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Adventures.Foundation.slnx not found above " + AppContext.BaseDirectory);
    }
}

public sealed record ProjectInfo(
    string Name,
    string Directory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    int SourceFilesAtProjectRoot);

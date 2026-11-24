using Adventures.Blog;
using Adventures.Blog.Legacy;
using Adventures.Collaboration;
using Adventures.Entities;
using Adventures.MEF;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>R2 of the host reuse review: each feature library lists its own seeds, and the list must match the files beside the schemas, so a seed added to a library cannot be forgotten by a host.</summary>
public sealed class SeedManifestTests
{
    // seed-012 (MCP wave 2 tables) is deliberately not applied by any host today; it stays listed here so adding it to a manifest is a conscious change.
    private static readonly string[] NotApplied = ["seed-012-legacy-wave2.nq"];

    private static string SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Adventures.Entities")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src");
    }

    public static TheoryData<string, string> Manifests => new()
    {
        { "Adventures.Entities.Diagnostics", nameof(DiagnosticsSeeds) },
        { "Adventures.Entities.Identity", nameof(IdentitySeeds) },
        { "Adventures.Blog", nameof(BlogSeeds) },
        { "Adventures.Collaboration", nameof(CollaborationSeeds) },
        { "Adventures.MEF", nameof(PluginSeeds) },
        { "Adventures.Blog.Legacy", nameof(LegacySeeds) },
    };

    private static IReadOnlyList<SeedEntry> Load(string cls) => cls switch
    {
        nameof(DiagnosticsSeeds) => DiagnosticsSeeds.All,
        nameof(IdentitySeeds) => IdentitySeeds.All,
        nameof(BlogSeeds) => BlogSeeds.All,
        nameof(CollaborationSeeds) => CollaborationSeeds.All,
        nameof(PluginSeeds) => PluginSeeds.All,
        nameof(LegacySeeds) => LegacySeeds.All,
        _ => throw new ArgumentOutOfRangeException(nameof(cls)),
    };

    [Theory]
    [MemberData(nameof(Manifests))]
    public void Every_seed_file_in_a_library_is_in_its_manifest_and_every_entry_has_a_file(string project, string manifest)
    {
        var onDisk = Directory.GetFiles(Path.Combine(SrcRoot(), project, "Sql", "seed"), "seed-*.nq").Select(Path.GetFileName).ToHashSet();
        var listed = Load(manifest).Select(s => s.File).ToHashSet();
        Assert.Empty(onDisk.Except(listed).Except(NotApplied));
        Assert.Empty(listed.Except(onDisk));
    }

    [Fact]
    public void Seed_names_and_files_are_unique_across_all_manifests()
    {
        var all = Manifests.SelectMany(m => Load((string)m[1]!)).ToList();
        Assert.Equal(all.Count, all.Select(s => s.Name).Distinct().Count());
        Assert.Equal(all.Count, all.Select(s => s.File).Distinct().Count());
    }
}

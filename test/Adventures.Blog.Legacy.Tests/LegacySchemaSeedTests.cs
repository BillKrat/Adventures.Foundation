// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Blog.Legacy.Research;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>The legacy table list: the single source for the paged row tool and the generated mirror schemas, kept honest by tests.</summary>
public sealed class LegacySchemaSeedTests
{
    private static string SeedPath(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Adventures.Blog.Legacy")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Foundation root not found."), "src", "Adventures.Blog.Legacy", "Sql", "seed", file);
    }
    private const string ColumnsSeed = "seed-009-legacy-schemas.nq";
    private const string IdFieldsSeed = "seed-010-legacy-ids.nq";

    private static void AssertSeedMatches(string file, string generated)
    {
        if (Environment.GetEnvironmentVariable("REGENERATE_LEGACY_SEED") == "1")
        {
            File.WriteAllText(SeedPath(file), generated, new System.Text.UTF8Encoding(false));
        }

        Assert.Equal(generated.Replace("\r\n", "\n"), File.ReadAllText(SeedPath(file)).Replace("\r\n", "\n"));
    }

    [Fact]
    public void TheCheckedInSeeds_AreExactlyWhatTheGeneratorProduces() =>
        AssertSeedMatches(ColumnsSeed, LegacySchemaSeed.Generate(BlogAiTables.Wave1));

    [Fact]
    public void TheIdFieldsSeed_IsExactlyWhatTheGeneratorProduces() =>
        AssertSeedMatches(IdFieldsSeed, LegacySchemaSeed.GenerateIdFields(BlogAiTables.Wave1));

    [Fact]
    public void EverySchema_LoadsThroughTheRealSchemaLoader_WithAnIdAndAllItsColumns()
    {
        // both seeds, in the order the app applies them
        var quads = new NQuadFileParser().Parse(File.ReadAllText(SeedPath(ColumnsSeed)) + File.ReadAllText(SeedPath(IdFieldsSeed)));

        foreach (var table in BlogAiTables.Wave1)
        {
            var schema = SchemaDal.Load(quads, LegacySchemaSeed.SchemaBase + table.Name);
            var fields = schema.Fields.Values.OrderBy(f => f.Order).ToList();
            Assert.Equal(["Id", .. table.Columns.Select(c => c.Name)], fields.Select(f => f.Name));
            Assert.Equal(table.Columns.Select(c => c.Required), fields.Skip(1).Select(f => f.IsRequired));
            Assert.Equal("Guid", fields[0].Type);
        }
    }

    [Fact]
    public void Tables_HaveUniqueNames_AnIdentityColumnFirst_AndNoSecretOrBinaryColumn()
    {
        Assert.Equal(BlogAiTables.All.Count, BlogAiTables.All.Select(t => t.Name).Distinct().Count());
        Assert.All(BlogAiTables.All, t =>
        {
            Assert.StartsWith("be_", t.Name);
            Assert.Contains(t.Columns[0].Name, new[] { "BlogRowId", "UserID", "RoleID", "UserRoleID", "RightRowId", "RightRoleRowId", "ProfileID", "SettingRowId", "CustomType", "PostRowID", "PageRowID", "CategoryRowID", "PostCategoryID", "PostTagID" });
            Assert.Equal(t.Columns.Count, t.Columns.Select(c => c.Name).Distinct().Count());
            Assert.DoesNotContain(t.Columns, c => c.Name is "Password" or "Comment" or "Contents");
        });
    }

    [Fact]
    public void TheSettingsTable_MasksASecretLookingValueInSql_AndNeverSelectsItRaw()
    {
        var sql = BlogAiTables.PageSql(BlogAiTables.Find("be_Settings")!);

        Assert.Contains("CASE WHEN [SettingName] LIKE N'%password%'", sql);
        Assert.Contains("OR [SettingName] LIKE N'%key%'", sql);
        Assert.Contains("THEN N'[masked]' ELSE [SettingValue] END AS [SettingValue]", sql);
        Assert.DoesNotContain(", [SettingValue],", sql);   // the raw column is only ever read inside the CASE
        Assert.EndsWith("ORDER BY [SettingRowId] OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY", sql);
    }

    [Fact]
    public void CustomFields_ArePagedByAllFourKeyColumns_AndAnIdIsDerivedFromThem()
    {
        var table = BlogAiTables.Find("be_CustomFields")!;
        var sql = BlogAiTables.PageSql(table);
        var row = new Dictionary<string, object?> { ["CustomType"] = "POST", ["ObjectId"] = "p1", ["BlogId"] = Guid.Empty, ["Key"] = "Role", ["Value"] = "Admin", ["Attribute"] = null };

        Assert.Contains("ORDER BY [CustomType], [ObjectId], [BlogId], [Key] OFFSET", sql);
        Assert.Equal("POST|p1|00000000-0000-0000-0000-000000000000|Role", table.IdentityOf(row));
        Assert.DoesNotContain("[masked]", sql);   // only the settings table has a secret rule
    }

    [Fact]
    public void TheWave1SeedsStayTheTablesTheyWereAppliedFor_AndWave2IsSeparate()
    {
        Assert.DoesNotContain(BlogAiTables.Wave1, t => t.Name is "be_Settings" or "be_CustomFields");
        Assert.Equal(["be_Settings", "be_CustomFields"], BlogAiTables.Wave2.Select(t => t.Name));
        Assert.Equal(BlogAiTables.Wave1.Count + BlogAiTables.Wave2.Count, BlogAiTables.All.Count);
    }

    [Fact]
    public void PageSql_NamesOnlyListedColumns_AndPagesWithParameters()
    {
        var sql = BlogAiTables.PageSql(BlogAiTables.Find("be_Users")!);

        Assert.Equal("SELECT [UserID], [BlogID], [UserName], [LastLoginTime], [EmailAddress] FROM dbo.[be_Users] ORDER BY [UserID] OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY", sql);
        Assert.DoesNotContain("Password", sql);
    }

    [Fact]
    public void LegacyConstants_MatchTheGenerator()
    {
        Assert.Equal(LegacySchemaSeed.SchemaBase, EntityConstants.Legacy.SchemaBase);
        Assert.Equal(LegacySchemaSeed.Graph, EntityConstants.Legacy.DefaultGraph);
    }
}

using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Materializes both a User schema and User instances from the same seed.nq artifact used
/// by the N-Quad Postgres tool tests, proving the dynamic entity model reconstructs typed
/// data purely from the parsed file (no live database required). Each field value carries
/// the id of its originating NQuad row (FieldValue), confirming the id is available for a
/// future update/delete against that row - not just the raw value.
/// </summary>
public sealed class UserAndUserSchemaTests
{
    [Fact]
    public void LoadUserSchema_And_LoadUsers_MaterializeAdminFromSeedFile()
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        var nquadText = File.ReadAllText(seedPath);
        var quads = new NQuadFileParser().Parse(nquadText);

        var schema = SchemaDal.Load(quads, EntityConstants.Schema.UserIri);
        Assert.Equal(EntityConstants.Schema.UserIri, schema.SchemaIri);
        Assert.Contains("Id", schema.Fields.Keys);
        Assert.Contains("First", schema.Fields.Keys);
        Assert.Contains("Last", schema.Fields.Keys);
        Assert.Contains("UserName", schema.Fields.Keys);
        Assert.Contains("Email", schema.Fields.Keys);
        Assert.Contains("Phone", schema.Fields.Keys);
        Assert.Contains("DOB", schema.Fields.Keys);

        var users = NQuadUserAdapter.LoadUsers(quads, schema).ToArray();
        var admin = Assert.Single(users, u => u.GetValue("UserName")?.ToString() == "Admin");

        Assert.NotNull(admin.EntityId);
        Assert.Equal("Admin User", admin.GetValue("DisplayName"));

        var userNameFieldValue = admin.GetFieldValue("UserName");
        Assert.NotNull(userNameFieldValue);
        Assert.False(string.IsNullOrWhiteSpace(userNameFieldValue!.Id.Value));
        Assert.Equal("Admin", userNameFieldValue.Value);
    }

    [Fact]
    public void LoadUsers_SeedFile_AlsoIncludesClaude()
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        var quads = new NQuadFileParser().Parse(File.ReadAllText(seedPath));
        var schema = SchemaDal.Load(quads, EntityConstants.Schema.UserIri);

        var users = NQuadUserAdapter.LoadUsers(quads, schema).ToArray();

        Assert.Contains(users, u => u.GetValue("UserName")?.ToString() == "Claude");
    }
}

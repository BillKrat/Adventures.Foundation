using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// <see cref="SchemaDal"/> owns the N-Quad-specific triple-walking that used to live in
/// <c>EntitySchema.Load</c>. Exercised against two different schemas already described in the
/// real seed file (User and AuditRecord) to prove it is not User-specific.
/// </summary>
public sealed class SchemaDalTests
{
    private static NQuad[] SeedQuads() =>
        new NQuadFileParser().Parse(File.ReadAllText(NQuadStoreTestSupport.SeedFilePath)).ToArray();

    [Fact]
    public void Load_UserSchema_ReturnsExpectedFields()
    {
        var schema = SchemaDal.Load(SeedQuads(), EntityConstants.Schema.UserIri);

        Assert.Equal(EntityConstants.Schema.UserIri, schema.SchemaIri);
        Assert.Contains("Id", schema.Fields.Keys);
        Assert.Contains("First", schema.Fields.Keys);
        Assert.Contains("Last", schema.Fields.Keys);
        Assert.Contains("UserName", schema.Fields.Keys);
        Assert.Contains("Email", schema.Fields.Keys);
        Assert.Equal("mailto:", schema.Fields["Email"].ValuePrefix);
    }

    [Fact]
    public void Load_AuditRecordSchema_ReturnsExpectedFields()
    {
        var schema = SchemaDal.Load(SeedQuads(), EntityConstants.AuditRecord.SchemaIri);

        Assert.Equal(EntityConstants.AuditRecord.SchemaIri, schema.SchemaIri);
        Assert.Contains("EntityType", schema.Fields.Keys);
        Assert.Contains("EntityId", schema.Fields.Keys);
        Assert.Contains("Action", schema.Fields.Keys);
        Assert.Contains("ChangedAt", schema.Fields.Keys);
        Assert.Contains("ChangedBy", schema.Fields.Keys);
    }

    [Fact]
    public void Load_UnknownSchemaIri_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => SchemaDal.Load(SeedQuads(), "https://global-webnet.com/schema/DoesNotExist"));
    }
}

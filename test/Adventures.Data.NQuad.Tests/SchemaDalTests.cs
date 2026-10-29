// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// <see cref="SchemaDal"/> owns the N-Quad-specific triple-walking that used to live in
/// <c>EntitySchema.Load</c>. Exercised against two different schemas already described in the
/// real seed file (User) to prove it is not User-specific.
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
    public void Load_ReadsOptionalFieldMetadata_AndDefaultsWhenAbsent()
    {
        const string schemaIri = "urn:test:Gadget";
        const string graph = "urn:g";
        var b = EntityConstants.Schema.BaseIri;
        NQuad Q(string s, string p, string o) => new(Guid.NewGuid(), s, p, o, graph);
        var quads = new[]
        {
            Q(schemaIri, EntityConstants.Schema.TypePredicate, EntityConstants.Schema.TypeIri),
            Q(schemaIri, EntityConstants.Schema.FieldPredicate, "urn:f:plain"),
            Q("urn:f:plain", EntityConstants.Schema.FieldNamePredicate, "Plain"),
            Q("urn:f:plain", EntityConstants.Schema.FieldTypePredicate, "String"),
            Q("urn:f:plain", EntityConstants.Schema.FieldRdfPredicate, "urn:p:plain"),
            Q(schemaIri, EntityConstants.Schema.FieldPredicate, "urn:f:rich"),
            Q("urn:f:rich", EntityConstants.Schema.FieldNamePredicate, "Rich"),
            Q("urn:f:rich", EntityConstants.Schema.FieldTypePredicate, "Date"),
            Q("urn:f:rich", EntityConstants.Schema.FieldRdfPredicate, "urn:p:rich"),
            Q("urn:f:rich", b + "label", "Date of birth"),
            Q("urn:f:rich", b + "readOnly", "true"),
            Q("urn:f:rich", b + "order", "5"),
            Q("urn:f:rich", b + "listVisible", "false"),
            Q("urn:f:rich", b + "required", "true"),
        };

        var schema = SchemaDal.Load(quads, schemaIri);

        var plain = schema.Fields["Plain"];
        Assert.Null(plain.Label);
        Assert.False(plain.IsReadOnly);
        Assert.Null(plain.Order);
        Assert.True(plain.IsListVisible);
        Assert.False(plain.IsRequired);
        var rich = schema.Fields["Rich"];
        Assert.Equal("Date of birth", rich.Label);
        Assert.True(rich.IsReadOnly);
        Assert.Equal(5, rich.Order);
        Assert.False(rich.IsListVisible);
        Assert.True(rich.IsRequired);
    }

    [Fact]
    public void Load_UnknownSchemaIri_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => SchemaDal.Load(SeedQuads(), "https://global-webnet.com/schema/DoesNotExist"));
    }
}

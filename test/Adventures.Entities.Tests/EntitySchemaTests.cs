using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="EntitySchema.Create"/> is a storage-agnostic value object constructor - it only
/// enforces the field-list's own invariants, never how the fields were discovered (that is
/// Adventures.Data.NQuad's <c>SchemaDal</c>, covered separately). These invariants used to be
/// exercised only indirectly through <c>EntitySchema.Load</c>'s triple-walking.
/// </summary>
public sealed class EntitySchemaTests
{
    private const string SchemaIri = "https://global-webnet.com/schema/Widget";

    private static EntitySchemaField Field(string name, string predicate) =>
        new(SchemaIri, Id: $"{SchemaIri}/{name}", name, Type: "String", predicate);

    [Fact]
    public void Create_BuildsFieldsAndFieldsByPredicate()
    {
        var idField = Field("Id", "urn:widget#id");
        var nameField = Field("Name", "urn:widget#name");

        var schema = EntitySchema.Create(SchemaIri, [idField, nameField]);

        Assert.Equal(SchemaIri, schema.SchemaIri);
        Assert.Equal(2, schema.Fields.Count);
        Assert.Same(idField, schema.Fields["Id"]);
        Assert.Same(nameField, schema.FieldsByPredicate["urn:widget#name"]);
    }

    [Fact]
    public void Create_FieldNameLookupIsCaseInsensitive()
    {
        var schema = EntitySchema.Create(SchemaIri, [Field("Name", "urn:widget#name")]);

        Assert.True(schema.Fields.ContainsKey("name"));
        Assert.True(schema.Fields.ContainsKey("NAME"));
    }

    [Fact]
    public void Create_NoFields_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EntitySchema.Create(SchemaIri, []));
    }

    [Fact]
    public void Create_DuplicateFieldName_Throws()
    {
        var duplicates = new[] { Field("Name", "urn:widget#name"), Field("Name", "urn:widget#name2") };

        Assert.Throws<InvalidOperationException>(() => EntitySchema.Create(SchemaIri, duplicates));
    }

    [Fact]
    public void Create_TwoFieldsMappedToSamePredicate_Throws()
    {
        var duplicates = new[] { Field("Name", "urn:widget#same"), Field("Nickname", "urn:widget#same") };

        Assert.Throws<InvalidOperationException>(() => EntitySchema.Create(SchemaIri, duplicates));
    }

    [Fact]
    public void Create_BlankSchemaIri_Throws()
    {
        Assert.Throws<ArgumentException>(() => EntitySchema.Create(" ", [Field("Name", "urn:widget#name")]));
    }
}

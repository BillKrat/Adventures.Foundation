// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="EntityFormModel.From"/> is the "standard object" projection - a schema-driven form
/// screen's schema part and value part, built purely from an <see cref="EntitySchema"/> and a
/// <see cref="DynamicEntity"/>, no store involved. Deliberately plain records (not
/// <see cref="EntitySchema"/>/<see cref="DynamicEntity"/> themselves): those are DAL/domain-shaped
/// and not something a UI form control should depend on directly.
/// </summary>
public sealed class EntityFormModelTests
{
    private static EntitySchema WidgetSchema() => EntitySchema.Create(
        "Widget",
        [
            new EntitySchemaField("Widget", "id-1", "Id", "String", "urn:widget#id"),
            new EntitySchemaField("Widget", "id-2", "Name", "String", "urn:widget#name", IsRequired: true),
        ]);

    [Fact]
    public void From_BuildsSchemaAndDataParts()
    {
        var schema = WidgetSchema();
        var entity = new User(schema)
            .Set(DynamicEntity.EntityIdPropertyName, "w-1", "w-1")
            .Set("Name", "q-1", "Lamp");

        var model = EntityFormModel.From(schema, entity);

        Assert.Equal("Widget", model.Schema.SchemaIri);
        Assert.Equal(2, model.Schema.Fields.Count);
        Assert.Contains(model.Schema.Fields, f => f.Name == "Name" && f.Type == "String" && f.IsRequired);
        Assert.Contains(model.Schema.Fields, f => f.Name == "Id" && !f.IsRequired);

        Assert.Equal("w-1", model.Entity.EntityId);
        Assert.Equal("Lamp", model.Entity.Values["Name"]);
    }

    [Fact]
    public void Fields_CarryLabelReadOnlyOrderAndListVisibility_WithSensibleDefaults()
    {
        var schema = EntitySchema.Create(
            "Widget",
            [
                new EntitySchemaField("Widget", "f1", "Id", "Guid", "urn:w#id"),
                new EntitySchemaField("Widget", "f2", "DisplayName", "String", "urn:w#d", Order: 20),
                new EntitySchemaField("Widget", "f3", "DOB", "Date", "urn:w#dob", Label: "Born", Order: 10, IsListVisible: false),
                new EntitySchemaField("Widget", "f4", "Code", "String", "urn:w#c", IsReadOnly: true),
            ]);

        var fields = EntitySchemaModel.From(schema).Fields;

        // no declared order falls back to the schema position (Id 0, Code 3); declared orders (10, 20) sort after
        Assert.Equal(["Id", "Code", "DOB", "DisplayName"], fields.Select(f => f.Name));
        Assert.Equal("Born", fields.Single(f => f.Name == "DOB").Label);
        Assert.False(fields.Single(f => f.Name == "DOB").IsListVisible);
        Assert.True(fields.Single(f => f.Name == "Code").IsReadOnly);
        Assert.True(fields.Single(f => f.Name == "Id").IsReadOnly);
    }

    [Theory]
    [InlineData("DisplayName", "Display name")]
    [InlineData("DOB", "DOB")]
    [InlineData("UserName", "User name")]
    [InlineData("First", "First")]
    [InlineData("OccurredAt", "Occurred at")]
    public void Humanize_SplitsCamelCaseIntoWords(string name, string expected) =>
        Assert.Equal(expected, EntityFieldSchema.Humanize(name));

    [Fact]
    public void Form_CarriesFullPermissionsAndTheContractVersionByDefault()
    {
        var schema = WidgetSchema();
        var entity = new User(schema).Set(DynamicEntity.EntityIdPropertyName, "w-1", "w-1");

        var model = EntityFormModel.From(schema, entity);

        Assert.Equal(EntityPermissions.Full, model.Permissions);
        Assert.Equal(1, model.ContractVersion);
        Assert.True(model.Schema.Fields.Single(f => f.Name == "Id").IsReadOnly);
        Assert.Equal("Name", model.Schema.Fields.Single(f => f.Name == "Name").Label);
    }

    [Fact]
    public void From_FieldWithNoValue_IsNullInValues()
    {
        var schema = WidgetSchema();
        var entity = new User(schema).Set(DynamicEntity.EntityIdPropertyName, "w-1", "w-1");

        var model = EntityFormModel.From(schema, entity);

        Assert.Null(model.Entity.Values["Name"]);
    }
}

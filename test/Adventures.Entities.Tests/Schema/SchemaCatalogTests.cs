using Adventures.Entities;
using Xunit;

namespace Adventures.Entities.Tests;

public class SchemaCatalogTests
{
    private const string Tenant = "https://schema.example/identity/Tenant";
    private const string Role = "https://schema.example/identity/Role";
    private const string Assignment = "https://schema.example/identity/Assignment";
    private const string Post = "https://schema.example/content/Post";

    private static EntitySchemaField Field(string schema, string name, string type = "string", int? order = null, bool required = false, string? label = null) =>
        new(schema, schema + "/" + name, name, type, "urn:p:" + name, IsRequired: required, Label: label, Order: order);

    private static SchemaCatalog Catalog() => new(
    [
        EntitySchema.Create(Tenant, [Field(Tenant, "Name", required: true, label: "Tenant name")], [new EntityRelationDefinition("People", "People", Assignment, "urn:p:Tenant", true)], "Name"),
        EntitySchema.Create(Role, [Field(Role, "Name")]),
        EntitySchema.Create(Assignment, [Field(Assignment, "RoleIds", "refs:" + Role, order: 2), Field(Assignment, "TenantId", "ref:" + Tenant, order: 1)]),
        EntitySchema.Create(Post, [Field(Post, "DisplayName")]),
    ], unreadable: ["urn:broken"]);

    [Fact]
    public void The_list_is_ordered_by_area_then_name_with_both_taken_from_the_address()
    {
        var list = Catalog().List();

        Assert.Equal(
            [("content", "Post", 1), ("identity", "Assignment", 2), ("identity", "Role", 1), ("identity", "Tenant", 1)],
            list.Schemas.Select(s => (s.Area, s.Name, s.FieldCount)));
        Assert.Equal(["urn:broken"], list.Unreadable);
    }

    [Theory]
    [InlineData("https://example.com/schema/User", "schema", "User")]
    [InlineData("https://example.com/ontology/schema#SchemaField", "schema", "SchemaField")]
    [InlineData("https://example.com/Alone", "", "Alone")]
    [InlineData("https://example.com/area/Name/", "area", "Name")]
    [InlineData("urn:thing", "", "urn:thing")]
    public void Any_address_gives_an_area_and_a_name(string id, string area, string name)
    {
        var summary = Assert.Single(new SchemaCatalog([EntitySchema.Create(id, [Field(id, "X")])]).List().Schemas);

        Assert.Equal((area, name), (summary.Area, summary.Name));
    }

    [Fact]
    public void A_schema_shows_its_fields_in_order_with_labels_pointers_and_relations()
    {
        var assignment = Catalog().Find(Assignment)!;
        var tenant = Catalog().Find(Tenant)!;
        var post = Catalog().Find(Post)!;

        Assert.Equal(["TenantId", "RoleIds"], assignment.Fields.Select(f => f.Name));
        Assert.Equal([Tenant, Role], assignment.Fields.Select(f => f.RefSchemaId));
        Assert.Equal("Tenant name", tenant.Fields[0].Label);
        Assert.True(tenant.Fields[0].IsRequired);
        Assert.Equal("urn:p:Name", tenant.Fields[0].Predicate);
        Assert.Equal("Name", tenant.DisplayField);
        Assert.Equal(new SchemaRelationDescription("People", "People", Assignment, "urn:p:Tenant", true), Assert.Single(tenant.Relations));
        Assert.Equal("Display name", post.Fields[0].Label); // no label in the data: the name, made readable
        Assert.Null(post.Fields[0].RefSchemaId);
    }

    [Fact]
    public void A_schema_shows_who_points_at_it()
    {
        Assert.Equal([new SchemaUse(Assignment, "Assignment", "TenantId")], Catalog().Find(Tenant)!.UsedBy);
        Assert.Equal([new SchemaUse(Assignment, "Assignment", "RoleIds")], Catalog().Find(Role)!.UsedBy);
        Assert.Empty(Catalog().Find(Post)!.UsedBy);
    }

    [Theory]
    [InlineData("https://schema.example/identity/Nothing")]
    [InlineData("urn:broken")]
    [InlineData("")]
    public void An_unknown_or_unreadable_schema_is_not_found(string id) => Assert.Null(Catalog().Find(id));
}

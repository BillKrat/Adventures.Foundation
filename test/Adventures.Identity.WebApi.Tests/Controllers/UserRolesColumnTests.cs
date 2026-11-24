using Adventures.Entities;
using Adventures.Identity.WebApi;
using Xunit;

namespace Adventures.Identity.WebApi.Tests.Controllers;

/// <summary>DCP (user usage review): the Users list has a Roles column, like the legacy blog, so an administrator sees who holds what without opening each person.</summary>
public sealed class UserRolesColumnTests
{
    private static EntityGridModel Grid() => new(
        new EntitySchemaModel("urn:user", [new EntityFieldSchema("UserName", "String", true, "User name", Order: 1), new EntityFieldSchema("Email", "String", false, "Email", Order: 2)]),
        [
            new EntityDataModel("u1", new Dictionary<string, string?> { ["UserName"] = "Admin", ["Email"] = "a@x.test" }),
            new EntityDataModel("u2", new Dictionary<string, string?> { ["UserName"] = "Claude" }),
        ]);

    [Fact]
    public void A_Roles_column_is_added_read_only_and_listed_after_the_other_fields()
    {
        var grid = UserRolesColumn.Add(Grid(), new Dictionary<string, string> { ["u1"] = "Admin, Editor" });

        var field = grid.Schema.Fields.Single(f => f.Name == "Roles");
        Assert.True(field.IsReadOnly);
        Assert.True(field.IsListVisible);
        Assert.Equal(grid.Schema.Fields.Max(f => f.Order), field.Order);
        Assert.Equal("Roles", field.Label);
    }

    [Fact]
    public void Each_row_shows_its_roles_and_a_person_with_none_shows_an_empty_cell()
    {
        var grid = UserRolesColumn.Add(Grid(), new Dictionary<string, string> { ["u1"] = "Admin, Editor" });

        Assert.Equal("Admin, Editor", grid.Rows.Single(r => r.EntityId == "u1").Values["Roles"]);
        Assert.Equal(string.Empty, grid.Rows.Single(r => r.EntityId == "u2").Values["Roles"]);
    }

    [Fact]
    public void A_grid_that_already_has_a_Roles_field_is_left_alone()
    {
        var once = UserRolesColumn.Add(Grid(), new Dictionary<string, string>());

        Assert.Equal(once.Schema.Fields.Count, UserRolesColumn.Add(once, new Dictionary<string, string>()).Schema.Fields.Count);
    }

    [Fact]
    public void The_role_names_of_a_person_are_the_distinct_names_of_the_assignments_in_the_tenant_being_worked_in()
    {
        var rows = new[]
        {
            new EntityDataModel("a1", new Dictionary<string, string?> { ["Role"] = "r-admin", ["Tenant"] = "t1" }, new Dictionary<string, string> { ["Role"] = "Admin" }),
            new EntityDataModel("a2", new Dictionary<string, string?> { ["Role"] = "r-editor", ["Tenant"] = "t1" }, new Dictionary<string, string> { ["Role"] = "Editor" }),
            new EntityDataModel("a3", new Dictionary<string, string?> { ["Role"] = "r-dev", ["Tenant"] = "t2" }, new Dictionary<string, string> { ["Role"] = "Developer" }),
            new EntityDataModel("a4", new Dictionary<string, string?> { ["Role"] = "r-admin", ["Tenant"] = "t1" }, new Dictionary<string, string> { ["Role"] = "Admin" }),
        };

        Assert.Equal("Admin, Editor", UserRolesColumn.NamesOf(rows, "t1"));
        Assert.Equal("Developer", UserRolesColumn.NamesOf(rows, "t2"));
        Assert.Equal(string.Empty, UserRolesColumn.NamesOf([], "t1"));
    }
}

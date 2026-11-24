using Adventures.Entities;

namespace Adventures.Identity.WebApi;

/// <summary>The Roles column of the Users list (DCP, user usage review): who holds which role, read from their role assignments, shown beside the other columns.</summary>
public static class UserRolesColumn
{
    public const string FieldName = "Roles";

    /// <summary>The grid with a read-only Roles column; <paramref name="rolesByRowId"/> maps a person's id to their role names (a person with none shows an empty cell).</summary>
    public static EntityGridModel Add(EntityGridModel grid, IReadOnlyDictionary<string, string> rolesByRowId)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Schema.Fields.Any(f => f.Name == FieldName))
        {
            return grid;
        }

        var order = grid.Schema.Fields.Count == 0 ? 0 : grid.Schema.Fields.Max(f => f.Order);
        var field = new EntityFieldSchema(FieldName, "String", false, "Roles", IsReadOnly: true, Order: order, IsListVisible: true);
        var rows = grid.Rows
            .Select(row => row with { Values = new Dictionary<string, string?>(row.Values) { [FieldName] = rolesByRowId.GetValueOrDefault(row.EntityId, string.Empty) } })
            .ToList();
        return grid with { Schema = grid.Schema with { Fields = [.. grid.Schema.Fields, field] }, Rows = rows };
    }

    /// <summary>The distinct role names among one person's assignments that apply in the tenant being worked in (an assignment with no tenant, an organisation one, counts everywhere).</summary>
    public static string NamesOf(IEnumerable<EntityDataModel> assignments, string? tenantId, IReadOnlyDictionary<string, string>? roleNames = null) =>
        string.Join(", ", assignments
            .Where(row => tenantId is null || !row.Values.TryGetValue("Tenant", out var assigned) || string.IsNullOrEmpty(assigned) || assigned == tenantId)
            .Select(row => row.Values.GetValueOrDefault("Role") is { Length: > 0 } id && roleNames is not null && roleNames.TryGetValue(id, out var named) ? named : row.Display?.GetValueOrDefault("Role") ?? row.Values.GetValueOrDefault("Role"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal));
}

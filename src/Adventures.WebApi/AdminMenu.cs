using System.Security.Claims;
using Adventures.Security;

namespace Adventures.WebApi;

/// <summary>
/// One entry the host declares for the admin menu: where it sits (section), what it is called, where it goes, which role may see
/// it, and whether the screen exists yet (<see cref="Available"/> false lists the destination as "coming" without a route). The
/// host registers these once; the client never decides what an administrator sees.
/// </summary>
public sealed record AdminMenuItemDefinition(
    string Section,
    string Id,
    string Label,
    string? Route,
    string? Icon = null,
    string? RequiredRole = null,
    bool Available = true,
    int Order = 0);

/// <summary>One menu entry as the client renders it. <see cref="Badge"/> is a count to show beside it (active exceptions), or null.</summary>
public sealed record AdminMenuItem(string Id, string Label, string? Route, string? Icon, bool Available, int? Badge);

public sealed record AdminMenuSection(string Id, string Label, IReadOnlyList<AdminMenuItem> Items);

/// <summary>The admin navigation for the caller: only the sections and items their roles allow, in the declared order. Empty for a caller with no admin role.</summary>
public sealed record AdminMenuModel(IReadOnlyList<AdminMenuSection> Sections, int ContractVersion = 1)
{
    public bool IsEmpty => Sections.Count == 0;
}

/// <summary>Builds the <see cref="AdminMenuModel"/> a caller may see from the host declarations (role filtering, ordering, badges).</summary>
public static class AdminMenuBuilder
{
    /// <param name="definitions">Every entry the host declared, in any order; sections keep the order of their first entry.</param>
    /// <param name="user">The caller; their roles come from the token claim, the same two spellings the role policies accept.</param>
    /// <param name="badges">Counts to show beside an entry, by item id.</param>
    public static AdminMenuModel Build(
        IEnumerable<AdminMenuItemDefinition> definitions, ClaimsPrincipal user, IReadOnlyDictionary<string, int>? badges = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(user);

        var roles = user.FindAll(claim => claim.Type is WellKnownClaims.Roles or ClaimTypes.Role)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var signedIn = user.Identity?.IsAuthenticated == true;

        var visible = definitions
            .Where(item => item.RequiredRole is null ? signedIn : roles.Contains(item.RequiredRole))
            .ToList();

        var sections = visible
            .GroupBy(item => item.Section, StringComparer.Ordinal)
            .Select(group => new AdminMenuSection(
                group.Key,
                group.Key,
                group.OrderBy(item => item.Order)
                    .Select(item => new AdminMenuItem(
                        item.Id,
                        item.Label,
                        item.Available ? item.Route : null,
                        item.Icon,
                        item.Available,
                        badges is not null && badges.TryGetValue(item.Id, out var count) && count > 0 ? count : null))
                    .ToList()))
            .ToList();
        return new AdminMenuModel(sections);
    }
}

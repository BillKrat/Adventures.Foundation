using System.Text.Json;
using Adventures.Data;

namespace Adventures.Identity;

/// <summary>"active" | "disabled" | "must_change_password" (Sql/schema-users.sql) as a real enum.</summary>
public enum UserStatus
{
    Active,
    Disabled,
    MustChangePassword,
}

/// <summary>
/// Parsed view of a user <see cref="Entity"/>'s <c>standard_fields</c> JSONB (see
/// <c>Sql/schema-users.sql</c> for the authoritative shape) - the fields every user has,
/// regardless of tenant/org. Anything beyond these lives as <c>entity_triples</c> rows instead.
/// </summary>
public sealed record UserAccount(
    Guid Id,
    string Tenant,
    string Org,
    string Username,
    string Email,
    string DisplayName,
    UserStatus Status,
    IReadOnlyList<string> Roles)
{
    /// <summary>
    /// Parses the standard user fields out of an entity's <see cref="Entity.StandardFieldsJson"/>.
    /// Throws <see cref="FormatException"/> if <paramref name="entity"/> isn't <c>entity_type =
    /// "user"</c> or is missing the required <c>username</c> field - callers should only pass
    /// entities already filtered to that type (e.g. via <see cref="IEntityRepository.FindByStandardFieldAsync"/>).
    /// </summary>
    public static UserAccount FromEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.EntityType != "user")
        {
            throw new FormatException($"Expected entity_type 'user', got '{entity.EntityType}'.");
        }

        using var document = JsonDocument.Parse(entity.StandardFieldsJson);
        var root = document.RootElement;

        var roles = root.TryGetProperty("roles", out var rolesElement)
            ? rolesElement.EnumerateArray().Select(r => r.GetString() ?? string.Empty).ToArray()
            : [];

        return new UserAccount(
            entity.Id,
            entity.Tenant,
            entity.Org,
            root.TryGetProperty("username", out var username) ? username.GetString() ?? throw new FormatException("'username' is null.") : throw new FormatException("Missing 'username'."),
            root.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("display_name", out var displayName) ? displayName.GetString() ?? string.Empty : string.Empty,
            ParseStatus(root.TryGetProperty("status", out var status) ? status.GetString() : null),
            roles);
    }

    private static UserStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "disabled" => UserStatus.Disabled,
        "must_change_password" => UserStatus.MustChangePassword,
        _ => UserStatus.Active,
    };
}

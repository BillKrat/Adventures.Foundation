using Adventures.Entities;

namespace Adventures.Entities;

/// <summary>The seeds of the identity schemas: tenant and role model, required fields, profile fields, role rights, tenant aliases.</summary>
public static class IdentitySeeds
{
    /// <summary>In application order.</summary>
    public static IReadOnlyList<SeedEntry> All { get; } =
    [
        new("seed-004-identity.nq", IdentityConstants.SeedName),
        new("seed-005-identity-required.nq", IdentityConstants.RequiredSeedName),
        new("seed-027-profile-fields.nq", IdentityConstants.ProfileSeedName),
        new("seed-018-role-rights.nq", "018-role-rights"),
        new("seed-020-tenant-aliases.nq", "020-tenant-aliases"),
    ];
}


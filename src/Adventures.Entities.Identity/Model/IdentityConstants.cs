namespace Adventures.Entities;

/// <summary>The User entity's well-known IRIs (the person model of the seed data).</summary>
public static class UserConstants
{
    public const string SchemaIri = "https://global-webnet.com/schema/User";
    public const string BaseIri = "https://global-webnet.com/id/user/";
    public const string DefaultGraph = "https://global-webnet.com/graph/user/b7963bd0-5ad3-4c83-b61c-fa622cc6a2df";
    public const string TypeIri = "http://xmlns.com/foaf/0.1/Person";
}

/// <summary>
/// The tenant and role model already present in the baseline seed (roles, tenants, organizations and the role assignments
/// that point from a user), described as schemas by <c>Sql/seed/seed-004-identity.nq</c> under the schema host convention.
/// The data is untouched; the schemas map onto its existing predicates, so the assignments stay where they are
/// (the user points at each assignment).
/// </summary>
public static class IdentityConstants
{
    public const string SeedName = "004-identity";

    /// <summary>Seed 005 marks the assignment pointer fields required (seed 004 had already been applied, and a named seed applies once).</summary>
    public const string RequiredSeedName = "005-identity-required";
    public const string DefaultGraph = "https://global-webnet.com/graph/user/b7963bd0-5ad3-4c83-b61c-fa622cc6a2df";
    public const string SchemaBase = "https://schema.global-webnet.com/identity/";
    public const string IdBase = "https://global-webnet.com/id/";
    public const string OntologyBase = "https://global-webnet.com/ontology/tenant#";

    public const string RoleSchemaIri = SchemaBase + "Role";
    public const string RoleBaseIri = IdBase + "role/";
    public const string RoleTypeIri = OntologyBase + "Role";

    public const string TenantSchemaIri = SchemaBase + "Tenant";
    public const string TenantBaseIri = IdBase + "tenant/";
    public const string TenantTypeIri = OntologyBase + "Tenant";

    public const string BlogSchemaIri = SchemaBase + "Blog";
    public const string BlogBaseIri = IdBase + "blog/";
    public const string BlogTypeIri = OntologyBase + "Blog";

    public const string OrganizationSchemaIri = SchemaBase + "Organization";
    public const string OrganizationBaseIri = IdBase + "organization/";
    public const string OrganizationTypeIri = OntologyBase + "Organization";

    public const string TenantRoleSchemaIri = SchemaBase + "TenantRoleAssignment";
    public const string TenantRoleBaseIri = IdBase + "tenant-role/";
    public const string TenantRoleTypeIri = OntologyBase + "TenantRoleAssignment";

    public const string OrganizationRoleSchemaIri = SchemaBase + "OrganizationRoleAssignment";
    public const string OrganizationRoleBaseIri = IdBase + "organization-role/";
    public const string OrganizationRoleTypeIri = OntologyBase + "OrganizationRoleAssignment";
}

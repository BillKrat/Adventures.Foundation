using Adventures.Data.NQuad;
using Adventures.Entities;

namespace Adventures.Browser.Host;

/// <summary>One kind of entity the browser can list and edit: a short key for the URL and the registration that says where it is stored.</summary>
public sealed record BrowserType(string Key, string Title, EntityTypeRegistration Registration);

/// <summary>
/// The entity kinds the browser offers, built from the schemas present in the store (a kind whose schema is not in the store is left out). Only
/// the generic model is named here: schemas and their fields, users and their roles and tenants, and the exception log. A feature's own types
/// (posts, collaboration items) are not known to the browser, which is the point of keeping it independent of them.
/// </summary>
public static class BrowserCatalog
{
    public static IReadOnlyList<BrowserType> Build(IReadOnlyList<NQuad> quads)
    {
        var graph = IdentityConstants.DefaultGraph;
        var candidates = new (string Key, string Title, string SchemaIri, string BaseIri, string TypeIri, string Graph)[]
        {
            ("schemas", "Schemas", EntityConstants.Schema.EntityTypeIri, EntityConstants.Schema.EntityBaseIri, EntityConstants.Schema.EntityTypeIri, UserConstants.DefaultGraph),
            ("schema-fields", "Schema fields", EntityConstants.Schema.FieldEntityTypeIri, EntityConstants.Schema.FieldEntityBaseIri, EntityConstants.Schema.FieldEntityTypeIri, UserConstants.DefaultGraph),
            ("users", "Users", UserConstants.SchemaIri, UserConstants.BaseIri, UserConstants.TypeIri, UserConstants.DefaultGraph),
            ("roles", "Roles", IdentityConstants.RoleSchemaIri, IdentityConstants.RoleBaseIri, IdentityConstants.RoleTypeIri, graph),
            ("tenants", "Tenants", IdentityConstants.TenantSchemaIri, IdentityConstants.TenantBaseIri, IdentityConstants.TenantTypeIri, graph),
            ("blogs", "Blogs (tenant sites)", IdentityConstants.BlogSchemaIri, IdentityConstants.BlogBaseIri, IdentityConstants.BlogTypeIri, graph),
            ("organizations", "Organizations", IdentityConstants.OrganizationSchemaIri, IdentityConstants.OrganizationBaseIri, IdentityConstants.OrganizationTypeIri, graph),
            ("tenant-roles", "Tenant role assignments", IdentityConstants.TenantRoleSchemaIri, IdentityConstants.TenantRoleBaseIri, IdentityConstants.TenantRoleTypeIri, graph),
            ("organization-roles", "Organization role assignments", IdentityConstants.OrganizationRoleSchemaIri, IdentityConstants.OrganizationRoleBaseIri, IdentityConstants.OrganizationRoleTypeIri, graph),
            ("exceptions", "Exception log", ExceptionLogConstants.SchemaIri, ExceptionLogConstants.BaseIri, ExceptionLogConstants.TypeIri, ExceptionLogConstants.DefaultGraph),
            ("exception-signatures", "Exception signatures", ExceptionSignatureConstants.SchemaIri, ExceptionSignatureConstants.BaseIri, ExceptionSignatureConstants.TypeIri, ExceptionSignatureConstants.DefaultGraph),
        };

        var types = new List<BrowserType>();
        foreach (var candidate in candidates)
        {
            if (!quads.Any(q => string.Equals(q.Subject, candidate.SchemaIri, StringComparison.Ordinal)))
            {
                continue;
            }

            var schema = SchemaDal.Load(quads, candidate.SchemaIri);
            types.Add(new BrowserType(candidate.Key, candidate.Title, new EntityTypeRegistration(schema, candidate.BaseIri, candidate.TypeIri, candidate.Graph)));
        }

        return types;
    }
}

using Adventures.Blog;
using Adventures.Collaboration;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Entities.NQuad;

/// <summary>
/// Each feature registers its own entity repositories over the seeded N-Quad store (R2 of the host reuse review): a host calls one method per feature it uses, with the quads read from the store,
/// instead of listing thirty registrations. The schema, base IRI, type IRI and graph of every type are unchanged from the host list these replace.
/// </summary>
public static class FeatureEntityRegistrations
{
    /// <summary>The user and the schema model itself. The schema repositories are needed because the lifetime scan registers the schema business layer whether or not a host calls it.</summary>
    public static IServiceCollection AddCoreEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, UserConstants.SchemaIri), UserConstants.BaseIri, UserConstants.TypeIri, UserConstants.DefaultGraph, schema => new User(schema));
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, EntityConstants.Schema.EntityTypeIri), EntityConstants.Schema.EntityBaseIri, EntityConstants.Schema.EntityTypeIri,
            UserConstants.DefaultGraph, schema => new SchemaEntity(schema));
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, EntityConstants.Schema.FieldEntityTypeIri), EntityConstants.Schema.FieldEntityBaseIri, EntityConstants.Schema.FieldEntityTypeIri,
            UserConstants.DefaultGraph, schema => new SchemaFieldEntity(schema));
        return services;
    }

    public static IServiceCollection AddDiagnosticsEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, ExceptionLogConstants.SchemaIri), ExceptionLogConstants.BaseIri, ExceptionLogConstants.TypeIri,
            ExceptionLogConstants.DefaultGraph, schema => new ExceptionLogEntry(schema));
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, ExceptionSignatureConstants.SchemaIri), ExceptionSignatureConstants.BaseIri, ExceptionSignatureConstants.TypeIri,
            ExceptionSignatureConstants.DefaultGraph, schema => new ExceptionSignatureEntry(schema));
        return services;
    }

    /// <summary>Role, tenant, blog, organization and the two role-assignment types (seed 004): the data is untouched, each schema maps onto the existing predicates.</summary>
    public static IServiceCollection AddIdentityEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        static void Add<TEntity>(IServiceCollection s, IReadOnlyList<Adventures.Data.NQuad.NQuad> q, string schemaIri, string baseIri, string typeIri, Func<EntitySchema, TEntity> factory)
            where TEntity : DynamicEntity =>
            s.AddNQuadEntityRepository(SchemaDal.Load(q, schemaIri), baseIri, typeIri, IdentityConstants.DefaultGraph, factory);

        Add(services, quads, IdentityConstants.RoleSchemaIri, IdentityConstants.RoleBaseIri, IdentityConstants.RoleTypeIri, s => new Role(s));
        Add(services, quads, IdentityConstants.TenantSchemaIri, IdentityConstants.TenantBaseIri, IdentityConstants.TenantTypeIri, s => new Tenant(s));
        Add(services, quads, IdentityConstants.BlogSchemaIri, IdentityConstants.BlogBaseIri, IdentityConstants.BlogTypeIri, s => new Blog(s));
        Add(services, quads, IdentityConstants.OrganizationSchemaIri, IdentityConstants.OrganizationBaseIri, IdentityConstants.OrganizationTypeIri, s => new Organization(s));
        Add(services, quads, IdentityConstants.TenantRoleSchemaIri, IdentityConstants.TenantRoleBaseIri, IdentityConstants.TenantRoleTypeIri, s => new TenantRoleAssignment(s));
        Add(services, quads, IdentityConstants.OrganizationRoleSchemaIri, IdentityConstants.OrganizationRoleBaseIri, IdentityConstants.OrganizationRoleTypeIri, s => new OrganizationRoleAssignment(s));
        return services;
    }

    /// <summary>Pages, posts, categories, tags, visitor comments, site settings, widget placements and language files: all in the request tenant's own graph.</summary>
    public static IServiceCollection AddBlogEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        static void Add<TEntity>(IServiceCollection s, IReadOnlyList<Adventures.Data.NQuad.NQuad> q, string schemaIri, string baseIri, string typeIri, string graph, Func<EntitySchema, TEntity> factory)
            where TEntity : DynamicEntity =>
            s.AddNQuadEntityRepository(SchemaDal.Load(q, schemaIri), baseIri, typeIri, graph, factory, tenantScoped: true);

        var graph = BlogConstants.Content.DefaultGraph;
        Add(services, quads, BlogConstants.Content.PageSchemaIri, BlogConstants.Content.PageBaseIri, BlogConstants.Content.PageTypeIri, graph, s => new Page(s));
        Add(services, quads, BlogConstants.Content.PostSchemaIri, BlogConstants.Content.PostBaseIri, BlogConstants.Content.PostTypeIri, graph, s => new Post(s));
        Add(services, quads, BlogConstants.Content.CategorySchemaIri, BlogConstants.Content.CategoryBaseIri, BlogConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        Add(services, quads, BlogConstants.Content.TagSchemaIri, BlogConstants.Content.TagBaseIri, BlogConstants.Content.TagTypeIri, graph, s => new Tag(s));
        Add(services, quads, BlogConstants.Content.VisitorCommentSchemaIri, BlogConstants.Content.VisitorCommentBaseIri, BlogConstants.Content.VisitorCommentTypeIri, graph, s => new VisitorComment(s));
        Add(services, quads, WidgetConstants.SchemaIri, WidgetConstants.BaseIri, WidgetConstants.TypeIri, WidgetConstants.DefaultGraph, s => new WidgetPlacement(s));
        // The language files (one table of texts per language, per tenant) the site language picks from.
        Add(services, quads, LanguageConstants.SchemaIri, LanguageConstants.BaseIri, LanguageConstants.TypeIri, LanguageConstants.DefaultGraph, s => new LanguageFile(s));
        Add(services, quads, BlogConstants.Site.SettingSchemaIri, BlogConstants.Site.SettingBaseIri, BlogConstants.Site.SettingTypeIri, BlogConstants.Site.DefaultGraph, s => new SiteSetting(s));
        return services;
    }

    /// <summary>The conversation under a post and the collaboration items (never public, a type of their own).</summary>
    public static IServiceCollection AddCollaborationEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, CollaborationConstants.CommentSchemaIri), CollaborationConstants.CommentBaseIri, CollaborationConstants.CommentTypeIri,
            BlogConstants.Content.DefaultGraph, s => new Comment(s), tenantScoped: true);
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, CollaborationConstants.SchemaIri), CollaborationConstants.BaseIri, CollaborationConstants.TypeIri,
            CollaborationConstants.DefaultGraph, s => new CollaborationItem(s), tenantScoped: true);
        return services;
    }

    /// <summary>The on/off state of the plug-ins and the settings an administrator customizes for each.</summary>
    public static IServiceCollection AddPluginEntities(this IServiceCollection services, IReadOnlyList<Adventures.Data.NQuad.NQuad> quads)
    {
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, PluginConstants.SchemaIri), PluginConstants.BaseIri, PluginConstants.TypeIri,
            CollaborationConstants.DefaultGraph, s => new Plugin(s), tenantScoped: true);
        services.AddNQuadEntityRepository(
            SchemaDal.Load(quads, PluginConstants.SettingSchemaIri), PluginConstants.SettingBaseIri, PluginConstants.SettingTypeIri,
            CollaborationConstants.DefaultGraph, s => new PluginSetting(s), tenantScoped: true);
        return services;
    }
}

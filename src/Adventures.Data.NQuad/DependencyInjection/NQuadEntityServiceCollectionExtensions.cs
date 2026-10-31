// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary>
/// Host-side registration of the N-Quad-backed <see cref="IEntityRepository{TEntity}"/> and the generic
/// <see cref="EntityBll{TEntity}"/> above it, so a host's composition root never has to know a repository's base IRI,
/// type IRI or graph (AppArch: only the Dal knows how data is stored) or repeat the construction per entity type.
/// </summary>
public static class NQuadEntityServiceCollectionExtensions
{
    /// <summary>Registers a scoped <see cref="IEntityRepository{TEntity}"/> over the registered <see cref="INQuadStore"/>.</summary>
    public static IServiceCollection AddNQuadEntityRepository<TEntity>(
        this IServiceCollection services,
        EntitySchema schema,
        string baseIri,
        string typeIri,
        string graph,
        Func<EntitySchema, TEntity> entityFactory)
        where TEntity : DynamicEntity
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entityFactory);

        // The typed schema holder is what presenters and Blls take; a bare EntitySchema could only ever mean one schema.
        services.AddSingleton(new EntitySchemaOf<TEntity>(schema));
        services.AddSingleton(new EntityTypeRegistration(schema, baseIri, typeIri, graph));

        return services.AddScoped<IEntityRepository<TEntity>>(provider =>
            new NQuadEntityRepository<TEntity>(provider.GetRequiredService<INQuadStore>(), schema, baseIri, typeIri, graph, entityFactory));
    }

    /// <summary>
    /// Registers <see cref="EntityBll{TEntity}"/> for every <see cref="IEntityBll{TEntity}"/> not given a more specific
    /// registration. An entity with its own Bll (e.g. <c>UserBll</c>, found by the Ioc scan) is registered for its own
    /// closed interface and wins for that entity because it is registered later by the scan or by the host.
    /// </summary>
    public static IServiceCollection AddEntityBll(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped(typeof(IEntityBll<>), typeof(EntityBll<>));
        return services;
    }
}

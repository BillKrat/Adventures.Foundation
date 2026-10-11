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
    /// <summary>The audit actor recorded when no signed-in user is available (startup seeding, background work).</summary>
    public const string SystemActor = "system";

    /// <summary>
    /// Registers a scoped <see cref="IEntityRepository{TEntity}"/> over the registered <see cref="INQuadStore"/>.
    /// <paramref name="changedBy"/> is evaluated per scope, so the audit trail records who made each change
    /// (e.g. the current request's user name); null or blank falls back to <see cref="SystemActor"/>.
    /// </summary>
    public static IServiceCollection AddNQuadEntityRepository<TEntity>(
        this IServiceCollection services,
        EntitySchema schema,
        string baseIri,
        string typeIri,
        string graph,
        Func<EntitySchema, TEntity> entityFactory,
        Func<IServiceProvider, string?>? changedBy = null)
        where TEntity : DynamicEntity
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entityFactory);

        return services.AddScoped<IEntityRepository<TEntity>>(provider =>
        {
            var actor = changedBy?.Invoke(provider);
            return new NQuadEntityRepository<TEntity>(
                provider.GetRequiredService<INQuadStore>(),
                schema,
                baseIri,
                typeIri,
                graph,
                entityFactory,
                changedBy: string.IsNullOrWhiteSpace(actor) ? SystemActor : actor);
        });
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

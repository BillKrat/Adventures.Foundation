using Adventures.Data;

namespace Adventures.Identity.Tests;

/// <summary>
/// Hand-rolled fake <see cref="IEntityRepository"/> for interaction-testing <see cref="UserAccountService"/>
/// without a real Postgres-backed implementation. Only <see cref="FindByStandardFieldAsync"/> is
/// exercised by these tests; everything else throws if accidentally called.
/// </summary>
internal sealed class FakeEntityRepository : IEntityRepository
{
    public Entity? EntityToReturn { get; set; }
    public List<(string Tenant, string EntityType, string FieldName, string FieldValue)> FindCalls { get; } = [];

    public Task<Entity?> FindByStandardFieldAsync(string tenant, string entityType, string fieldName, string fieldValue, CancellationToken cancellationToken = default)
    {
        FindCalls.Add((tenant, entityType, fieldName, fieldValue));
        return Task.FromResult(EntityToReturn);
    }

    public Task<Entity> CreateAsync(Entity entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Entity?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(Entity entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> ListAsync(EntityQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

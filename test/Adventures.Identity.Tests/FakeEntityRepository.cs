// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data;

namespace Adventures.Identity.Tests;

/// <summary>
/// Hand-rolled fake <see cref="IEntityRecordRepository"/> for interaction-testing <see cref="UserAccountService"/>
/// without a real Postgres-backed implementation. Only <see cref="FindByStandardFieldAsync"/> is
/// exercised by these tests; everything else throws if accidentally called.
/// </summary>
internal sealed class FakeEntityRepository : IEntityRecordRepository
{
    public Entity? EntityToReturn { get; set; }
    public List<(string Tenant, string EntityType, string FieldName, string FieldValue)> FindCalls { get; } = [];

    public Task<Entity?> FindByStandardFieldAsync(string tenant, string entityType, string fieldName, string fieldValue, CancellationToken cancellationToken = default)
    {
        FindCalls.Add((tenant, entityType, fieldName, fieldValue));
        return Task.FromResult(EntityToReturn);
    }

    public List<Entity> CreateCalls { get; } = [];

    public Task<Entity> CreateAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        CreateCalls.Add(entity);
        return Task.FromResult(entity);
    }

    public List<Entity> UpdateCalls { get; } = [];

    public Task<Entity?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(EntityToReturn);

    public Task<bool> UpdateAsync(Entity entity, CancellationToken cancellationToken = default)
    {
        UpdateCalls.Add(entity);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Entity>> ListAsync(EntityQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

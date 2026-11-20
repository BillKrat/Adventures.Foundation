// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>Test double for the relation writer: returns what the test set, and remembers the last call.</summary>
internal sealed class FakeRelationWriter : IEntityRelationWriter
{
    public EntityDataModel? AddResult { get; set; } = new("child-1", new Dictionary<string, string?>());

    public bool RemoveResult { get; set; } = true;

    public Exception? Throw { get; set; }

    public (string ParentId, string Relation, EntityDataModel Data)? LastAdd { get; private set; }

    public (string ParentId, string Relation, string ChildId)? LastRemove { get; private set; }

    public Task<EntityDataModel?> AddAsync(EntitySchema parentSchema, string parentId, string relationName, EntityDataModel data, string? actingUserName = null, CancellationToken cancellationToken = default)
    {
        LastAdd = (parentId, relationName, data);
        return Throw is null ? Task.FromResult(AddResult) : throw Throw;
    }

    public Task<bool> RemoveAsync(EntitySchema parentSchema, string parentId, string relationName, string childId, string? actingUserName = null, CancellationToken cancellationToken = default)
    {
        LastRemove = (parentId, relationName, childId);
        return Throw is null ? Task.FromResult(RemoveResult) : throw Throw;
    }
}

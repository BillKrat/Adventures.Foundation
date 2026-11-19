// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary>
/// Adds and removes the children of a relation in the N-Quad store. A child is created or tombstoned through its own
/// repository (so its lifecycle is the ordinary one) and the link between parent and child is one quad:
/// forward relations write it on the parent (the parent points at the child), reverse relations need no extra quad because
/// the child holds the pointer in its own field. Removing a child tombstones it; the link quad stays but a tombstoned child
/// is never listed, so the relation no longer shows it.
/// </summary>
public sealed class NQuadRelationWriter(
    INQuadStore store,
    IEnumerable<EntityTypeRegistration> types,
    IEnumerable<IRelationChangeGuard> guards,
    IGraphScope? scope = null) : IEntityRelationWriter
{
    private readonly Dictionary<string, EntityTypeRegistration> _bySchema =
        types.GroupBy(t => t.Schema.SchemaIri, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public async Task<EntityDataModel?> AddAsync(
        EntitySchema parentSchema, string parentId, string relationName, EntityDataModel data, string? actingUserName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentSchema);
        ArgumentNullException.ThrowIfNull(data);
        var (relation, parent, child) = Resolve(parentSchema, relationName);
        var parentRepository = Repository(parent);
        if (await parentRepository.GetAsync(parentId, cancellationToken: cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>(EntityInputValidator.Validate(child.Schema, data, isCreate: true), StringComparer.OrdinalIgnoreCase);
        foreach (var field in child.Schema.Fields.Values)
        {
            var target = EntityFieldSchema.RefSchemaOf(field.Type);
            if (target is null || !data.Values.TryGetValue(field.Name, out var value) || string.IsNullOrEmpty(value) || !_bySchema.TryGetValue(target, out var targetType))
            {
                continue;
            }

            if (await Repository(targetType).GetAsync(value, cancellationToken: cancellationToken).ConfigureAwait(false) is null)
            {
                errors[field.Name] = [$"{EntityFieldSchema.Humanize(field.Name)} does not exist."];
            }
        }

        if (errors.Count > 0)
        {
            throw new InputValidationException(errors);
        }

        var values = child.Schema.Fields.Keys
            .Where(name => !name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(name => name, name => data.Values.TryGetValue(name, out var v) ? v : null, StringComparer.OrdinalIgnoreCase);

        var existing = await ChildrenAsync(relation, parent, child, parentId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(entity => values.All(pair => string.Equals(SchemaTypeConverter.Format(entity.GetValue(pair.Key)), pair.Value ?? string.Empty, StringComparison.Ordinal))))
        {
            throw new BusinessRuleException("This entry already exists.");
        }

        var change = new RelationChange(parentSchema, parentId, relation, values, ChildId: null, actingUserName);
        foreach (var guard in guards)
        {
            await guard.CheckAddAsync(change, cancellationToken).ConfigureAwait(false);
        }

        var childId = Guid.NewGuid().ToString();
        var entity = new DynamicRecord(child.Schema);
        entity.Set(DynamicEntity.EntityIdPropertyName, childId, childId);
        foreach (var (name, value) in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                entity.Set(name, Guid.NewGuid().ToString(), value);
            }
        }

        var created = await Repository(child).CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        if (!relation.Reverse)
        {
            await store.InsertAsync(
                new NQuad(Guid.NewGuid(), parent.BaseIri + parentId, relation.ViaPredicate, child.BaseIri + childId,
                    child.TenantScoped ? GraphScopes.GraphFor(child.Graph, scope!.TenantKey) : child.Graph),
                cancellationToken).ConfigureAwait(false);
        }

        return EntityFormModel.From(child.Schema, created).Entity;
    }

    public async Task<bool> RemoveAsync(
        EntitySchema parentSchema, string parentId, string relationName, string childId, string? actingUserName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentSchema);
        var (relation, parent, child) = Resolve(parentSchema, relationName);
        var existing = (await ChildrenAsync(relation, parent, child, parentId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(entity => entity.EntityId?.Value == childId);
        if (existing is null)
        {
            return false;
        }

        var values = child.Schema.Fields.Keys
            .Where(name => !name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(name => name, name => SchemaTypeConverter.Format(existing.GetValue(name)), StringComparer.OrdinalIgnoreCase);
        var change = new RelationChange(parentSchema, parentId, relation, values, childId, actingUserName);
        foreach (var guard in guards)
        {
            await guard.CheckRemoveAsync(change, cancellationToken).ConfigureAwait(false);
        }

        return await Repository(child).DeleteAsync(childId, cancellationToken).ConfigureAwait(false);
    }

    private (EntityRelationDefinition Relation, EntityTypeRegistration Parent, EntityTypeRegistration Child) Resolve(EntitySchema parentSchema, string relationName)
    {
        var relation = parentSchema.Relations.FirstOrDefault(r => r.Name.Equals(relationName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{parentSchema.SchemaIri}' declares no relation '{relationName}'.");
        if (!_bySchema.TryGetValue(parentSchema.SchemaIri, out var parent) || !_bySchema.TryGetValue(relation.ChildSchemaIri, out var child))
        {
            throw new InvalidOperationException($"The entity types of relation '{relation.Name}' are not registered.");
        }

        return (relation, parent, child);
    }

    private NQuadEntityRepository<DynamicRecord> Repository(EntityTypeRegistration type) => type.CreateRepository(store, scope);

    /// <summary>The current children of one parent (the same indexed lookup the provider uses), tombstoned ones excluded.</summary>
    private async Task<IReadOnlyList<DynamicRecord>> ChildrenAsync(
        EntityRelationDefinition relation, EntityTypeRegistration parent, EntityTypeRegistration child, string parentId, CancellationToken cancellationToken)
    {
        var parentSubject = parent.BaseIri + parentId;
        var links = relation.Reverse
            ? (await store.QueryAsync(predicate: relation.ViaPredicate, @object: parentSubject, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(q => q.Subject)
            : (await store.QueryAsync(subject: parentSubject, predicate: relation.ViaPredicate, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(q => q.Object);
        var ids = links
            .Where(iri => iri.StartsWith(child.BaseIri, StringComparison.Ordinal))
            .Select(iri => iri[child.BaseIri.Length..])
            .ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return [];
        }

        return (await Repository(child).ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(entity => entity.EntityId is { } id && ids.Contains(id.Value))
            .ToList();
    }
}

public static class NQuadRelationWriterServiceCollectionExtensions
{
    /// <summary>Registers the relation writer (the rules over what it may change are registered by the identity layer: AddIdentityRelationGuards) (on top of <see cref="NQuadRelationServiceCollectionExtensions.AddNQuadRelations"/>).</summary>
    public static IServiceCollection AddNQuadRelationWriter(this IServiceCollection services)
    {
        return services.AddScoped<IEntityRelationWriter, NQuadRelationWriter>();
    }
}

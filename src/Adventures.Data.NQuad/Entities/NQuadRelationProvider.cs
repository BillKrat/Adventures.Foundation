// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary>Where one entity type lives in the store: its schema, the IRI its ids hang under, its type IRI and graph. One is registered per entity type by <see cref="NQuadEntityServiceCollectionExtensions.AddNQuadEntityRepository{TEntity}"/>.</summary>
public sealed record EntityTypeRegistration(EntitySchema Schema, string BaseIri, string TypeIri, string Graph);

/// <summary>
/// Reads the child collections a schema declares, straight from the N-Quad store: one indexed lookup for the links
/// (forward from the parent, or reverse back to it), then the children and, for pointer fields, the entities they point at
/// so a grid shows a name rather than an id. It needs no class per entity type: it reads every schema through
/// <see cref="DynamicRecord"/> using the registrations the host already made.
/// </summary>
public sealed class NQuadRelationProvider(INQuadStore store, IEnumerable<EntityTypeRegistration> types, IEnumerable<IRelationNameSource>? nameSources = null) : IEntityRelationProvider
{
    private readonly Dictionary<string, IRelationNameSource> _sources =
        (nameSources ?? []).GroupBy(s => s.SchemaIri, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    private readonly Dictionary<string, EntityTypeRegistration> _bySchema =
        types.GroupBy(t => t.Schema.SchemaIri, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public async Task<IReadOnlyList<EntityRelationModel>> GetRelationsAsync(
        EntitySchema parentSchema, string parentEntityId, bool editable = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentSchema);
        var result = new List<EntityRelationModel>();
        if (!_bySchema.TryGetValue(parentSchema.SchemaIri, out var parent))
        {
            return result;
        }

        var parentSubject = parent.BaseIri + parentEntityId;
        foreach (var relation in parentSchema.Relations)
        {
            if (!_bySchema.TryGetValue(relation.ChildSchemaIri, out var child))
            {
                continue;
            }

            var links = relation.Reverse
                ? (await store.QueryAsync(predicate: relation.ViaPredicate, @object: parentSubject, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(q => q.Subject)
                : (await store.QueryAsync(subject: parentSubject, predicate: relation.ViaPredicate, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(q => q.Object);
            var childIds = links
                .Where(iri => iri.StartsWith(child.BaseIri, StringComparison.Ordinal))
                .Select(iri => iri[child.BaseIri.Length..])
                .ToHashSet(StringComparer.Ordinal);

            var repository = new NQuadEntityRepository<DynamicRecord>(
                store, child.Schema, child.BaseIri, child.TypeIri, child.Graph, schema => new DynamicRecord(schema));
            var entities = childIds.Count == 0
                ? []
                : (await repository.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                    .Where(entity => entity.EntityId is { } id && childIds.Contains(id.Value))
                    .ToList();

            var grid = EntityGridModel.From(
                child.Schema, entities, readOnlyFields: null,
                hiddenColumns: new HashSet<string>([DynamicEntity.EntityIdPropertyName], StringComparer.OrdinalIgnoreCase));
            grid = grid with
            {
                Rows = await AddDisplayAsync(child.Schema, grid.Rows, cancellationToken).ConfigureAwait(false),
                CanCreate = editable,
                RowPermissions = new EntityPermissions(CanUpdate: false, CanDelete: editable),
            };
            result.Add(new EntityRelationModel(relation.Name, relation.Label, grid));
        }

        return result;
    }

    public Task<IReadOnlyList<EntityDataModel>> ResolveDisplayAsync(EntitySchema schema, IReadOnlyList<EntityDataModel> rows, CancellationToken cancellationToken = default) =>
        AddDisplayAsync(schema, rows, cancellationToken);

    public async Task<IReadOnlyList<RelationOption>> GetOptionsAsync(string schemaIri, string? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (!_bySchema.ContainsKey(schemaIri))
        {
            return [];
        }

        return (await LoadNamesAsync(schemaIri, depth: 2, cancellationToken).ConfigureAwait(false))
            .Where(pair => excludeId is null || !string.Equals(pair.Key, excludeId, StringComparison.Ordinal))
            .Select(pair => new RelationOption(pair.Key, pair.Value))
            .OrderBy(option => option.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>For each pointer field of <paramref name="schema"/>, the text of the entity it points at (its Name, else its first text field).</summary>
    private async Task<IReadOnlyList<EntityDataModel>> AddDisplayAsync(
        EntitySchema schema, IReadOnlyList<EntityDataModel> rows, CancellationToken cancellationToken)
    {
        var refFields = schema.Fields.Values
            .Select(field => (Field: field, Target: EntityFieldSchema.RefSchemaOf(field.Type)))
            .Where(pair => pair.Target is not null && _bySchema.ContainsKey(pair.Target))
            .ToList();
        if (refFields.Count == 0 || rows.Count == 0)
        {
            return rows;
        }

        var names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var target in refFields.Select(pair => pair.Target!).Distinct(StringComparer.Ordinal))
        {
            names[target] = await LoadNamesAsync(target, depth: 2, cancellationToken).ConfigureAwait(false);
        }

        return rows.Select(row =>
        {
            var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (field, target) in refFields)
            {
                if (row.Values.TryGetValue(field.Name, out var raw) && !string.IsNullOrEmpty(raw))
                {
                    // one id, or a comma-separated list for a refs: field; unknown ids show as themselves
                    var shown = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(id => names[target!].TryGetValue(id, out var text) ? text : id);
                    display[field.Name] = string.Join(", ", shown);
                }
            }

            return display.Count == 0 ? row : row with { Display = display };
        }).ToList();
    }

    /// <summary>Entity id to display text for every entity of <paramref name="schemaIri"/>; when the naming field is itself a pointer, the text of what it points at (one more level per <paramref name="depth"/>).</summary>
    private async Task<Dictionary<string, string>> LoadNamesAsync(string schemaIri, int depth, CancellationToken cancellationToken)
    {
        if (_sources.TryGetValue(schemaIri, out var source))
        {
            return new Dictionary<string, string>(await source.NamesAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        }

        var registration = _bySchema[schemaIri];
        var repository = new NQuadEntityRepository<DynamicRecord>(
            store, registration.Schema, registration.BaseIri, registration.TypeIri, registration.Graph, s => new DynamicRecord(s));
        var displayField = DisplayFieldOf(registration.Schema);
        var pointsAt = registration.Schema.Fields.TryGetValue(displayField, out var field) ? EntityFieldSchema.RefSchemaOf(field.Type) : null;
        var inner = pointsAt is not null && depth > 0 && _bySchema.ContainsKey(pointsAt)
            ? await LoadNamesAsync(pointsAt, depth - 1, cancellationToken).ConfigureAwait(false)
            : null;

        return (await repository.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(entity => entity.EntityId is not null)
            .ToDictionary(
                entity => entity.EntityId!.Value,
                entity =>
                {
                    var raw = SchemaTypeConverter.Format(entity.GetValue(displayField));
                    return raw is not null && inner is not null && inner.TryGetValue(raw, out var text) ? text : raw ?? entity.EntityId!.Value;
                },
                StringComparer.Ordinal);
    }

    /// <summary>The field that names an entity: the schema declared one, else "Name", "DisplayName", "UserName", else the first non-id text field.</summary>
    private static string DisplayFieldOf(EntitySchema schema)
    {
        if (schema.DisplayField is { } declared && schema.Fields.ContainsKey(declared))
        {
            return declared;
        }

        foreach (var candidate in new[] { "Name", "DisplayName", "UserName" })
        {
            if (schema.Fields.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return schema.Fields.Values
            .FirstOrDefault(field => !field.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase)
                && field.Type.Equals("String", StringComparison.OrdinalIgnoreCase))?.Name
            ?? DynamicEntity.EntityIdPropertyName;
    }
}

public static class NQuadRelationServiceCollectionExtensions
{
    /// <summary>Registers the N-Quad <see cref="IEntityRelationProvider"/>, so presenters show the child collections their schemas declare.</summary>
    public static IServiceCollection AddNQuadRelations(this IServiceCollection services) =>
        services.AddScoped<IEntityRelationProvider, NQuadRelationProvider>();
}

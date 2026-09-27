using System.Globalization;
using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>
/// The N-Quad-backed <see cref="IEntityRepository{TEntity}"/>: CRUDL over any <see cref="DynamicEntity"/>-derived
/// type described by an <see cref="EntitySchema"/>, generic enough that a schema with no dedicated entity class
/// (the POC's <c>GenericEntity</c> pattern) is served exactly the same way a hand-written one (e.g. <c>User</c>)
/// is - only the schema, base/type IRIs, and entity factory passed to the constructor differ. Never adds a
/// Delete/Update-by-id to <see cref="INQuadStore"/>: delete tombstones the subject, update supersedes the changed
/// field's prior quad(s) and inserts new ones, so the store stays insert/query-only.
/// </summary>
public sealed class NQuadEntityRepository<TEntity> : IEntityRepository<TEntity>
    where TEntity : DynamicEntity
{
    private const string RdfTypePredicate = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private readonly INQuadStore _store;
    private readonly EntitySchema _schema;
    private readonly string _baseIri;
    private readonly string _typeIri;
    private readonly string _graph;
    private readonly Func<EntitySchema, TEntity> _entityFactory;
    private readonly Func<Guid> _quadIdFactory;
    private readonly string _changedBy;
    private readonly Func<DateTimeOffset> _clock;

    public NQuadEntityRepository(
        INQuadStore store,
        EntitySchema schema,
        string baseIri,
        string typeIri,
        string graph,
        Func<EntitySchema, TEntity> entityFactory,
        Func<Guid>? quadIdFactory = null,
        string changedBy = "system",
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _baseIri = string.IsNullOrWhiteSpace(baseIri)
            ? throw new ArgumentException("A base IRI is required.", nameof(baseIri))
            : baseIri;
        _typeIri = string.IsNullOrWhiteSpace(typeIri)
            ? throw new ArgumentException("A type IRI is required.", nameof(typeIri))
            : typeIri;
        _graph = string.IsNullOrWhiteSpace(graph)
            ? throw new ArgumentException("A graph IRI is required.", nameof(graph))
            : graph;
        _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));
        _quadIdFactory = quadIdFactory ?? Guid.NewGuid;
        _changedBy = string.IsNullOrWhiteSpace(changedBy)
            ? throw new ArgumentException("An audit actor is required.", nameof(changedBy))
            : changedBy;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static string IdPropertyName => DynamicEntity.EntityIdPropertyName;

    public async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var entityId = RequireEntityId(entity);

        if (await GetAsync(entityId, includeDeleted: true, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"An entity with id '{entityId}' already exists.");
        }

        var subject = ToSubject(entityId);
        var quads = new List<NQuad> { NewQuad(subject, RdfTypePredicate, _typeIri) };
        var auditQuads = new List<NQuad>();
        var changedAt = _clock();

        foreach (var field in _schema.Fields.Values)
        {
            if (field.Name.Equals(IdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = entity.GetFieldValues(field.Name);
            if (values.Count == 0)
            {
                continue;
            }

            var persisted = RebuildFieldWithRealQuadIds(entity, subject, field, values, quads);

            auditQuads.AddRange(BuildAuditQuads(
                field, entityId, "Create", changedAt, fromValue: null, toValue: FormatValue(persisted[0].Value)));
        }

        await _store.InsertManyAsync(quads, cancellationToken).ConfigureAwait(false);
        await InsertIfAnyAsync(auditQuads, cancellationToken).ConfigureAwait(false);

        entity.AcceptChanges();
        return entity;
    }

    /// <summary>
    /// A caller building a new/changed <typeparamref name="TEntity"/> has no way to know the quad id a value will
    /// get once persisted - <see cref="DynamicEntity.Set"/>/<see cref="DynamicEntity.Add"/> require *some* id, so
    /// it supplies a placeholder. Once this repository has generated the real ids and queued the real quads, the
    /// entity's own values are rebuilt to carry those same ids, so a later <see cref="UpdateAsync"/> on this exact
    /// instance supersedes the quad that is actually in the store, not the caller's placeholder.
    /// </summary>
    private List<FieldValue> RebuildFieldWithRealQuadIds(
        TEntity entity,
        string subject,
        EntitySchemaField field,
        IReadOnlyList<FieldValue> values,
        List<NQuad> quads)
    {
        var persisted = new List<FieldValue>(values.Count);
        foreach (var value in values)
        {
            var quadId = _quadIdFactory();
            quads.Add(new NQuad(quadId, subject, field.Predicate, ApplyPrefix(field, FormatValue(value.Value)), _graph));
            persisted.Add(new FieldValue(new EntityId(quadId.ToString()), value.Value));
        }

        entity.Set(field.Name, persisted[0].Id.Value, persisted[0].Value);
        foreach (var value in persisted.Skip(1))
        {
            entity.Add(field.Name, value.Id.Value, value.Value);
        }

        return persisted;
    }

    public async Task<TEntity?> GetAsync(string entityId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        ValidateEntityId(entityId);
        var subject = ToSubject(entityId);
        var quads = await _store.QueryAsync(subject: subject, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (quads.Count == 0)
        {
            return null;
        }

        if (!includeDeleted && quads.Any(q => q.Predicate == EntityConstants.Lifecycle.DeletedAtPredicate))
        {
            return null;
        }

        return await MaterializeAsync(entityId, subject, quads, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var typeQuads = await _store.QueryAsync(predicate: RdfTypePredicate, @object: _typeIri, cancellationToken: cancellationToken).ConfigureAwait(false);
        var entityIds = typeQuads
            .Where(q => q.Subject.StartsWith(_baseIri, StringComparison.Ordinal))
            .Select(q => q.Subject[_baseIri.Length..])
            .Distinct(StringComparer.Ordinal);

        var results = new List<TEntity>();
        foreach (var entityId in entityIds)
        {
            var entity = await GetAsync(entityId, includeDeleted, cancellationToken).ConfigureAwait(false);
            if (entity is not null)
            {
                results.Add(entity);
            }
        }

        return results;
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var entityId = RequireEntityId(entity);

        if (await GetAsync(entityId, includeDeleted: false, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new KeyNotFoundException($"The entity with id '{entityId}' does not exist or is deleted.");
        }

        var dirtyFieldIds = entity.DirtyFieldIds;
        if (dirtyFieldIds.Count == 0)
        {
            return;
        }

        var subject = ToSubject(entityId);
        var quads = new List<NQuad>();
        var auditQuads = new List<NQuad>();
        var changedAt = _clock();
        var supersededAt = changedAt.ToString("O", CultureInfo.InvariantCulture);

        foreach (var field in _schema.Fields.Values.Where(f => dirtyFieldIds.Contains(f.Id)))
        {
            // Every prior quad for this field is superseded, not just the first - a multi-valued field (e.g. a
            // User with two Email values) replaced by Set() would otherwise leave its second quad looking live.
            var originalValues = entity.GetOriginalFieldValues(field.Name);
            foreach (var original in originalValues)
            {
                quads.Add(NewQuad(original.Id.Value, EntityConstants.Lifecycle.SupersededAtPredicate, supersededAt));
            }

            var newValues = entity.GetFieldValues(field.Name);
            var persisted = newValues.Count == 0
                ? []
                : RebuildFieldWithRealQuadIds(entity, subject, field, newValues, quads);

            // The audit log records one from/to pair per field (matching the seeded AuditRecord schema, one row
            // per field, not per value) - full per-value fidelity for a multi-valued field's audit trail is not
            // needed yet; the supersede mechanics above (which is what "current" reads depend on) are already
            // fully correct for every value, regardless of this simplification.
            auditQuads.AddRange(BuildAuditQuads(
                field,
                entityId,
                "Update",
                changedAt,
                fromValue: originalValues.Count > 0 ? FormatValue(originalValues[0].Value) : null,
                toValue: persisted.Count > 0 ? FormatValue(persisted[0].Value) : null));
        }

        await _store.InsertManyAsync(quads, cancellationToken).ConfigureAwait(false);
        await InsertIfAnyAsync(auditQuads, cancellationToken).ConfigureAwait(false);

        entity.AcceptChanges();
    }

    public async Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(entityId, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        var subject = ToSubject(entityId);
        var changedAt = _clock();
        var quads = new List<NQuad>
        {
            NewQuad(subject, EntityConstants.Lifecycle.DeletedAtPredicate, changedAt.ToString("O", CultureInfo.InvariantCulture)),
        };

        var auditQuads = new List<NQuad>();
        foreach (var field in _schema.Fields.Values)
        {
            if (field.Name.Equals(IdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var current = entity.GetFieldValue(field.Name);
            if (current is not null)
            {
                auditQuads.AddRange(BuildAuditQuads(field, entityId, "Delete", changedAt, fromValue: FormatValue(current.Value), toValue: null));
            }
        }

        await _store.InsertManyAsync(quads, cancellationToken).ConfigureAwait(false);
        await InsertIfAnyAsync(auditQuads, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private async Task<TEntity> MaterializeAsync(string entityId, string subject, IReadOnlyList<NQuad> subjectQuads, CancellationToken cancellationToken)
    {
        // The current value(s) for (subject, predicate) are whichever of these quads have not been superseded.
        // Queried broadly (every SupersededAt marker in the store) rather than per-candidate - simple and correct
        // for now; if the marker volume ever matters, narrowing this is a store-level optimization, not a
        // behavior change.
        var supersededMarkers = await _store.QueryAsync(predicate: EntityConstants.Lifecycle.SupersededAtPredicate, cancellationToken: cancellationToken).ConfigureAwait(false);
        var supersededIds = supersededMarkers.Select(m => m.Subject).ToHashSet(StringComparer.Ordinal);

        var entity = _entityFactory(_schema);
        entity.Set(IdPropertyName, subject, entityId);

        foreach (var quad in subjectQuads)
        {
            if (quad.Predicate == RdfTypePredicate || quad.Predicate == EntityConstants.Lifecycle.DeletedAtPredicate)
            {
                continue;
            }

            if (supersededIds.Contains(quad.Id.ToString()))
            {
                continue;
            }

            if (!_schema.FieldsByPredicate.TryGetValue(quad.Predicate, out var field) ||
                field.Name.Equals(IdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entity.Add(field.Name, quad.Id.ToString(), StripPrefix(field, quad.Object));
        }

        entity.AcceptChanges();
        return entity;
    }

    private static string RequireEntityId(TEntity entity) =>
        entity.GetValue(IdPropertyName)?.ToString()
            ?? throw new ArgumentException($"{IdPropertyName} is required.", nameof(entity));

    private static void ValidateEntityId(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity id is required.", nameof(entityId));
        }
    }

    private string ToSubject(string entityId) => _baseIri + entityId;

    private NQuad NewQuad(string subject, string predicate, string @object) =>
        new(_quadIdFactory(), subject, predicate, @object, _graph);

    private Task InsertIfAnyAsync(IReadOnlyCollection<NQuad> quads, CancellationToken cancellationToken) =>
        quads.Count == 0 ? Task.CompletedTask : _store.InsertManyAsync(quads, cancellationToken);

    private IEnumerable<NQuad> BuildAuditQuads(
        EntitySchemaField field,
        string entityId,
        string action,
        DateTimeOffset changedAt,
        string? fromValue,
        string? toValue)
    {
        var subject = EntityConstants.AuditRecord.BaseIri + Guid.NewGuid();
        yield return NewQuad(subject, RdfTypePredicate, EntityConstants.AuditRecord.TypeIri);
        yield return NewQuad(subject, EntityConstants.AuditRecord.EntityTypePredicate, field.EntityType);
        yield return NewQuad(subject, EntityConstants.AuditRecord.EntityIdPredicate, entityId);
        yield return NewQuad(subject, EntityConstants.AuditRecord.FieldIdPredicate, field.Id);
        yield return NewQuad(subject, EntityConstants.AuditRecord.FieldNamePredicate, field.Name);
        yield return NewQuad(subject, EntityConstants.AuditRecord.ActionPredicate, action);
        yield return NewQuad(subject, EntityConstants.AuditRecord.ChangedAtPredicate, changedAt.ToString("O", CultureInfo.InvariantCulture));
        yield return NewQuad(subject, EntityConstants.AuditRecord.ChangedByPredicate, _changedBy);
        if (fromValue is not null)
        {
            yield return NewQuad(subject, EntityConstants.AuditRecord.FromValuePredicate, fromValue);
        }

        if (toValue is not null)
        {
            yield return NewQuad(subject, EntityConstants.AuditRecord.ToValuePredicate, toValue);
        }
    }

    private static string ApplyPrefix(EntitySchemaField field, string rawValue) =>
        field.ValuePrefix is not null && !rawValue.StartsWith(field.ValuePrefix, StringComparison.OrdinalIgnoreCase)
            ? field.ValuePrefix + rawValue
            : rawValue;

    private static string StripPrefix(EntitySchemaField field, string rawValue) =>
        field.ValuePrefix is not null && rawValue.StartsWith(field.ValuePrefix, StringComparison.OrdinalIgnoreCase)
            ? rawValue[field.ValuePrefix.Length..]
            : rawValue;

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        string text => text,
        _ => value.ToString() ?? throw new ArgumentException("A value must have a string representation."),
    };
}

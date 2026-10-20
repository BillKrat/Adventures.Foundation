namespace Adventures.Entities;

/// <summary>
/// Implementation of <see cref="IEntityPresenter{TEntity}"/> - see that interface for the contract and rationale.
/// One generic presenter serves every entity type, a deliberate deviation from MVPVM's "presenters are not reused"
/// (docs/Claude-architecture-principles.md A7): the screens are generated from schemas, so there is no per-screen
/// behaviour to write. An entity with real behaviour derives from this (see <see cref="UserPresenter"/>) instead of
/// branching here. Reviewed and accepted 2026-10-11 (finding F-10).
/// </summary>
public class EntityPresenter<TEntity>(
    IEntityBll<TEntity> bll,
    EntitySchema schema,
    Func<EntitySchema, TEntity> entityFactory,
    IEntityRelationProvider? relations = null) : IEntityPresenter<TEntity>
    where TEntity : DynamicEntity
{
    protected IEntityBll<TEntity> Bll { get; } = bll ?? throw new ArgumentNullException(nameof(bll));

    protected EntitySchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

    protected Func<EntitySchema, TEntity> EntityFactory { get; } = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));

    /// <summary>Field names this presenter makes read-only on top of what the schema declares (for example a login name that must not change). The server refuses a change to them on update; the client only renders them as read-only.</summary>
    protected virtual IReadOnlySet<string> ReadOnlyFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fields only the server sets, through its own actions (a post publish state): a client may not send a different value on create or update, so the generic form cannot do what an action is guarded for.</summary>
    protected virtual IReadOnlySet<string> ServerManagedFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the caller may do with this entity (for rendering buttons; the Bll and the controller still enforce). Full by default.</summary>
    protected virtual EntityPermissions PermissionsFor(TEntity entity) => EntityPermissions.Full;

    /// <summary>Fields a grid does not show as a column by default (the client gear can still add them). The entity id is never a useful column.</summary>
    protected virtual IReadOnlySet<string> ListHiddenFields { get; } = new HashSet<string>([DynamicEntity.EntityIdPropertyName], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the caller may add a new entity from the grid (rendering only; the controller and Bll enforce).</summary>
    protected virtual bool CanCreate => true;

    /// <summary>What the caller may do with a row, for the grid buttons (rendering only).</summary>
    protected virtual EntityPermissions RowPermissions => EntityPermissions.Full;

    public async Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await Bll.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : await WithRelationsAsync(EntityFormModel.From(Schema, entity, PermissionsFor(entity), ReadOnlyFields), PermissionsFor(entity).CanUpdate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds the child collections the schema declares (when this presenter has a provider and the schema declares any).</summary>
    protected async Task<EntityFormModel> WithRelationsAsync(EntityFormModel form, bool editable, CancellationToken cancellationToken)
    {
        if (relations is null)
        {
            return form;
        }

        var shown = (await relations.ResolveDisplayAsync(Schema, [form.Entity], cancellationToken).ConfigureAwait(false))[0];
        form = form with { Entity = shown };
        if (Schema.Relations.Count == 0)
        {
            return form;
        }

        var models = await relations.GetRelationsAsync(Schema, form.Entity.EntityId, editable, cancellationToken).ConfigureAwait(false);
        return form with { Relations = models };
    }

    public virtual Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CanCreate ? EntityFormModel.Blank(Schema, EntityPermissions.Full, ReadOnlyFields) : null);

    public async Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Bll.ListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(entity => EntityFormModel.From(Schema, entity).Entity).ToArray();
    }

    public virtual async Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await Bll.ListAsync(cancellationToken).ConfigureAwait(false);
        var (page, paging) = EntityGridPager.Apply(Schema, all, query);
        var grid = EntityGridModel.From(Schema, page, ReadOnlyFields, ListHiddenFields);
        if (relations is not null)
        {
            grid = grid with { Rows = await relations.ResolveDisplayAsync(Schema, grid.Rows, cancellationToken).ConfigureAwait(false) };
        }

        return grid with { Paging = paging, CanCreate = CanCreate, RowPermissions = RowPermissions };
    }

    public virtual async Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default) =>
        EntityGridModel.From(Schema, await Bll.ListAsync(cancellationToken).ConfigureAwait(false), ReadOnlyFields, ListHiddenFields);

    public async Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        EntityInputValidator.ThrowIfInvalid(Schema, data, isCreate: true);
        RejectServerManaged(data, isCreate: true);
        await ValidatePointersAsync(data, cancellationToken).ConfigureAwait(false);

        var newId = Guid.NewGuid().ToString();
        var entity = ApplyValues(EntityFactory(Schema), data, newId);
        var created = await Bll.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        return EntityFormModel.From(Schema, created, PermissionsFor(created), ReadOnlyFields);
    }

    public async Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        EntityInputValidator.ThrowIfInvalid(Schema, data, isCreate: false);
        RejectServerManaged(data, isCreate: false);
        await ValidatePointersAsync(data, cancellationToken).ConfigureAwait(false);

        var existing = await Bll.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        RejectReadOnlyChanges(existing, data);
        ApplyValues(existing, data, entityId: null);
        await Bll.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        return EntityFormModel.From(Schema, existing, PermissionsFor(existing), ReadOnlyFields);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Bll.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Applies every value in <paramref name="data"/> except "Id" - the entity id is derived from
    /// the store subject, never a settable field, same convention <c>NQuadEntityRepository</c>
    /// itself follows. When <paramref name="entityId"/> is supplied (Create), sets it first. The input was already
    /// checked by <see cref="EntityInputValidator"/>. Rules, chosen to fit a browser form that is pre-filled from the
    /// server and sends every field back:
    /// <list type="bullet">
    /// <item>null: not supplied, field untouched.</item>
    /// <item>"" (empty): if the field currently holds a value, the user cleared it, so it is cleared (and a required field
    /// then fails the Bll's required check); if it holds nothing, there is nothing to do. Either way "" is never parsed,
    /// so an unset non-string field (DateOnly, Guid, ...) cannot throw.</item>
    /// <item>a value equal to the current one: skipped, so saving an unchanged form writes nothing (no new quad, no audit
    /// row per field per save).</item>
    /// </list>
    /// </summary>
    /// <summary>A read-only field (declared by the schema or by <see cref="ReadOnlyFields"/>) may be sent back unchanged but not changed: the flag is for rendering, this is the enforcement.</summary>
    private void RejectServerManaged(EntityDataModel data, bool isCreate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ServerManagedFields)
        {
            if (!data.Values.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            var field = Schema.Fields.GetValueOrDefault(name);
            var isDefault = isCreate && field is not null && string.Equals(value, field.DefaultValue, StringComparison.OrdinalIgnoreCase);
            if (!isDefault && isCreate)
            {
                errors[name] = [$"{EntityFieldSchema.Humanize(name)} is set by the server, not by this form."];
            }
        }

        if (errors.Count > 0)
        {
            throw new Adventures.Common.Exceptions.InputValidationException(errors);
        }
    }

    private void RejectReadOnlyChanges(TEntity existing, EntityDataModel data)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Schema.Fields.Values)
        {
            var readOnly = field.IsReadOnly || ReadOnlyFields.Contains(field.Name)
                || field.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase);
            if (!readOnly || !data.Values.TryGetValue(field.Name, out var value) || value is null
                || field.Name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var unchanged = value.Length == 0 ? existing.GetValue(field.Name) is null : IsUnchanged(existing, field.Name, value);
            if (!unchanged)
            {
                errors[field.Name] = [$"{EntityFieldSchema.Humanize(field.Name)} cannot be changed."];
            }
        }

        if (errors.Count > 0)
        {
            throw new Adventures.Common.Exceptions.InputValidationException(errors);
        }
    }

    private TEntity ApplyValues(TEntity entity, EntityDataModel data, string? entityId)
    {
        if (entityId is not null)
        {
            entity.Set(DynamicEntity.EntityIdPropertyName, entityId, entityId);
        }

        foreach (var (name, value) in data.Values)
        {
            if (name.Equals(DynamicEntity.EntityIdPropertyName, StringComparison.OrdinalIgnoreCase) || value is null || ServerManagedFields.Contains(name))
            {
                continue;
            }

            if (Schema.Fields.TryGetValue(name, out var manyField) && EntityFormModel.IsMulti(manyField.Type))
            {
                ApplyMany(entity, name, value);
                continue;
            }

            if (value.Length == 0)
            {
                if (entity.GetValue(name) is not null)
                {
                    entity.Clear(name);
                }

                continue;
            }

            if (IsUnchanged(entity, name, value))
            {
                continue;
            }

            entity.Set(name, Guid.NewGuid().ToString(), value);
        }

        return entity;
    }

    /// <summary>Sets a multi-valued pointer field from a comma-separated list of ids: nothing changes when the same set is sent back, otherwise the field is replaced.</summary>
    private static void ApplyMany(TEntity entity, string name, string value)
    {
        var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        var current = entity.GetValues(name).Select(SchemaTypeConverter.Format).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (current.SetEquals(ids))
        {
            return;
        }

        entity.Clear(name);
        foreach (var id in ids)
        {
            entity.Add(name, Guid.NewGuid().ToString(), id);
        }
    }

    /// <summary>Every pointer value sent must name an entity that exists (a parent page, an author, categories); the field gets the error otherwise.</summary>
    private async Task ValidatePointersAsync(EntityDataModel data, CancellationToken cancellationToken)
    {
        if (relations is null)
        {
            return;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Schema.Fields.Values)
        {
            var target = EntityFieldSchema.RefSchemaOf(field.Type);
            if (target is null || !data.Values.TryGetValue(field.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var known = (await relations.GetOptionsAsync(target, null, cancellationToken).ConfigureAwait(false)).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            var missing = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(id => !known.Contains(id)).ToList();
            if (missing.Count > 0 && known.Count > 0)
            {
                errors[field.Name] = [$"{EntityFieldSchema.Humanize(field.Name)} refers to something that does not exist."];
            }
        }

        if (errors.Count > 0)
        {
            throw new Adventures.Common.Exceptions.InputValidationException(errors);
        }
    }

    private bool IsUnchanged(TEntity entity, string name, string raw)
    {
        var current = entity.GetValues(name);
        if (current.Count != 1 || !Schema.Fields.TryGetValue(name, out var field))
        {
            return false;
        }

        var incoming = SchemaTypeConverter.ConvertTo(raw, new SchemaTypeConverter().ResolveType(field.Type));
        return Equals(current[0], incoming);
    }
}

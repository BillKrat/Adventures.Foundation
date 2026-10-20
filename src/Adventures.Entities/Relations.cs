namespace Adventures.Entities;

/// <summary>
/// A child collection a schema declares (a User's role assignments). Declared in the schema as data, never in client code:
/// <see cref="ViaPredicate"/> is the quad predicate that links the two entities, <see cref="Reverse"/> says which end holds
/// the pointer (false: the parent points at each child, as a user points at its role assignments; true: each child points
/// back at the parent, as a comment points at its post). Both directions are one indexed lookup.
/// </summary>
public sealed record EntityRelationDefinition(string Name, string Label, string ChildSchemaIri, string ViaPredicate, bool Reverse = false);

/// <summary>One child collection of a form: its name, the label to show and the grid of its rows.</summary>
public sealed record EntityRelationModel(string Name, string Label, EntityGridModel Grid);

/// <summary>One choice for a pointer field: the id to store and the text to show.</summary>
public sealed record RelationOption(string Id, string Text);

/// <summary>
/// Reads the child collections a schema declares for one parent entity, as ready-to-render grids, and the choices for a
/// pointer field. The N-Quad implementation lives in Adventures.Data.NQuad; a presenter that has none shows no relations.
/// </summary>
public interface IEntityRelationProvider
{
    /// <summary>The grids of the schema's relations for one parent. <paramref name="editable"/> tells the renderer the caller may add and remove children (the server enforces it).</summary>
    Task<IReadOnlyList<EntityRelationModel>> GetRelationsAsync(
        EntitySchema parentSchema, string parentEntityId, bool editable = false, CancellationToken cancellationToken = default);

    /// <summary>Every entity of a schema as id and display text, for a lookup. Empty for a schema the host has not registered.</summary>
    Task<IReadOnlyList<RelationOption>> GetOptionsAsync(string schemaIri, string? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>Adds display text beside the raw ids of the pointer fields in <paramref name="rows"/> (a page parent is shown by its title), for any grid or form.</summary>
    Task<IReadOnlyList<EntityDataModel>> ResolveDisplayAsync(EntitySchema schema, IReadOnlyList<EntityDataModel> rows, CancellationToken cancellationToken = default);
}

/// <summary>What is about to change in a relation, handed to the <see cref="IRelationChangeGuard"/>s before it does.</summary>
public sealed record RelationChange(
    EntitySchema ParentSchema, string ParentId, EntityRelationDefinition Relation, IReadOnlyDictionary<string, string?> ChildValues, string? ChildId);

/// <summary>A business rule over relation changes (the last administrator cannot be removed). Throw <see cref="Adventures.Common.Exceptions.BusinessRuleException"/> to refuse.</summary>
public interface IRelationChangeGuard
{
    Task CheckAddAsync(RelationChange change, CancellationToken cancellationToken = default);

    Task CheckRemoveAsync(RelationChange change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adds and removes the children of a relation, each through the child own rules: values validated against the child schema,
/// pointers checked to exist, duplicates refused, guards consulted, then the child created (or tombstoned) and the link written.
/// A child is never saved as part of one aggregate write of its parent.
/// </summary>
public interface IEntityRelationWriter
{
    /// <summary>The created child, or null when the parent does not exist. Throws <see cref="Adventures.Common.Exceptions.InputValidationException"/> or <see cref="Adventures.Common.Exceptions.BusinessRuleException"/> for a refused change.</summary>
    Task<EntityDataModel?> AddAsync(
        EntitySchema parentSchema, string parentId, string relationName, EntityDataModel data, CancellationToken cancellationToken = default);

    /// <summary>True when the child was removed; false when it is not a child of this parent.</summary>
    Task<bool> RemoveAsync(
        EntitySchema parentSchema, string parentId, string relationName, string childId, CancellationToken cancellationToken = default);
}

/// <summary>An entity of any schema, for code that reads entities generically (the relation provider) without a class per type.</summary>
public sealed class DynamicRecord(EntitySchema schema) : DynamicEntity(schema);

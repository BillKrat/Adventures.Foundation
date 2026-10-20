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

/// <summary>
/// Reads the child collections a schema declares for one parent entity, as ready-to-render grids. The N-Quad
/// implementation lives in Adventures.Data.NQuad; a presenter that has none simply shows no relations.
/// </summary>
public interface IEntityRelationProvider
{
    Task<IReadOnlyList<EntityRelationModel>> GetRelationsAsync(EntitySchema parentSchema, string parentEntityId, CancellationToken cancellationToken = default);
}

/// <summary>An entity of any schema, for code that reads entities generically (the relation provider) without a class per type.</summary>
public sealed class DynamicRecord(EntitySchema schema) : DynamicEntity(schema);

namespace Adventures.Data;

/// <summary>
/// A W3C N-Quad style statement: a subject-predicate-object triple plus a graph name that scopes
/// the statement (this project uses the graph to encode tenant/org/user granularity, e.g.
/// <c>"tenant:acme.com/org:my-blog/user:42"</c>). Used to model fully user-defined/extension
/// fields (custom form fields, grid columns, etc.) that the application's standard JSONB schema
/// does not anticipate.
/// </summary>
/// <param name="Subject">The identifier of the entity (<see cref="Entity.Id"/>) this statement is about.</param>
/// <param name="Predicate">The name of the custom field/property (e.g. "favoriteColor").</param>
/// <param name="Object">The value of the field, as text (JSON-encoded when the datatype is complex).</param>
/// <param name="ObjectDatatype">The datatype of <see cref="Object"/> (e.g. "xsd:string", "xsd:integer", "json").</param>
/// <param name="Graph">The scoping context for this statement (tenant/org/user granularity).</param>
public sealed record Triple(
    Guid Subject,
    string Predicate,
    string Object,
    string ObjectDatatype,
    string Graph);

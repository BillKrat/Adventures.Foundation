namespace Adventures.Entities;

// What a schema browser shows: the shape of the data, never the data. These records are the wire contract between the browser API and its clients.

/// <summary>One schema in the list. <paramref name="Id"/> is its IRI; area and name are the last two parts of that address.</summary>
public sealed record SchemaSummary(string Id, string Area, string Name, int FieldCount);

/// <summary>One field. <paramref name="RefSchemaId"/> is the schema a pointer field points at, otherwise null.</summary>
public sealed record SchemaFieldDescription(string Name, string Label, string Type, string Predicate, bool IsRequired, bool IsReadOnly, bool IsListVisible, string? RefSchemaId);

/// <summary>A child collection a schema declares: the child schema and the predicate that links the two.</summary>
public sealed record SchemaRelationDescription(string Name, string Label, string ChildSchemaId, string ViaPredicate, bool Reverse);

/// <summary>A pointer field of another schema (or the same one) that points here.</summary>
public sealed record SchemaUse(string SchemaId, string SchemaName, string FieldName);

public sealed record SchemaDetail(
    string Id,
    string Area,
    string Name,
    string? DisplayField,
    IReadOnlyList<SchemaFieldDescription> Fields,
    IReadOnlyList<SchemaRelationDescription> Relations,
    IReadOnlyList<SchemaUse> UsedBy);

/// <summary>Every schema of a store, and the ids of any that could not be read (listed so a broken schema is seen, never dropped silently).</summary>
public sealed record SchemaCatalogList(IReadOnlyList<SchemaSummary> Schemas, IReadOnlyList<string> Unreadable);

/// <summary>The site that answered a schema browser request.</summary>
public sealed record SchemaBrowserContext(string? SiteName, string? SiteDomain);

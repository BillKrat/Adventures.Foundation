// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// Well-known IRIs/predicates used to describe schemas and the User entity in the N-Quad seed
/// data (mirrors the POC's PocConstants). Kept here - rather than referencing the storage-layer
/// project - so Adventures.Entities has no dependency on any specific store.
/// </summary>
public static class EntityConstants
{
    public static class Schema
    {
        public const string BaseIri = "https://global-webnet.com/ontology/schema#";
        public const string TypePredicate = BaseIri + "type";
        public const string TypeIri = BaseIri + "Schema";
        public const string FieldPredicate = BaseIri + "field";
        public const string FieldNamePredicate = BaseIri + "name";
        public const string FieldTypePredicate = BaseIri + "fieldType";
        public const string FieldRdfPredicate = BaseIri + "predicate";
        public const string FieldValuePrefixPredicate = BaseIri + "valuePrefix";

        // Optional presentation metadata of a field (all default when absent): the text to show, whether the field may be
        // changed, where it sits among the others, whether a grid shows it as a column, and whether a value is required on create.
        public const string FieldLabelPredicate = BaseIri + "label";
        public const string FieldReadOnlyPredicate = BaseIri + "readOnly";
        public const string FieldOrderPredicate = BaseIri + "order";
        public const string FieldListVisiblePredicate = BaseIri + "listVisible";
        public const string FieldRequiredPredicate = BaseIri + "required";

        /// <summary>Optional: the value a new entity form starts with (as text).</summary>
        public const string FieldDefaultPredicate = BaseIri + "default";

        // A child collection a schema declares (see EntityRelationDefinition): the schema points at a relation node, which names the
        // collection, the child schema, the linking predicate and the direction (forward or reverse).
        public const string RelationPredicate = BaseIri + "relation";
        public const string RelationNamePredicate = BaseIri + "relationName";
        public const string RelationLabelPredicate = BaseIri + "relationLabel";
        public const string RelationChildPredicate = BaseIri + "relationChild";
        public const string RelationViaPredicate = BaseIri + "relationVia";
        public const string RelationDirectionPredicate = BaseIri + "relationDirection";

        /// <summary>Optional: the field that names an entity of this schema when another entity points at it (default: Name, DisplayName, UserName, then the first text field).</summary>
        public const string DisplayFieldPredicate = BaseIri + "displayField";

        /// <summary>The field type prefixes for a pointer to another entity: <c>ref:&lt;SchemaIri&gt;</c> (one) and <c>refs:&lt;SchemaIri&gt;</c> (many). The value is the other entity id.</summary>
        public const string RefTypePrefix = "ref:";
        public const string RefsTypePrefix = "refs:";

        /// <summary>
        /// The placeholder predicate every schema's own "Id" field declares (see e.g. seed.nq's
        /// <c>schema/User/Id</c>). Never actually written to the store - <c>NQuadEntityRepository</c>
        /// always derives an entity's id from its subject IRI instead - but <see cref="DynamicEntity.Set"/>
        /// still requires "Id" to be a declared field, so every schema needs one.
        /// </summary>
        public const string IdentifierPredicate = BaseIri + "identifier";

        /// <summary>
        /// IRIs for <see cref="Adventures.Entities.SchemaEntity"/> instances - a schema definition
        /// (e.g. the seed-authored "User" schema) treated as an ordinary
        /// <c>NQuadEntityRepository{TEntity}</c>-managed entity, discovered the same way every other
        /// entity is: a standard rdf:type triple, not the <see cref="TypePredicate"/> marker
        /// <c>SchemaDal</c> reads. <see cref="EntityTypeIri"/> deliberately reuses <see cref="TypeIri"/> -
        /// same "this is a Schema" concept, just paired with the standard rdf:type predicate instead of
        /// the schema-describing one.
        /// </summary>
        public const string EntityBaseIri = "https://global-webnet.com/schema/";

        public const string EntityTypeIri = TypeIri;

        /// <summary>IRIs for <see cref="Adventures.Entities.SchemaFieldEntity"/> instances.</summary>
        public const string FieldEntityBaseIri = "https://global-webnet.com/schema-field/";

        public const string FieldEntityTypeIri = BaseIri + "Field";
    }

    /// <summary>
    /// The legacy mirror layer: one schema per ported BlogEngine.NET table, field for field (see ai-research-blog docs, the legacy table
    /// migration plan). Generated into <c>Sql/seed/seed-009-legacy-schemas.nq</c>; never edited by hand.
    /// </summary>
    public static class Legacy
    {
        public const string SchemaSeedName = "009-legacy-schemas";

        /// <summary>Seed 010 adds the Id field every legacy schema needs (seed 009 had been applied; a named seed applies once).</summary>
        public const string IdFieldsSeedName = "010-legacy-ids";

        /// <summary>Seed 012: the mirror schemas of wave 2 (be_Settings, be_CustomFields), columns and Id field in one file; generated like 009 and 010.</summary>
        public const string Wave2SeedName = "012-legacy-wave2";
        public const string DefaultGraph = "https://global-webnet.com/graph/legacy";
        public const string SchemaBase = "https://schema.global-webnet.com/legacy/";
    }

    /// <summary>
    /// Mechanical "is this still current" bookkeeping for <see cref="IEntityRepository{TEntity}"/>: a tombstoned
    /// entity or a superseded field-value quad is not physically removed. This is storage mechanics only: it records no
    /// actor, is not a tamper-evident log and is not an audit trail; the software makes no such claim (see NOTICE, Regulated use).
    /// </summary>
    public static class Lifecycle
    {
        public const string BaseIri = "https://global-webnet.com/ontology/lifecycle#";

        /// <summary>Quad: (entity subject, DeletedAtPredicate, ISO-8601 timestamp). Marks a whole entity deleted.</summary>
        public const string DeletedAtPredicate = BaseIri + "deletedAt";

        /// <summary>Quad: (superseded quad's own id as a string, SupersededAtPredicate, ISO-8601 timestamp).</summary>
        public const string SupersededAtPredicate = BaseIri + "supersededAt";
    }
}

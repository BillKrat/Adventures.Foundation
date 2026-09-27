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
        public const string UserIri = "https://global-webnet.com/schema/User";
        public const string BaseIri = "https://global-webnet.com/ontology/schema#";
        public const string TypePredicate = BaseIri + "type";
        public const string TypeIri = BaseIri + "Schema";
        public const string FieldPredicate = BaseIri + "field";
        public const string FieldNamePredicate = BaseIri + "name";
        public const string FieldTypePredicate = BaseIri + "fieldType";
        public const string FieldRdfPredicate = BaseIri + "predicate";
        public const string FieldValuePrefixPredicate = BaseIri + "valuePrefix";
    }

    public static class User
    {
        public const string BaseIri = "https://global-webnet.com/id/user/";
        public const string TypeIri = "http://xmlns.com/foaf/0.1/Person";
    }

    /// <summary>
    /// Mechanical "is this still current" bookkeeping for <see cref="IEntityRepository{TEntity}"/>: a tombstoned
    /// entity or a superseded field-value quad is never physically removed, so history is always reconstructable.
    /// Deliberately separate from <see cref="AuditRecord"/> - this is cheap, mechanical, and not meant to be read by
    /// a person; <see cref="AuditRecord"/> is the human-facing "what changed and why" log.
    /// </summary>
    public static class Lifecycle
    {
        public const string BaseIri = "https://global-webnet.com/ontology/lifecycle#";

        /// <summary>Quad: (entity subject, DeletedAtPredicate, ISO-8601 timestamp). Marks a whole entity deleted.</summary>
        public const string DeletedAtPredicate = BaseIri + "deletedAt";

        /// <summary>Quad: (superseded quad's own id as a string, SupersededAtPredicate, ISO-8601 timestamp).</summary>
        public const string SupersededAtPredicate = BaseIri + "supersededAt";
    }

    /// <summary>
    /// Same shape and predicate IRIs as the POC's <c>PocConstants.Audit</c> (and the seed data already carries this
    /// exact schema, unused, in <c>artifacts/seed.nq</c> / <c>Sql/seed/seed.nq</c>) - kept identical rather than
    /// reinvented so the two stay compatible. One row per changed field, written by
    /// <see cref="IEntityRepository{TEntity}"/> on every Create/Update/Delete.
    /// </summary>
    public static class AuditRecord
    {
        public const string SchemaIri = "https://global-webnet.com/schema/AuditRecord";
        public const string BaseIri = "https://global-webnet.com/id/audit/";
        public const string TypeIri = "https://global-webnet.com/ontology/audit#AuditRecord";
        public const string EntityTypePredicate = "https://global-webnet.com/ontology/audit#entityType";
        public const string EntityIdPredicate = "https://global-webnet.com/ontology/audit#entityId";
        public const string FieldIdPredicate = "https://global-webnet.com/ontology/audit#fieldId";
        public const string FieldNamePredicate = "https://global-webnet.com/ontology/audit#fieldName";
        public const string ActionPredicate = "https://global-webnet.com/ontology/audit#action";
        public const string ChangedAtPredicate = "https://global-webnet.com/ontology/audit#changedAt";
        public const string ChangedByPredicate = "https://global-webnet.com/ontology/audit#changedBy";
        public const string FromValuePredicate = "https://global-webnet.com/ontology/audit#fromValue";
        public const string ToValuePredicate = "https://global-webnet.com/ontology/audit#toValue";
    }
}

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
        /// (e.g. the seed-authored "User" and "AuditRecord" schemas) treated as an ordinary
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

    public static class User
    {
        public const string BaseIri = "https://global-webnet.com/id/user/";
        public const string DefaultGraph = "https://global-webnet.com/graph/user/b7963bd0-5ad3-4c83-b61c-fa622cc6a2df";
        public const string TypeIri = "http://xmlns.com/foaf/0.1/Person";
    }

    /// <summary>
    /// The exception log: one entity per unhandled server exception, written by the host's global handler and
    /// reviewed (then purged, i.e. tombstoned) by an administrator. Its schema ships in
    /// <c>Sql/seed/seed-002-exception-log.nq</c>, applied once under the seed name <see cref="SeedName"/>.
    /// </summary>
    public static class ExceptionLog
    {
        public const string SchemaIri = "https://global-webnet.com/schema/ExceptionLog";
        public const string BaseIri = "https://global-webnet.com/id/exception/";
        public const string TypeIri = "https://global-webnet.com/ontology/exception#ExceptionLog";
        public const string DefaultGraph = "https://global-webnet.com/graph/system";
        public const string SeedName = "002-exception-log";

        /// <summary>Seed 003 adds the SignatureId and CorrelationId fields to this schema.</summary>
        public const string SignatureSeedName = "003-exception-signature";
    }

    /// <summary>
    /// An exception signature groups repeats of one failure. Its schema starts with the schema host convention
    /// (<c>https://schema.global-webnet.com/&lt;area&gt;/&lt;name&gt;</c>); defined in <c>Sql/seed/seed-003-exception-signature.nq</c>.
    /// </summary>
    public static class ExceptionSignatureEntity
    {
        public const string SchemaIri = "https://schema.global-webnet.com/exception/ExceptionSignature";
        public const string BaseIri = "https://global-webnet.com/id/exception-signature/";
        public const string TypeIri = "https://global-webnet.com/ontology/exception#ExceptionSignature";
        public const string DefaultGraph = "https://global-webnet.com/graph/system";
    }

    /// <summary>
    /// The tenant and role model already present in the baseline seed (roles, tenants, organizations and the role assignments
    /// that point from a user), described as schemas by <c>Sql/seed/seed-004-identity.nq</c> under the schema host convention.
    /// The data is untouched; the schemas map onto its existing predicates, so the assignments stay where they are
    /// (the user points at each assignment).
    /// </summary>
    public static class Identity
    {
        public const string SeedName = "004-identity";

        /// <summary>Seed 005 marks the assignment pointer fields required (seed 004 had already been applied, and a named seed applies once).</summary>
        public const string RequiredSeedName = "005-identity-required";
        public const string DefaultGraph = "https://global-webnet.com/graph/user/b7963bd0-5ad3-4c83-b61c-fa622cc6a2df";
        public const string SchemaBase = "https://schema.global-webnet.com/identity/";
        public const string IdBase = "https://global-webnet.com/id/";
        public const string OntologyBase = "https://global-webnet.com/ontology/tenant#";

        public const string RoleSchemaIri = SchemaBase + "Role";
        public const string RoleBaseIri = IdBase + "role/";
        public const string RoleTypeIri = OntologyBase + "Role";

        public const string TenantSchemaIri = SchemaBase + "Tenant";
        public const string TenantBaseIri = IdBase + "tenant/";
        public const string TenantTypeIri = OntologyBase + "Tenant";

        public const string BlogSchemaIri = SchemaBase + "Blog";
        public const string BlogBaseIri = IdBase + "blog/";
        public const string BlogTypeIri = OntologyBase + "Blog";

        public const string OrganizationSchemaIri = SchemaBase + "Organization";
        public const string OrganizationBaseIri = IdBase + "organization/";
        public const string OrganizationTypeIri = OntologyBase + "Organization";

        public const string TenantRoleSchemaIri = SchemaBase + "TenantRoleAssignment";
        public const string TenantRoleBaseIri = IdBase + "tenant-role/";
        public const string TenantRoleTypeIri = OntologyBase + "TenantRoleAssignment";

        public const string OrganizationRoleSchemaIri = SchemaBase + "OrganizationRoleAssignment";
        public const string OrganizationRoleBaseIri = IdBase + "organization-role/";
        public const string OrganizationRoleTypeIri = OntologyBase + "OrganizationRoleAssignment";
    }

    /// <summary>
    /// The legacy mirror layer: one schema per ported BlogEngine.NET table, field for field (see ai-research-blog docs, the legacy table
    /// migration plan). Generated into <c>Sql/seed/seed-009-legacy-schemas.nq</c>; never edited by hand.
    /// </summary>
    public static class Legacy
    {
        public const string SchemaSeedName = "009-legacy-schemas";
        public const string DefaultGraph = "https://global-webnet.com/graph/legacy";
        public const string SchemaBase = "https://schema.global-webnet.com/legacy/";
    }

    /// <summary>
    /// The content model (pages, posts, categories, tags), new entities under the schema host convention; shipped by
    /// <c>Sql/seed/seed-006-content.nq</c> and modelled on what the BlogEngine admin edits (see the review in ai-research-blog docs).
    /// </summary>
    public static class Content
    {
        public const string SeedName = "006-content";

        /// <summary>Seed 007 adds the Categories and Tags fields to Post (seed 006 had been applied; a named seed applies once).</summary>
        public const string TaxonomySeedName = "007-post-taxonomy";

        /// <summary>Seed 008 adds RevisionOf to Post: a draft that proposes changes to a published post.</summary>
        public const string RevisionSeedName = "008-post-revision";
        public const string DefaultGraph = "https://global-webnet.com/graph/content";
        public const string SchemaBase = "https://schema.global-webnet.com/content/";
        public const string IdBase = "https://global-webnet.com/id/";
        public const string OntologyBase = "https://global-webnet.com/ontology/content#";

        public const string PageSchemaIri = SchemaBase + "Page";
        public const string PageBaseIri = IdBase + "page/";
        public const string PageTypeIri = OntologyBase + "Page";

        public const string PostSchemaIri = SchemaBase + "Post";
        public const string PostBaseIri = IdBase + "post/";
        public const string PostTypeIri = OntologyBase + "Post";

        public const string CategorySchemaIri = SchemaBase + "Category";
        public const string CategoryBaseIri = IdBase + "category/";
        public const string CategoryTypeIri = OntologyBase + "Category";

        public const string TagSchemaIri = SchemaBase + "Tag";
        public const string TagBaseIri = IdBase + "tag/";
        public const string TagTypeIri = OntologyBase + "Tag";
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

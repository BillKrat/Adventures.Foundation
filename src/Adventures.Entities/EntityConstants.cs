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
}

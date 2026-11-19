namespace Adventures.Entities;

/// <summary>
/// The exception log: one entity per unhandled server exception, written by the host's global handler and
/// reviewed (then purged, i.e. tombstoned) by an administrator. Its schema ships in
/// <c>Sql/seed/seed-002-exception-log.nq</c>, applied once under the seed name <see cref="SeedName"/>.
/// </summary>
public static class ExceptionLogConstants
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
public static class ExceptionSignatureConstants
{
    public const string SchemaIri = "https://schema.global-webnet.com/exception/ExceptionSignature";
    public const string BaseIri = "https://global-webnet.com/id/exception-signature/";
    public const string TypeIri = "https://global-webnet.com/ontology/exception#ExceptionSignature";
    public const string DefaultGraph = "https://global-webnet.com/graph/system";
}

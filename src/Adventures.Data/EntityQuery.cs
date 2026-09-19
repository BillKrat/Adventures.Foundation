namespace Adventures.Data;

/// <summary>
/// Filter criteria for listing entities. All properties are optional (null = no filter on that dimension).
/// </summary>
/// <param name="Tenant">Restrict to a specific tenant (domain).</param>
/// <param name="Org">Restrict to a specific org (blog) within the tenant.</param>
/// <param name="EntityType">Restrict to a specific entity type/discriminator.</param>
/// <param name="Skip">Number of matching rows to skip (paging).</param>
/// <param name="Take">Maximum number of rows to return (paging).</param>
public sealed record EntityQuery(
    string? Tenant = null,
    string? Org = null,
    string? EntityType = null,
    int Skip = 0,
    int Take = 100);

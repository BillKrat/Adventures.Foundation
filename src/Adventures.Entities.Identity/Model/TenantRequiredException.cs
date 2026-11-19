namespace Adventures.Entities;

/// <summary>
/// Tenant content was asked for but the request has no tenant (the host matched none). There is no shared fallback graph, so nothing is read or written; the web
/// host turns this into a 404 for the address. Threat addressed: a silent default into another tenant's data.
/// </summary>
public sealed class TenantRequiredException() : InvalidOperationException("No tenant is set up for this address.");

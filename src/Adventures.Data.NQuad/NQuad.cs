// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Data.NQuad;

/// <summary>
/// A single W3C N-Quad statement: (subject, predicate, object, graph). Mirrors the
/// nquad-end-to-end-poc's Quadruple shape - kept deliberately minimal (no multi-tenancy
/// fields yet) until this library grows beyond the initial red-green refactor.
/// </summary>
public sealed record NQuad(
    Guid Id,
    string Subject,
    string Predicate,
    string Object,
    string? Graph);


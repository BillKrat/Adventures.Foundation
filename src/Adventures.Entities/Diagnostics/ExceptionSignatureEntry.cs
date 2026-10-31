// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>One kind of failure (see <see cref="ExceptionSignature"/>), with how often and when it was seen (see <see cref="EntityConstants.ExceptionSignatureEntity"/>).</summary>
public sealed class ExceptionSignatureEntry(EntitySchema schema) : DynamicEntity(schema);

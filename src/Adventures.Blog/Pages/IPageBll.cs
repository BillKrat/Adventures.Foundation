// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>Business layer of pages: the rules BlogEngine kept in its editor, now in one place (see <see cref="PageBll"/>).</summary>
public interface IPageBll : IEntityBll<Page>
{
}

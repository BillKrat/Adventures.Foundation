// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks.Interfaces
{
    public interface IMockDal : IScopedLifetime
    {
        public string GetData(object sender, EventArgs e);

        public Task<string> GetDataAsync(object sender, EventArgs e);
    }

}

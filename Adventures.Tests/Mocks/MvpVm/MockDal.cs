// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Tests.Mocks.Interfaces;

namespace Adventures.Tests.Mocks.MvpVm
{

    public class MockDal : IMockDal
    {
        public const string MockDataMessage = "Hello World! Mock data from MockDal";
        public string GetData(object sender, EventArgs e)
        {
            return MockDataMessage;
        }

        public Task<string> GetDataAsync(object sender, EventArgs e)
        {
            return Task.FromResult(GetData(sender, e));
        }
    }
}

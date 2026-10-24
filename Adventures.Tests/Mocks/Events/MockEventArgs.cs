// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Tests.Mocks.Events
{
    public class MockEventArgs(string message) : EventArgs
    {
        public string Message { get; set; } = message;
    }
}

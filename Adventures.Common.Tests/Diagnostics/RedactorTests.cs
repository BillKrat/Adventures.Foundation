// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Diagnostics;
using Xunit;

namespace Adventures.Common.Tests.Diagnostics;

public sealed class RedactorTests
{
    [Theory]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk end", "[jwt]")]
    [InlineData("Authorization: Bearer abcdef0123456789xyz", "Bearer [token]")]
    [InlineData("key sk-ant-api03-ABCDEFGH12345678 here", "[api-key]")]
    [InlineData("Host=<db-host>;Port=6432;Password=hunter2;Username=bill", "Password=[redacted]")]
    [InlineData("password: s3cret!", "password=[redacted]")]
    [InlineData("contact bill@example.com please", "[email]")]
    [InlineData("from 208.98.35.166 port 443", "[ip]")]
    [InlineData(@"at C:\Users\billkratochvil\source\x.cs", @"C:\Users\[user]")]
    public void Scrub_RemovesSecretsAndPersonalData(string input, string expectedFragment)
    {
        var result = Redactor.Scrub(input);

        Assert.Contains(expectedFragment, result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("billkratochvil", result);
        Assert.DoesNotContain("bill@example.com", result);
        Assert.DoesNotContain("abcdef0123456789xyz", result);
        Assert.DoesNotContain("s3cret", result);
    }

    [Fact]
    public void Scrub_KeepsWhatTriageNeeds()
    {
        const string text = "The SSL connection could not be established calling https://mcp.global-webnet.com/api/hello (corr 8948795210b848809cb4b1fc7ad90482)";

        Assert.Equal(text, Redactor.Scrub(text));
    }

    [Fact]
    public void ScrubAndLimit_MarksACut()
    {
        var result = Redactor.ScrubAndLimit(new string('x', 100), 10);

        Assert.Equal("xxxxxxxxxx ...[cut]", result);
    }
}

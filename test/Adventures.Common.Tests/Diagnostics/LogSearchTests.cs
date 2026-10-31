// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Diagnostics;
using Xunit;

namespace Adventures.Common.Tests.Diagnostics;

public sealed class LogSearchTests
{
    [Fact]
    public async Task LogSearch_ReturnsAnExceptionLineWhole_AndAGiantOneAsAValidStub()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var bigStack = new string('s', 100_000);
            var giant = new string('g', LogSearch.MaxLineLength + 10);
            await File.WriteAllLinesAsync(Path.Combine(directory, "webapi-json-20261018.log"),
            [
                $$"""{"@t":"2026-10-18T10:00:00Z","@l":"Error","@m":"boom","@x":"{{bigStack}}","CorrelationId":"req-1"}""",
                $$"""{"@t":"2026-10-18T10:00:01Z","@l":"Error","@m":"giant","@x":"{{giant}}","CorrelationId":"req-1"}""",
            ]);

            var lines = await LogSearch.FindLinesAsync(directory, "req-1");

            Assert.Equal(2, lines.Count);
            Assert.Contains(bigStack, lines[0]);
            using var stub = System.Text.Json.JsonDocument.Parse(lines[1]);
            Assert.True(stub.RootElement.GetProperty("truncated").GetBoolean());
            Assert.Equal("giant", stub.RootElement.GetProperty("@m").GetString());
            Assert.True(stub.RootElement.GetProperty("originalLength").GetInt32() > LogSearch.MaxLineLength);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LogSearch_KeepsTheNewestLinesThatFitTheTotalBudget()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var chunk = new string('c', 200_000);
            var lines = Enumerable.Range(0, 30)
                .Select(i => $$"""{"@m":"line {{i}}","@x":"{{chunk}}","CorrelationId":"req-1"}""")
                .ToArray();
            await File.WriteAllLinesAsync(Path.Combine(directory, "webapi-json-20261018.log"), lines);

            var found = await LogSearch.FindLinesAsync(directory, "req-1");

            Assert.True(found.Sum(l => l.Length) <= LogSearch.MaxTotalLength);
            Assert.Contains("line 29", found[^1]);
            Assert.True(found.Count < 30);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LogSearch_FindsOnlyTheLinesOfOneRequest_AcrossFiles()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "webapi-json-20261017.log"),
                ["""{"@m":"old a","CorrelationId":"req-1"}""", """{"@m":"other","CorrelationId":"req-2"}"""]);
            // the spelling Serilog produced for the first JSON log (webapi-.json.log) must keep being found
            await File.WriteAllLinesAsync(Path.Combine(directory, "webapi-.json20261016.log"), ["""{"@m":"first spelling","CorrelationId":"req-1"}"""]);
            await File.WriteAllLinesAsync(Path.Combine(directory, "webapi-json-20261018.log"),
                ["""{"@m":"new a","CorrelationId":"req-1"}""", """{"@m":"none"}"""]);

            var lines = await LogSearch.FindLinesAsync(directory, "req-1");

            Assert.Equal(3, lines.Count);
            Assert.Contains("first spelling", lines[0]);
            Assert.Contains("old a", lines[1]);
            Assert.Contains("new a", lines[2]);
            Assert.Empty(await LogSearch.FindLinesAsync(directory, "bad id!"));
            Assert.Empty(await LogSearch.FindLinesAsync(Path.Combine(directory, "missing"), "req-1"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

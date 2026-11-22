using System.Reflection;
using Adventures.Common.Diagnostics;
using Adventures.Diagnostics.WebApi;
using Adventures.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

/// <summary>"Prompt Claude through MCP": a row of the exception list has an entry id even when it has no correlation id, so the machine surface can read one exception by that id.</summary>
public sealed class ExceptionByIdTests
{
    private sealed class FakeReader : IDiagnosticsReader
    {
        private static readonly ExceptionOccurrence Known = new("e1", DateTimeOffset.UtcNow, "System.TimeoutException", "slow", "GET /a", "Admin", "corr-1", "sig-1");

        public Task<CorrelationReport> FindByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExceptionOccurrence>> ListRecentAsync(int max = 50, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExceptionSignatureSummary>> ListSignaturesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ExceptionDetail?> FindByEntryIdAsync(string entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(entryId == "e1" ? new ExceptionDetail(Known, null) : null);
    }

    [Fact]
    public async Task AKnownEntryIsReturned_WithItsCorrelationIdSoTheNextToolCanTraceTheRequest()
    {
        var result = await new McpDiagnosticsController(new FakeReader()).Exception("e1", CancellationToken.None);

        var detail = Assert.IsType<ExceptionDetail>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("corr-1", detail.Occurrence.CorrelationId);
    }

    [Fact]
    public async Task AnUnknownEntryIsNotFound_AndAMalformedIdIsABadRequest()
    {
        var controller = new McpDiagnosticsController(new FakeReader());

        Assert.IsType<NotFoundResult>((await controller.Exception("nope", CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Exception("../x y", CancellationToken.None)).Result);
    }

    [Fact]
    public void TheToolIsDeclared_ReadOnly_UnderTheDiagnosticsScope()
    {
        var tool = DiagnosticsTools.All.Single(t => t.Name == "get_exception");

        Assert.Equal(("GET", "api/diagnostics/exception/{id}", McpDiagnosticsController.RequiredScope), (tool.Method, tool.PathTemplate, tool.RequiredScope));
        Assert.Contains(typeof(McpDiagnosticsController).GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "Scope:mcp.diagnostics.read");
    }
}

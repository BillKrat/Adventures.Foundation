namespace Adventures.Common.Diagnostics;

/// <summary>
/// The outcome of one live check of a dependency. <see cref="Detail"/> is a short non-sensitive code ("ok", "http_401",
/// "timeout", "exception_HttpRequestException"), never an exception message. <see cref="Failure"/> carries the exception for
/// the caller that records it; <see cref="Message"/> is what a successful call returned; <see cref="Skipped"/> means the
/// dependency is not configured in this host (so there was nothing to check).
/// </summary>
public sealed record ProbeResult(
    bool Ok, long DurationMs, string Detail, string? Message = null, Exception? Failure = null, bool Skipped = false)
{
    public static implicit operator ProbeResult((bool Ok, long DurationMs, string Detail) result) =>
        new(result.Ok, result.DurationMs, result.Detail);
}

/// <summary>Live checks of the dependencies, shared by the health endpoint and the triage so both ask the same questions the same way.</summary>
public interface IDependencyProbes
{
    Task<ProbeResult> ProbeMcpAsync(CancellationToken cancellationToken);

    Task<ProbeResult> ProbeDatabaseAsync(CancellationToken cancellationToken);
}

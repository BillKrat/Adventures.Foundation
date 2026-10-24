// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Identity.Tests;

internal sealed class FakeUserCredentialStore : IUserCredentialStore
{
    public UserCredential? CredentialToReturn { get; set; }
    public List<Guid> SuccessfulLoginCalls { get; } = [];
    public List<(Guid UserId, int LockoutThreshold, TimeSpan LockoutDuration)> FailedLoginCalls { get; } = [];

    public List<(Guid UserId, string Hash, string Algorithm)> SetPasswordCalls { get; } = [];

    public List<(Guid UserId, string Hash, string Algorithm, bool MustChange)> CreateOrResetCalls { get; } = [];

    public Task CreateOrResetAsync(Guid userId, string passwordHash, string passwordAlgorithm, bool mustChangePassword, CancellationToken cancellationToken = default)
    {
        CreateOrResetCalls.Add((userId, passwordHash, passwordAlgorithm, mustChangePassword));
        return Task.CompletedTask;
    }

    public Task SetPasswordAsync(Guid userId, string passwordHash, string passwordAlgorithm, CancellationToken cancellationToken = default)
    {
        SetPasswordCalls.Add((userId, passwordHash, passwordAlgorithm));
        return Task.CompletedTask;
    }

    public Task<UserCredential?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CredentialToReturn);

    public Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        SuccessfulLoginCalls.Add(userId);
        return Task.CompletedTask;
    }

    public Task RecordFailedLoginAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken cancellationToken = default)
    {
        FailedLoginCalls.Add((userId, lockoutThreshold, lockoutDuration));
        return Task.CompletedTask;
    }
}

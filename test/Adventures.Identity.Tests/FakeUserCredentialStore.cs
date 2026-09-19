namespace Adventures.Identity.Tests;

internal sealed class FakeUserCredentialStore : IUserCredentialStore
{
    public UserCredential? CredentialToReturn { get; set; }
    public List<Guid> SuccessfulLoginCalls { get; } = [];
    public List<(Guid UserId, int LockoutThreshold, TimeSpan LockoutDuration)> FailedLoginCalls { get; } = [];

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

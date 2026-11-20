using System.Security.Claims;
using Adventures.Data;
using Adventures.Security;
using Xunit;

namespace Adventures.Identity.Tests;

/// <summary>
/// A person whose password must be changed (a seeded or administrator-set one) carries a marker claim in the token, and the gate answers
/// from the store whether the change is still pending, so the marker stops mattering the moment the password is changed.
/// Threat addressed: an account left on its default password working like any other.
/// </summary>
public sealed class PasswordChangeGateTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Entity User(string status) => new(
        Id: UserId, Tenant: "acme.com", Org: "org", EntityType: "user",
        StandardFieldsJson: $$"""{"username":"admin","email":"a@b.c","display_name":"Admin","status":"{{status}}","roles":[]}""",
        CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow, RowVersion: 1);

    private static UserCredential Credential(bool mustChange) => new(UserId, "hash", "algo", mustChange, 0, null, null);

    private static PasswordChangeGate Gate(Entity? entity, UserCredential? credential) =>
        new(new FakeEntityRepository { EntityToReturn = entity }, new FakeUserCredentialStore { CredentialToReturn = credential });

    [Theory]
    [InlineData(true, "active")]
    [InlineData(false, "must_change_password")]
    [InlineData(true, "must_change_password")]
    public async Task ThePasswordChangeIsPending_FromEitherTheCredentialFlagOrTheEntityStatus(bool credentialFlag, string status)
    {
        Assert.True(await Gate(User(status), Credential(credentialFlag)).IsPendingAsync(UserId));
    }

    [Fact]
    public async Task OnceBothAreCleared_NothingIsPending()
    {
        Assert.False(await Gate(User("active"), Credential(false)).IsPendingAsync(UserId));
    }

    [Fact]
    public async Task ADisabledOrUnknownAccount_IsNotPending_SoItIsNotHeldBehindTheGate()
    {
        Assert.False(await Gate(User("disabled"), Credential(false)).IsPendingAsync(UserId));
        Assert.False(await Gate(null, null).IsPendingAsync(UserId));
    }

    [Fact]
    public async Task APendingChangeMarksTheToken_AndAFreeAccountDoesNot()
    {
        foreach (var (pending, expected) in new[] { (true, true), (false, false) })
        {
            var entities = new FakeEntityRepository { EntityToReturn = User("active") };
            var credentials = new FakeUserCredentialStore { CredentialToReturn = Credential(pending) };
            var tokens = new FakeJwtTokenService();
            var service = new UserAccountService(entities, credentials, new FakePasswordHasher { VerifyResult = true }, tokens);

            await service.LoginAsync("acme.com", "admin", "Password");

            var claims = tokens.IssueTokenCalls.Single().Claims;
            Assert.Equal(expected, claims.Any(c => c.Type == WellKnownClaims.PasswordChange && c.Value == "true"));
        }
    }
}

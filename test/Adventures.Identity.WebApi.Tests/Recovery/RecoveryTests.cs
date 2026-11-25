using System.Reflection;
using Adventures.Entities;
using Adventures.Identity;
using Adventures.Identity.WebApi;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Recovery without e-mail: the owner proves control of the installation by putting a file named with a one-time code into the site's App_Data folder, then chooses a new
/// password. Threats addressed: a reset triggered by anyone (it needs the file), a reset to a known default (the owner chooses the password), guessing (128-bit codes, rate
/// limited), a path built from the request (the code is hex only), a code used twice or after it expired, and learning anything about accounts before the proof.
/// </summary>
public sealed class RecoveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "adv-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private void Upload(RecoveryChallenge challenge, string? extension = null) =>
        File.WriteAllText(Path.Combine(_folder, challenge.FileName + extension), "x");

    [Fact]
    public void Start_GivesA128BitHexCode_TheFileNameAndAFifteenMinuteLife_AndMakesTheFolder()
    {
        var clock = new Clock();
        var challenge = new FileRecoveryProof(_folder, clock).Start();

        Assert.Matches("^[0-9a-f]{32}$", challenge.Code);
        Assert.Equal("recovery-" + challenge.Code, challenge.FileName);
        Assert.Equal("App_Data", challenge.Folder);
        Assert.Equal(clock.Now.AddMinutes(15), challenge.ExpiresAtUtc);
        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public void Start_StillAnswers_WhenTheAppCannotMakeTheFolder_ThePersonMakesItOverFtp()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "adv-recovery-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "a file where the folder should be");
        try
        {
            var challenge = new FileRecoveryProof(Path.Combine(blocker, "App_Data")).Start();

            Assert.Matches("^[0-9a-f]{32}$", challenge.Code);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void TheCodeIsProven_OnlyOnceTheFileIsThere_WithOrWithoutTxt()
    {
        var proof = new FileRecoveryProof(_folder);
        var plain = proof.Start();
        var withTxt = proof.Start();

        Assert.False(proof.IsProven(plain.Code));
        Upload(plain);
        Upload(withTxt, ".txt");

        Assert.True(proof.IsProven(plain.Code));
        Assert.True(proof.IsProven(withTxt.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void AMalformedCode_IsNeverProven(string? code) => Assert.False(new FileRecoveryProof(_folder).IsProven(code));

    [Fact]
    public void AFileForACodeThatWasNeverIssued_ProvesNothing_EvenAStaleOneFromBeforeARestart()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "recovery-0123456789abcdef0123456789abcdef"), "x");

        Assert.False(new FileRecoveryProof(_folder).IsProven("0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void ACodeExpiresAfterFifteenMinutes()
    {
        var clock = new Clock();
        var proof = new FileRecoveryProof(_folder, clock);
        var challenge = proof.Start();
        Upload(challenge);
        clock.Now = clock.Now.AddMinutes(14);
        Assert.True(proof.IsProven(challenge.Code));

        clock.Now = clock.Now.AddMinutes(2);

        Assert.False(proof.IsProven(challenge.Code));
    }

    [Fact]
    public void Consuming_RemovesTheFile_AndEveryOtherPendingCode_SoNothingCanBeUsedAgain()
    {
        var proof = new FileRecoveryProof(_folder);
        var used = proof.Start();
        var other = proof.Start();
        Upload(used);
        Upload(other);

        proof.Consume(used.Code);

        Assert.False(File.Exists(Path.Combine(_folder, used.FileName)));
        Assert.False(proof.IsProven(used.Code));
        Assert.False(proof.IsProven(other.Code));
    }

    [Fact]
    public void OnlyTwentyCodesAreKept_TheOldestIsForgotten()
    {
        var proof = new FileRecoveryProof(_folder);
        var first = proof.Start();
        Upload(first);
        for (var i = 0; i < FileRecoveryProof.MaxPending; i++)
        {
            proof.Start();
        }

        Assert.False(proof.IsProven(first.Code));
    }

    // ---- the controller -----------------------------------------------------------------------------------------------------------------------------------------

    private sealed class Resetter(ResetOutcome outcome = ResetOutcome.Reset) : IPasswordResetter
    {
        public (string Tenant, string User, string Password)? Called { get; private set; }

        public Task<ResetOutcome> ResetAsync(string tenant, string username, string newPassword, CancellationToken cancellationToken = default)
        {
            Called = (tenant, username, newPassword);
            return Task.FromResult(outcome);
        }
    }

    private sealed class Sites : ITenantAliasEditor
    {
        public Task<IReadOnlyList<TenantHostInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantHostInfo>>([new("t1", "Main", "main.example", []), new("t2", "Other", "other.example", [])]);

        public Task<IReadOnlyList<string>?> AddAsync(string tenantId, string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>?> RemoveAsync(string tenantId, string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private (RecoveryController Controller, FileRecoveryProof Proof, Resetter Resetter) Make(ResetOutcome outcome = ResetOutcome.Reset, bool onTenant = true)
    {
        var proof = new FileRecoveryProof(_folder);
        var resetter = new Resetter(outcome);
        var current = onTenant ? new CurrentTenant("t1", "Main", "main.example", "main.example", TenantMatch.Domain) : new CurrentTenant(null, null, null, "x.example", TenantMatch.None);
        return (new RecoveryController(proof, resetter, new Sites(), new FixedRequestTenant(current)), proof, resetter);
    }

    private static RecoveryComplete Body(string code, string? site = null, string user = "Admin", string password = "a-long-enough-password") => new(code, site, user, password);

    [Fact]
    public void Start_AnswersTheChallenge_WithNoAbsolutePath()
    {
        var (controller, _, _) = Make();

        var challenge = Assert.IsType<RecoveryChallenge>(Assert.IsType<OkObjectResult>(controller.Start()).Value);

        Assert.Equal("App_Data", challenge.Folder);
        Assert.DoesNotContain(_folder, System.Text.Json.JsonSerializer.Serialize(challenge));
    }

    [Fact]
    public async Task WithoutTheFile_NothingIsReset_AndTheAnswerIsTheSameForEveryBadInput()
    {
        var (controller, proof, resetter) = Make();
        var challenge = proof.Start();   // no file uploaded

        var noFile = Assert.IsType<BadRequestObjectResult>(await controller.Complete(Body(challenge.Code), CancellationToken.None));
        var unknownCode = Assert.IsType<BadRequestObjectResult>(await controller.Complete(Body("0123456789abcdef0123456789abcdef", user: "nobody"), CancellationToken.None));

        Assert.Equal(noFile.Value, unknownCode.Value);
        Assert.Null(resetter.Called);
    }

    [Fact]
    public async Task AWeakPassword_IsRefused_WithoutUsingUpTheCode()
    {
        var (controller, proof, resetter) = Make();
        var challenge = proof.Start();
        Upload(challenge);

        Assert.IsType<UnprocessableEntityObjectResult>(await controller.Complete(Body(challenge.Code, password: "short"), CancellationToken.None));

        Assert.Null(resetter.Called);
        Assert.True(proof.IsProven(challenge.Code));
    }

    [Fact]
    public async Task WithTheFile_ThePasswordTheOwnerChoseIsSet_ForTheNamedAccountOfTheSite_AndTheCodeIsUsedUp()
    {
        var (controller, proof, resetter) = Make();
        var challenge = proof.Start();
        Upload(challenge);

        Assert.IsType<NoContentResult>(await controller.Complete(Body(challenge.Code, site: "OTHER.example", user: "Admin", password: "my-own-new-password"), CancellationToken.None));

        Assert.Equal(("other.example", "Admin", "my-own-new-password"), resetter.Called);
        Assert.IsType<BadRequestObjectResult>(await controller.Complete(Body(challenge.Code), CancellationToken.None));
    }

    [Fact]
    public async Task WithoutASite_TheTenantOfTheHostIsUsed_AndOnAHostWithNoTenantASiteIsRequired()
    {
        var (onTenant, proofA, resetterA) = Make();
        var a = proofA.Start();
        Upload(a);
        Assert.IsType<NoContentResult>(await onTenant.Complete(Body(a.Code), CancellationToken.None));
        Assert.Equal("main.example", resetterA.Called!.Value.Tenant);

        var (noTenant, proofB, resetterB) = Make(onTenant: false);
        var b = proofB.Start();
        Upload(b);
        Assert.IsType<BadRequestObjectResult>(await noTenant.Complete(Body(b.Code), CancellationToken.None));
        Assert.Null(resetterB.Called);
        Assert.True(proofB.IsProven(b.Code));
    }

    [Fact]
    public async Task AnUnknownSiteOrAccount_IsNotFound_AfterTheProof_AndTheCodeSurvivesForARetry()
    {
        var (controller, proof, _) = Make(ResetOutcome.NotFound);
        var challenge = proof.Start();
        Upload(challenge);

        Assert.IsType<NotFoundObjectResult>(await controller.Complete(Body(challenge.Code, site: "nowhere.example"), CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.Complete(Body(challenge.Code, site: "main.example", user: "ghost"), CancellationToken.None));

        Assert.True(proof.IsProven(challenge.Code));
    }

    [Fact]
    public void BothEndpoints_AreAnonymous_RateLimitedLikeSignIn_AndSizeCapped()
    {
        var type = typeof(RecoveryController);
        Assert.NotEmpty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(type.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/recovery");
        foreach (var action in new[] { nameof(RecoveryController.Start), nameof(RecoveryController.Complete) })
        {
            var method = type.GetMethod(action)!;
            Assert.Contains(method.GetCustomAttributes<EnableRateLimitingAttribute>(), a => a.PolicyName == RequestHardening.TokenPolicy);
            Assert.NotEmpty(method.GetCustomAttributes<RequestSizeLimitAttribute>());
        }
    }
}

using System.Net;
using Adventures.Entities;
using Adventures.Security;
using Xunit;

namespace Adventures.Client.Tests;

public class ShellViewModelTests
{
    private static string Token()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(new byte[32]);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("i", "a", [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "tester")],
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static readonly EntitySchemaModel Schema = new("urn:test", [new EntityFieldSchema("UserName", "String", IsRequired: true)]);

    private static (ShellViewModel Shell, ApiSession Session, System.Runtime.CompilerServices.StrongBox<HttpStatusCode> Status) Build()
    {
        var status = new System.Runtime.CompilerServices.StrongBox<HttpStatusCode>(HttpStatusCode.OK);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("token", StringComparison.Ordinal))
            {
                return StubHandler.Json(new { accessToken = Token(), expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), mustChangePassword = false });
            }

            if (status.Value != HttpStatusCode.OK)
            {
                return StubHandler.Empty(status.Value);
            }

            var row = new EntityDataModel("u1", new Dictionary<string, string?> { ["UserName"] = "Ann" });
            return request.Method == HttpMethod.Get && path.EndsWith("/u1", StringComparison.Ordinal)
                ? StubHandler.Json(new EntityFormModel(Schema, row))
                : request.Method == HttpMethod.Put ? StubHandler.Json(new EntityFormModel(Schema, row))
                : StubHandler.Json(new EntityGridModel(Schema, [row], new GridPaging(1, 25, 1, null, false), CanCreate: true));
        });
        var http = StubHandler.Client(handler);
        var session = new ApiSession(http, new AmbientPrincipal());
        var presenter = new HttpEntityPresenter<DynamicRecord>(http, "api/users");
        var shell = new ShellViewModel(session, new SignInViewModel(session), new EntityListViewModel(presenter), new EntityEditViewModel(presenter));
        return (shell, session, status);
    }

    [Fact]
    public async Task The_walk_from_sign_in_to_list_to_edit_and_back()
    {
        var (shell, session, _) = Build();
        Assert.Same(shell.SignIn, shell.Current);

        shell.SignIn.UserName = "tester";
        shell.SignIn.Password = "pw";
        await shell.SignIn.SignInAsync();
        await Settle();
        Assert.Same(shell.List, shell.Current);
        Assert.Equal("Ann", shell.List.Rows.Single().Cells[0]);

        shell.List.Selected = shell.List.Rows.Single();
        shell.List.EditCommand.Execute(null);
        await Settle();
        Assert.Same(shell.Edit, shell.Current);
        Assert.Equal("Ann", shell.Edit.Fields.Single().Value);

        await shell.Edit.SaveAsync();
        await Settle();
        Assert.Same(shell.List, shell.Current);

        shell.SignOutCommand.Execute(null);
        Assert.Same(shell.SignIn, shell.Current);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task A_lapsed_session_sends_the_person_back_to_sign_in_with_a_reason()
    {
        var (shell, session, status) = Build();
        shell.SignIn.UserName = "tester";
        shell.SignIn.Password = "pw";
        await shell.SignIn.SignInAsync();
        await Settle();

        status.Value = HttpStatusCode.Unauthorized;
        await shell.List.LoadAsync();

        Assert.Same(shell.SignIn, shell.Current);
        Assert.False(session.IsSignedIn);
        Assert.Contains("session has ended", shell.SignIn.Error);
    }

    private static Task Settle() => Task.Delay(50);
}

using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Lets a platform administrator see and open people of every tenant on the Users screen, so they can find a person (for example a service account) and give them a role in the
/// tenant being worked in. A platform administrator sees everyone when opening or changing a person, and in the list only when they ask for it (<c>Scope=Everyone</c>); anyone
/// else is never widened. The role assignment itself still has to name the tenant being worked in (see <see cref="UsersController"/>). Threat addressed: cross-tenant reads
/// limited to the platform right.
/// </summary>
public sealed class PlatformUserVisibilityFilter(IRightsAuthorizer authorizer, UserVisibility visibility) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && await authorizer.IsAllowedAsync(context.HttpContext.User, Rights.PlatformManage, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            var isList = string.Equals(context.ActionDescriptor.RouteValues["action"], nameof(UsersController.List), StringComparison.Ordinal);
            var asked = string.Equals(context.HttpContext.Request.Query["filter"], UsersController.ScopeFilter, StringComparison.Ordinal)
                && string.Equals(context.HttpContext.Request.Query["filterValue"], UsersController.EveryoneValue, StringComparison.Ordinal);
            visibility.SeeEveryone = !isList || asked;
        }

        await next().ConfigureAwait(false);
    }
}

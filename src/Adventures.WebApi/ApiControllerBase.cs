using Microsoft.AspNetCore.Mvc;

namespace Adventures.WebApi;

/// <summary>
/// Base for any Adventures.Foundation-provided controller - the bit that is actually duplicated
/// across hand-written controllers today (a Delete action that catches a Bll's business-rule
/// violation and turns it into a 409, the same handful of lines in more than one controller).
/// Deliberately thin: this is not an attempt to generalize routing or request shapes, only the
/// handful of truly common response-shaping concerns.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Runs <paramref name="action"/> and turns an <see cref="InvalidOperationException"/> (a Bll's
    /// business-rule violation - e.g. "you cannot delete your own account") into a 409 Conflict
    /// with the exception's message as the body, instead of letting it become an unhandled 500.
    /// </summary>
    protected async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}

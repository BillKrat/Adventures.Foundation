using Adventures.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.WebApi;

/// <summary>
/// Base for any Adventures.Foundation-provided controller - the response-shaping that would otherwise be
/// copy-pasted across hand-written controllers. Deliberately thin: this is not an attempt to generalize routing or
/// request shapes.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Runs <paramref name="action"/> and maps the two exceptions that are *meant* for the caller:
    /// <see cref="BusinessRuleException"/> (a rule refused the request, message written for the user) becomes 409
    /// Conflict, and <see cref="InputValidationException"/> becomes 400 with the field errors. Every other exception
    /// is a system fault and is deliberately not caught here: it propagates to the host's global handler, which
    /// logs it and answers with a generic body (exception shielding - internals never reach the client).
    /// </summary>
    protected async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (BusinessRuleException ex)
        {
            return Conflict(ex.Message);
        }
        catch (InputValidationException ex)
        {
            return ValidationFailure(ex);
        }
    }

    /// <summary>Same mapping as <see cref="GuardedAsync(Func{Task{IActionResult}})"/> for actions that return a typed result.</summary>
    protected async Task<ActionResult<T>> GuardedAsync<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (BusinessRuleException ex)
        {
            return Conflict(ex.Message);
        }
        catch (InputValidationException ex)
        {
            return ValidationFailure(ex);
        }
    }

    private BadRequestObjectResult ValidationFailure(InputValidationException exception) =>
        BadRequest(new ValidationProblemDetails(exception.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = 400,
        });
}

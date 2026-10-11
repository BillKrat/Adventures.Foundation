namespace Adventures.Common.Exceptions;

/// <summary>
/// Input crossing a trust boundary failed validation (unknown field, wrong type, missing required
/// value). Carries every error by field name so a host can answer with one 400 listing all of them.
/// The messages are written here, never taken from a parser's own exception text.
/// </summary>
public sealed class InputValidationException : Exception
{
    public InputValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    public InputValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    /// <summary>Error messages keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

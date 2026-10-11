namespace Adventures.Common.Exceptions;

/// <summary>
/// A business rule refused the request (e.g. "you cannot delete your own account"). The message is
/// written by the rule's author for the end user, so a host may show it as-is (HTTP 409). Everything
/// else that goes wrong - a missing schema, a store failure - is a system exception: it must be logged
/// and shielded, never echoed to the caller (AppArch: exception shielding).
/// </summary>
public class BusinessRuleException(string message) : Exception(message);

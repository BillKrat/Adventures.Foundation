using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Generic keyed-service resolution helper for interfaces with multiple keyed implementations (today
/// <see cref="Adventures.Data.NQuad.INQuadStore"/>, keyed "memory"/"postgres"; a future <c>ITools</c>, etc.).
/// </summary>
/// <remarks>
/// A keyed registration can fail to resolve for two different reasons: the key was never registered, or it was
/// registered with a factory that throws because one of its own dependencies isn't satisfied (e.g. a missing
/// connection string). Callers holding only a runtime key - not knowing in advance which keys have that kind of
/// extra requirement - shouldn't need an <c>if (key == "postgres")</c> to account for the second case. This
/// extension collapses both outcomes into a single "no service available for this key" result, so business logic
/// can resolve a dynamic key generically and only needs one fallback path, regardless of which key or why it
/// failed. Placed in the <c>Microsoft.Extensions.DependencyInjection</c> namespace so it's discoverable wherever
/// keyed services already are, without an extra <c>using</c>.
/// </remarks>
public static class KeyedServiceResolutionExtensions
{
    /// <summary>
    /// Attempts to resolve a keyed service, treating "key not registered" and "registered but construction failed"
    /// as the same negative result.
    /// </summary>
    public static bool TryGetKeyedService<TService>(this IServiceProvider provider, object? serviceKey, [NotNullWhen(true)] out TService? service)
        where TService : class
    {
        try
        {
            service = provider.GetKeyedService<TService>(serviceKey);
        }
        catch (InvalidOperationException)
        {
            service = null;
        }

        return service is not null;
    }
}

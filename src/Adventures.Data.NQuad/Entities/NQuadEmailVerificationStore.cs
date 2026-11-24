using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Data.NQuad;

/// <summary><see cref="IEmailVerificationStore"/> as statements about the person in their own graph, outside the tenant graphs and the entity types, so no screen or generic update can write them.</summary>
public sealed class NQuadEmailVerificationStore(INQuadStore store) : IEmailVerificationStore
{
    public const string Graph = "https://global-webnet.com/graph/email-verification";
    public const string PredicateBase = "https://global-webnet.com/ontology/email-verification#";

    public Task AddAsync(string userId, string kind, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return store.InsertAsync(new NQuad(Guid.NewGuid(), UserConstants.BaseIri + userId, PredicateBase + kind, value, Graph), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAsync(string userId, string kind, CancellationToken cancellationToken = default) =>
        (await store.QueryAsync(UserConstants.BaseIri + userId, PredicateBase + kind, graph: Graph, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Select(quad => quad.Object ?? string.Empty).ToList();
}

public static class NQuadEmailVerificationServiceCollectionExtensions
{
    /// <summary>Registers the verification store and service (per request). The host must also register an <see cref="IEmailSender"/>.</summary>
    public static IServiceCollection AddNQuadEmailVerification(this IServiceCollection services)
    {
        services.AddScoped<IEmailVerificationStore, NQuadEmailVerificationStore>();
        services.AddScoped<EmailVerificationService>(p => new EmailVerificationService(p.GetRequiredService<IEmailVerificationStore>(), p.GetRequiredService<IEmailSender>(), p.GetService<TimeProvider>()));
        return services;
    }
}

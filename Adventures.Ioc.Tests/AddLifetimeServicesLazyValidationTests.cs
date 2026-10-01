using Adventures.Ioc.Extensions;
using Adventures.Ioc.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Ioc.Tests
{
    /// <summary>
    /// A dependency this test deliberately never registers anywhere - standing in for a real
    /// example found in ai-research-blog: a Bll from a referenced library whose own dependency
    /// (e.g. IEntityRepository{SchemaEntity}) the consuming host never wires up because it does
    /// not use that Bll.
    /// </summary>
    public interface IDependencyNobodyRegisters
    {
    }

    public sealed class BrokenScopedService(IDependencyNobodyRegisters dependency) : IScopedLifetime
    {
    }

    /// <summary>
    /// Regression coverage for the fix: AddLifetimeServices registers everything it discovers via
    /// a factory delegate, specifically so an unused type with an unsatisfiable dependency (like
    /// BrokenScopedService above) cannot fail Build() under ValidateOnBuild - only resolving it
    /// should fail, exactly as ordinary DI would for anything else.
    /// </summary>
    public class AddLifetimeServicesLazyValidationTests
    {
        [Fact]
        public void Build_DoesNotThrow_ForUnusedTypeWithUnsatisfiableDependency()
        {
            var services = new ServiceCollection();
            services.AddLifetimeServices();

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            Assert.NotNull(provider);
        }

        [Fact]
        public void ResolvingTheBrokenType_StillThrows_AtFirstUse()
        {
            var services = new ServiceCollection();
            services.AddLifetimeServices();

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            using var scope = provider.CreateScope();
            Assert.Throws<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredKeyedService<IScopedLifetime>(nameof(BrokenScopedService)));
        }
    }
}

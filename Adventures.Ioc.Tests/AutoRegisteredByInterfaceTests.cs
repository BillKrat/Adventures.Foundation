// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Ioc.Interfaces;
using Adventures.Tests.Base;
using Adventures.Ioc.Extensions;
using Adventures.Tests.Mocks.Foo;
using Adventures.Tests.Mocks.Interfaces;
using Adventures.Tests.Mocks.MvpVm;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Ioc.Tests
{
    public class AutoRegisteredByInterfaceTests : TestIocBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            // Register additional services if needed for testing
        }

        [Fact]
        public void KeyedLifetimeServices_AreResolvable()
        {
            Assert.True(ServiceProvider.ResolveKey<ISingletonLifetime>(nameof(FooSingleton), out var fooSingleton));
            Assert.True(ServiceProvider.ResolveKey<ITransientLifetime>(nameof(FooTransient), out var fooTransient));
            Assert.True(ScopedServiceProvider.ResolveKey<IScopedLifetime>(nameof(FooScoped), out var fooScoped));
        }

        [Fact]
        public void DerivedLifetimeInterfaces_AreResolvable()
        {
            Assert.True(ServiceProvider.Resolve<IFooBarSingleton>(out var fooBarSingleton));
            Assert.True(ServiceProvider.Resolve<IFooBarTransient>(out var fooBarTransient));
            Assert.True(ScopedServiceProvider.Resolve<IFooBarScoped>(out var fooBarScoped));
        }

        [Fact]
        public void InterfaceWithMultipleImplementations_HasNoUnkeyedRegistration()
        {
            Assert.False(ServiceProvider.Resolve<IMockBll>(out _));
            Assert.True(ServiceProvider.ResolveKey<IMockBll>(nameof(MockBll), out _));
            Assert.True(ServiceProvider.ResolveKey<IMockBll>(nameof(MockBllV2), out _));
        }

    }
}

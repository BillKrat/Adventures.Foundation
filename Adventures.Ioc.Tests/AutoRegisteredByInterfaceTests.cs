using Adventures.Ioc.Interfaces;
using Adventures.Tests.Base;
using Adventures.Tests.Extensions;
using Adventures.Tests.Mocks.Foo;
using Adventures.Tests.Mocks.Interfaces;
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


    }
}

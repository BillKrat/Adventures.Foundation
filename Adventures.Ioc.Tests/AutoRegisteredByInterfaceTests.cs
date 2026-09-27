using Adventures.Ioc.Interfaces;
using Adventures.Tests.Base;
using Adventures.Tests.Extensions;
using Adventures.Tests.Mocks;
using Adventures.Tests.Mocks.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Ioc.Tests
{
    public class AutoRegisteredByInterfaceTests : TestIocBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddLifetimeServices();
        }

        [Fact]
        public void KeyedLifetimeServices_AreResolvable()
        {
            Assert.True(ServiceProvider.TryGetKeyedService<ISingletonLifetime>(nameof(FooSingleton), out var fooSingleton));
            Assert.True(ServiceProvider.TryGetKeyedService<ITransientLifetime>(nameof(FooTransient), out var fooTransient));
            Assert.True(ScopedServiceProvider.TryGetScopedKeyService<IScopedLifetime>(nameof(FooScoped), out var fooScoped));
        }

        [Fact]
        public void DerivedLifetimeInterfaces_AreResolvable()
        {
            Assert.True(ServiceProvider.TryGetKeyedService<IFooBarSingleton>(null, out var fooBarSingleton));
            Assert.True(ServiceProvider.TryGetKeyedService<IFooBarTransient>(null, out var fooBarTransient));
            Assert.True(ScopedServiceProvider.TryGetScopedKeyService<IFooBarScoped>(null, out var fooBarScoped));
        }


    }
}

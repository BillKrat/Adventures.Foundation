using Adventures.Ioc.Extensions;
using Adventures.Ioc.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Ioc.Tests
{
    public interface IScanThing : IScopedLifetime
    {
    }

    public sealed class ScanThing : IScanThing
    {
    }

    /// <summary>A second implementation the host wires by hand (a decorator for one screen): left out of the scan so the interface keeps its single, unkeyed registration.</summary>
    [ExcludeFromLifetimeScan]
    public sealed class ScanThingDecorator : IScanThing
    {
    }

    public class ExcludeFromScanTests
    {
        [Fact]
        public void AnExcludedImplementationDoesNotMakeTheInterfaceAmbiguous()
        {
            var services = new ServiceCollection();
            services.AddLifetimeServices();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsType<ScanThing>(scope.ServiceProvider.GetRequiredService<IScanThing>());
            Assert.Null(provider.GetKeyedService<IScanThing>(nameof(ScanThingDecorator)));
        }
    }
}

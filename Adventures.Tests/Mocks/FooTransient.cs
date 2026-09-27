using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks
{
    public class FooTransient : ITransientLifetime
    {
        public string Name { get; set; } = "FooTransient";
    }
}

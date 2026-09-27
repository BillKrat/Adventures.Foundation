using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks
{
    public class FooScoped : IScopedLifetime
    {
        public string Name { get; set; } = "FooScoped";
    }
}

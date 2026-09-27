using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks.Foo
{
    public class FooTransient : ITransientLifetime
    {
        public string Name { get; set; } = "FooTransient";
    }
}

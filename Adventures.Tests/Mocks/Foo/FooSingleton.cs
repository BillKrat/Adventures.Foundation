using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks.Foo
{
    public class FooSingleton : ISingletonLifetime
    {
        public string Name { get; set; } = "FooSingleton";
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Tests.Base
{
    public abstract class TestIocBase : IAsyncLifetime
    {
        public IServiceProvider ServiceProvider { get; private set; } = default!;
        public IServiceScope ScopedServiceProvider { get; private set; } = default!;

        protected virtual Task DisposeStoreAsync() => Task.CompletedTask;

        public async Task InitializeAsync() => ServiceProvider = await CreateServiceProviderAsync();

        public async Task DisposeAsync()
        {
            await DisposeStoreAsync();

            if (ScopedServiceProvider is IAsyncDisposable asyncDisposableScope)
            {
                await asyncDisposableScope.DisposeAsync();
            }
            else if (ScopedServiceProvider is IDisposable disposableScope)
            {
                disposableScope.Dispose();
            }

            if (ServiceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        protected virtual Task<IServiceProvider> CreateServiceProviderAsync()
        {
            // 1. Arrange: Use WebApplication.CreateBuilder to set up the host environment
            var builder = WebApplication.CreateBuilder();

            this.ConfigureServices(builder.Services);

            // 2. Act: Call your extension method to register BLL/DAL
            var app = builder.Build();

            // 3. Assert: Verify the container can resolve the top-level BLL services
            ScopedServiceProvider = app.Services.CreateScope();

            return Task.FromResult(app.Services);
        }

        protected abstract void ConfigureServices(IServiceCollection services);
    }
}

using Adventures.Tests.Base;
using Adventures.Tests.Extensions;
using Adventures.Tests.Mocks.Events;
using Adventures.Tests.Mocks.Interfaces;
using Adventures.Tests.Mocks.MvpVm;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Common.Tests
{
    public class MvpVmTests : TestIocBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
        }

        [Fact]
        public void CanResolvePipeline()
        {
            AssertPipeline("sync", nameof(MockBll));
        }

        [Fact]
        public async Task CanResolvePipelineAsync()
        {
            await AssertPipelineAsync("async", nameof(MockBll));
        }

        [Fact]
        public void CanResolvePipeline_FromScopedProvider()
        {
            AssertPipeline("scoped", nameof(MockBll), useScopedProvider: true);
        }

        [Fact]
        public void CanResolvePipeline_MockBllV2()
        {
            AssertPipeline("sync", nameof(MockBllV2), ver: "V2:");
        }

        [Fact]
        public async Task CanResolvePipelineAsync_MockBllV2()
        {
            await AssertPipelineAsync("async", nameof(MockBllV2), ver: "V2:");
        }

        [Fact]
        public void CanResolvePipeline_MockBllV2_FromScopedProvider()
        {
            AssertPipeline("scoped", nameof(MockBllV2), ver: "V2:", useScopedProvider: true);
        }

        private void AssertPipeline(string msg, string key, string? ver = null, bool useScopedProvider = false)
        {
            var bll = useScopedProvider
                ? ScopedServiceProvider.ResolveKey<IMockBll>(key)
                : ServiceProvider.ResolveKey<IMockBll>(key);
            Assert.NotNull(bll);
            var args = new MockEventArgs(msg);

            var result = bll.GetData(this, args);

            Assert.Equal($"{ver}{msg}: {MockDal.MockDataMessage}", result);
        }

        private async Task AssertPipelineAsync(string msg, string key, string? ver = null, bool useScopedProvider = false)
        {
            var bll = useScopedProvider
                ? ScopedServiceProvider.ResolveKey<IMockBll>(key)
                : ServiceProvider.ResolveKey<IMockBll>(key);
            Assert.NotNull(bll);
            var args = new MockEventAsyncArgs(msg);

            var result = await bll.GetDataAsync(this, args);

            Assert.Equal($"{ver}{msg}: {MockDal.MockDataMessage}", result);
        }

    }
}

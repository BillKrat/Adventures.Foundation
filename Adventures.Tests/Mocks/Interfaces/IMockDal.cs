using Adventures.Ioc.Interfaces;

namespace Adventures.Tests.Mocks.Interfaces
{
    public interface IMockDal : IScopedLifetime
    {
        public string GetData(object sender, EventArgs e);

        public Task<string> GetDataAsync(object sender, EventArgs e);
    }

}

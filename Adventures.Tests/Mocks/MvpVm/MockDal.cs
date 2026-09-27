using Adventures.Tests.Mocks.Interfaces;

namespace Adventures.Tests.Mocks.MvpVm
{

    public class MockDal : IMockDal
    {
        public const string MockDataMessage = "Hello World! Mock data from MockDal";
        public string GetData(object sender, EventArgs e)
        {
            return MockDataMessage;
        }

        public Task<string> GetDataAsync(object sender, EventArgs e)
        {
            return Task.FromResult(GetData(sender, e));
        }
    }
}

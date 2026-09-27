using Adventures.Tests.Mocks.Events;
using Adventures.Tests.Mocks.Interfaces;

namespace Adventures.Tests.Mocks.MvpVm
{

    public class MockBll(IMockDal dal) : IMockBll
    {
        public string GetData(object sender, EventArgs e)
        {
            var eTypeName = e.GetType().Name;
            var message = "NOT DEFINED";

            switch (eTypeName)
            {
                case nameof(MockEventAsyncArgs):
                    if (e is MockEventAsyncArgs mockEventAsyncArgs) message = mockEventAsyncArgs.AsyncMessage;
                    break;
                case nameof(MockEventArgs):
                    if (e is MockEventArgs mockEventArgs) message = mockEventArgs.Message;
                    break;
                default:
                    return $"EventArgs {eTypeName} not supported!";
            }
            var dalResult = dal.GetData(sender, e);
            var returnValue = $"{message}: {dalResult}";
            return returnValue;
        }

        public Task<string> GetDataAsync(object sender, EventArgs e)
        {
            return Task.FromResult(GetData(sender, e));
        }
    }
}
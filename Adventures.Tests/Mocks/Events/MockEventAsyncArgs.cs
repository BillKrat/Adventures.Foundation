namespace Adventures.Tests.Mocks.Events
{
    public class MockEventAsyncArgs(string message) : EventArgs
    {
        public string AsyncMessage { get; set; } = message;
    }
}

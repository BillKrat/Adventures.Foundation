namespace Adventures.Tests.Mocks.Events
{
    public class MockEventArgs(string message) : EventArgs
    {
        public string Message { get; set; } = message;
    }
}

using Adventures.Data;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

public class DataModeTests
{
    [Theory]
    [InlineData(null, DataMode.New)]
    [InlineData("", DataMode.New)]
    [InlineData("  ", DataMode.New)]
    [InlineData("New", DataMode.New)]
    [InlineData("legacy", DataMode.Legacy)]
    [InlineData(" Legacy ", DataMode.Legacy)]
    public void The_mode_defaults_to_New_and_ignores_case_and_spaces(string? value, DataMode expected) =>
        Assert.Equal(expected, DataModes.Parse(value));

    [Theory]
    [InlineData("Old")]
    [InlineData("1")]
    [InlineData("99")]
    public void An_unknown_mode_stops_the_start_instead_of_running_the_wrong_store(string value) =>
        Assert.Throws<InvalidOperationException>(() => DataModes.Parse(value));
}

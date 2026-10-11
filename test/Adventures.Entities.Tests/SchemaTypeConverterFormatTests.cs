using System.Globalization;
using Xunit;

namespace Adventures.Entities.Tests;

public sealed class SchemaTypeConverterFormatTests
{
    [Fact]
    public void Format_Date_IsIsoRegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1961-11-19", SchemaTypeConverter.Format(new DateOnly(1961, 11, 19)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("1961-11-19")]
    [InlineData("11-19-1961")]
    [InlineData("11/19/1961")]
    [InlineData("1/5/1961")]
    public void ConvertTo_Date_AcceptsEveryDocumentedForm(string raw)
    {
        var date = (DateOnly)SchemaTypeConverter.ConvertTo(raw, typeof(DateOnly));

        Assert.Equal(1961, date.Year);
    }

    [Fact]
    public void Format_ThenConvertTo_RoundTripsEveryBuiltInType()
    {
        object[] values =
        [
            new DateOnly(2026, 10, 11),
            new DateTimeOffset(2026, 10, 11, 14, 30, 0, TimeSpan.Zero),
            true,
            42,
            12.5m,
            Guid.Parse("123e4567-e89b-12d3-a456-426614174000"),
            "text",
        ];

        foreach (var value in values)
        {
            var text = SchemaTypeConverter.Format(value)!;

            Assert.Equal(value, SchemaTypeConverter.ConvertTo(text, value.GetType()));
        }
    }

    [Fact]
    public void Format_Null_IsNull() => Assert.Null(SchemaTypeConverter.Format(null));

    [Fact]
    public void Format_Bool_IsLowerCase() => Assert.Equal("true", SchemaTypeConverter.Format(true));
}

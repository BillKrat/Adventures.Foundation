using Adventures.Common.Exceptions;
using Xunit;

namespace Adventures.Entities.Tests;

public sealed class EntityInputValidatorTests
{
    private const string SchemaIri = "urn:test:Widget";

    private static readonly EntitySchema Schema = EntitySchema.Create(SchemaIri,
    [
        new EntitySchemaField(SchemaIri, "f-id", "Id", "string", "urn:p:id"),
        new EntitySchemaField(SchemaIri, "f-name", "Name", "string", "urn:p:name", IsRequired: true),
        new EntitySchemaField(SchemaIri, "f-born", "Born", "date", "urn:p:born"),
        new EntitySchemaField(SchemaIri, "f-count", "Count", "int", "urn:p:count"),
    ]);

    private static EntityDataModel Data(params (string Name, string? Value)[] values) =>
        new(string.Empty, values.ToDictionary(v => v.Name, v => v.Value));

    [Fact]
    public void Validate_ValidInput_HasNoErrors()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Name", "Ada"), ("Born", "1990-04-01"), ("Count", "3")), isCreate: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_UnknownField_IsAnError()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Name", "Ada"), ("Role", "Admin")), isCreate: true);

        Assert.Contains("Role", errors.Keys);
    }

    [Theory]
    [InlineData("Born", "not-a-date")]
    [InlineData("Count", "three")]
    [InlineData("Count", "99999999999999999999")]
    public void Validate_ValueOfWrongType_IsAnErrorWithOurOwnMessage(string field, string value)
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Name", "Ada"), (field, value)), isCreate: true);

        var message = Assert.Single(errors[field]);
        Assert.Equal($"{field} is not a valid {Schema.Fields[field].Type}.", message);
    }

    [Fact]
    public void Validate_EmptyOptionalValue_IsNotAnError()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Name", "Ada"), ("Born", ""), ("Count", null)), isCreate: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingRequiredFieldOnCreate_IsAnError()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Born", "1990-04-01")), isCreate: true);

        Assert.Equal("Name is required.", Assert.Single(errors["Name"]));
    }

    [Fact]
    public void Validate_EmptyRequiredFieldOnCreate_IsAnError()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Name", "")), isCreate: true);

        Assert.Contains("Name", errors.Keys);
    }

    [Fact]
    public void Validate_RequiredFieldOmittedOnUpdate_IsNotAnError()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Born", "1990-04-01")), isCreate: false);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IdFieldIsIgnored()
    {
        var errors = EntityInputValidator.Validate(Schema, Data(("Id", "anything"), ("Name", "Ada")), isCreate: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void ThrowIfInvalid_CollectsEveryError()
    {
        var ex = Assert.Throws<InputValidationException>(() =>
            EntityInputValidator.ThrowIfInvalid(Schema, Data(("Born", "x"), ("Count", "y"), ("Nope", "z")), isCreate: true));

        Assert.Equal(["Born", "Count", "Name", "Nope"], ex.Errors.Keys.Order().ToArray());
    }
}

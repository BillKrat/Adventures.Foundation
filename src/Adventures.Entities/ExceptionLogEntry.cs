namespace Adventures.Entities;

/// <summary>One recorded server exception (see <see cref="EntityConstants.ExceptionLog"/>).</summary>
public sealed class ExceptionLogEntry(EntitySchema schema) : DynamicEntity(schema);

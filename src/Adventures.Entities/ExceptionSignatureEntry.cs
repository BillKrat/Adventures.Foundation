namespace Adventures.Entities;

/// <summary>One kind of failure (see <see cref="ExceptionSignature"/>), with how often and when it was seen (see <see cref="EntityConstants.ExceptionSignatureEntity"/>).</summary>
public sealed class ExceptionSignatureEntry(EntitySchema schema) : DynamicEntity(schema);

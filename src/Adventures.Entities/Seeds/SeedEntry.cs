namespace Adventures.Entities;

/// <summary>One named, once-only seed: the file under <c>Sql/seed</c> and the name recorded when it was applied (R2 of the host reuse review). A feature library lists its own; a host orders the lists.</summary>
public sealed record SeedEntry(string File, string Name);

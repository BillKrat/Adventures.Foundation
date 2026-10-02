using Adventures.Entities;

namespace Adventures.Data.NQuad;

/// <summary>
/// Builds strongly-typed <see cref="User"/> instances from raw <see cref="NQuad"/> rows, given
/// an already-built <see cref="EntitySchema"/> (see <see cref="SchemaDal.Load"/>). Each stored
/// field value is paired with the id of the originating NQuad row (<see cref="FieldValue"/>) so
/// update/delete operations can target that exact row later - the raw value alone is not enough.
/// </summary>
public static class NQuadUserAdapter
{
    /// <summary>
    /// Materializes every distinct User subject found in <paramref name="quads"/> into a
    /// <see cref="User"/>. The Id field is derived from the subject IRI itself
    /// (e.g. ".../id/user/{guid}") since the seed data does not emit a separate identifier
    /// triple for it; every other field is populated from the quad whose predicate matches
    /// the schema's field mapping, with the owning quad's Id preserved via FieldValue.
    /// </summary>
    public static IEnumerable<User> LoadUsers(IEnumerable<Adventures.Data.NQuad.NQuad> quads, EntitySchema schema)
    {
        var all = quads as Adventures.Data.NQuad.NQuad[] ?? quads.ToArray();
        var userSubjects = all
            .Where(q => q.Subject.StartsWith(EntityConstants.User.BaseIri, StringComparison.Ordinal))
            .Select(q => q.Subject)
            .Distinct(StringComparer.Ordinal);

        foreach (var subject in userSubjects)
        {
            var user = new User(schema);
            var idValue = subject[EntityConstants.User.BaseIri.Length..];
            user.Set(DynamicEntity.EntityIdPropertyName, subject, idValue);

            foreach (var quad in all.Where(q => q.Subject == subject))
            {
                if (schema.FieldsByPredicate.TryGetValue(quad.Predicate, out var field))
                {
                    user.Add(field.Name, quad.Id.ToString(), quad.Object);
                }
            }

            yield return user;
        }
    }
}


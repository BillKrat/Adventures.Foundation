using Adventures.Common.Exceptions;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>One address belongs to one entry: a slug made from the title gets a numeric suffix, a typed duplicate is refused naming its holder.</summary>
public sealed class SlugRulesTests
{
    private static (string Id, string Slug, string Title) Entry(string id, string slug, string title) => (id, slug, title);

    [Fact]
    public void AGeneratedSlug_GetsTheNextFreeNumber()
    {
        var others = new[] { Entry("1", "my-post", "My post"), Entry("2", "my-post-2", "My post"), Entry("3", "other", "Other") };

        Assert.Equal("my-post-3", SlugRules.Resolve(null, "My post", null, null, others));
        Assert.Equal("fresh", SlugRules.Resolve("  ", "Fresh", null, null, others));
        Assert.Equal("MY-POST-3", SlugRules.Resolve(null, "My post", null, null, others).ToUpperInvariant());
    }

    [Fact]
    public void ATypedDuplicate_IsRefusedNamingTheHolder_IgnoringCase()
    {
        var others = new[] { Entry("1", "about", "About us") };

        var refused = Assert.Throws<BusinessRuleException>(() => SlugRules.Resolve("About", "Something else", null, null, others));

        Assert.Contains("\"About\"", refused.Message);
        Assert.Contains("About us", refused.Message);
        Assert.Equal("about-2", SlugRules.Resolve("about-2", "x", null, null, others));
    }

    [Fact]
    public void AnEntryDoesNotClashWithItself_AndAnUnchangedOldDuplicateStaysSavable()
    {
        var others = new[] { Entry("1", "same", "First"), Entry("2", "same", "Second") };

        Assert.Equal("same", SlugRules.Resolve("same", "Second", "2", "same", others));     // unchanged: allowed even though entry 1 also has it
        Assert.Throws<BusinessRuleException>(() => SlugRules.Resolve("same", "Second", "2", "different", others));   // changed to a taken one: refused
        Assert.Equal("unique", SlugRules.Resolve("unique", "Second", "2", "same", others));
        Assert.Equal("first", SlugRules.Resolve(null, "First", "1", "same", [Entry("1", "first", "First")]));   // its own slug does not count as taken
    }
}

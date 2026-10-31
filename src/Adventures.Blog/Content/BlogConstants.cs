namespace Adventures.Blog;

/// <summary>Seed names, graphs and IRIs of the blog content model; the seeds themselves ship in <c>Adventures.Data.NQuad</c> under <c>Sql/seed</c> until the Data split (S5).</summary>
public static class BlogConstants
{
    /// <summary>The site settings (title, tagline, posts per page, legacy time zone): named rows an administrator edits; defined in <c>Sql/seed/seed-011-site-settings.nq</c>.</summary>
    public static class Site
    {
        public const string SeedName = "011-site-settings";
        public const string DefaultGraph = "https://global-webnet.com/graph/site";
        public const string SettingSchemaIri = "https://schema.global-webnet.com/site/Setting";
        public const string SettingBaseIri = "https://global-webnet.com/id/site-setting/";
        public const string SettingTypeIri = "https://global-webnet.com/ontology/site#Setting";
    }

    /// <summary>
    /// The content model (pages, posts, categories, tags), new entities under the schema host convention; shipped by
    /// <c>Sql/seed/seed-006-content.nq</c> and modelled on what the BlogEngine admin edits (see the review in ai-research-blog docs).
    /// </summary>
    public static class Content
    {
        public const string SeedName = "006-content";

        /// <summary>Seed 007 adds the Categories and Tags fields to Post (seed 006 had been applied; a named seed applies once).</summary>
        public const string TaxonomySeedName = "007-post-taxonomy";

        /// <summary>Seed 008 adds RevisionOf to Post: a draft that proposes changes to a published post.</summary>
        public const string RevisionSeedName = "008-post-revision";

        /// <summary>Seed 013 adds Approval, ApprovedBy and ApprovedAt to Post: the review state of a collaboration post, changed only by the server (the approve actions).</summary>
        public const string ApprovalSeedName = "013-post-approval";
        public const string DefaultGraph = "https://global-webnet.com/graph/content";
        public const string SchemaBase = "https://schema.global-webnet.com/content/";
        public const string IdBase = "https://global-webnet.com/id/";
        public const string OntologyBase = "https://global-webnet.com/ontology/content#";

        public const string PageSchemaIri = SchemaBase + "Page";
        public const string PageBaseIri = IdBase + "page/";
        public const string PageTypeIri = OntologyBase + "Page";

        public const string PostSchemaIri = SchemaBase + "Post";
        public const string PostBaseIri = IdBase + "post/";
        public const string PostTypeIri = OntologyBase + "Post";

        public const string CategorySchemaIri = SchemaBase + "Category";
        public const string CategoryBaseIri = IdBase + "category/";
        public const string CategoryTypeIri = OntologyBase + "Category";

        public const string TagSchemaIri = SchemaBase + "Tag";
        public const string TagBaseIri = IdBase + "tag/";
        public const string TagTypeIri = OntologyBase + "Tag";
    }

}

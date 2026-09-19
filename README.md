# Adventures.Foundation

Reusable .NET class libraries shared across "Adventures.*"-family projects — extracted from
[`ai-research-blog`](https://github.com/BillKrat/ai-research-blog) on 2026-09-19 because neither
library is actually specific to that project. Naming convention: **`Adventures.*`** for anything
meant to be reused across projects, **`<Product>.*`** (e.g. `AiBlogResearch.*`) for code specific
to one product. If you're building a new project that needs auth or a generic multi-tenant data
store, start here instead of re-implementing it.

## Packages

- **`Adventures.Security`** (`src/Adventures.Security/`) — self-hosted JWT issuing/validation,
  OAuth2 client-credentials (M2M) auth for service-to-service calls, scope-based authorization
  policies, and PBKDF2-HMAC-SHA256 password hashing for human users. No external identity
  provider required. See the XML doc comments on `PasswordHasher` vs. `ClientSecretHasher` for why
  user passwords and machine secrets deliberately use different hashing strategies — this is a
  hard security requirement, not a style choice; don't unify them to save a step.
- **`Adventures.Data`** (`src/Adventures.Data/`) — generic, tenant/org-scoped entity storage over
  PostgreSQL using a **JSONB Hybrid + W3C N-Quads** model: known/structured fields live in an
  indexable JSONB column (`IEntityRepository`), fully user-defined/extension fields live as
  N-Quad-style triples scoped by a `graph` value (`ITripleStore`). This is what lets an app built
  on this library support user-designed custom fields/forms per tenant/org/user without schema
  migrations. `Sql/schema.sql` has the DDL.

Both are plain class libraries with no web framework dependency beyond what's needed for JWT
bearer validation (`Adventures.Security` only) — safe to reference from an ASP.NET Core host, a
console app, an Azure Function, etc.

## Why extracted, not just referenced in place

`ai-research-blog` is an open-source, cost-conscious "AI starter kit" (see its own
`docs/SESSION_HANDOFF.md` for that project's full rationale, e.g. why it rejected Auth0's M2M
free-tier quota). A second, separate Angular/backend app — a security-provider/admin UI mirroring
what Auth0's own dashboard does, for managing tenants/orgs/users/M2M clients — needs the *same*
security and data foundation without depending on `ai-research-blog`'s source tree. Packaging
these as independently-versioned libraries is what makes that possible without cross-repo
`ProjectReference` hacks.

## Consuming this from another repo

**During active co-development** (this repo and a consumer changing together, same machine): pack
and push to a local folder-based NuGet feed rather than publishing a real version for every
iteration.

```bash
dotnet pack src/Adventures.Security -c Release -o ../local-nuget-feed
dotnet pack src/Adventures.Data -c Release -o ../local-nuget-feed
```

A consumer repo's `nuget.config` should add that folder as a package source (see
`ai-research-blog/nuget.config` for the pattern already wired up there).

**Once a version is stable enough to depend on for real**: publish to nuget.org (public, free, no
auth needed to *consume* — deliberately not GitHub Packages, since GitHub Packages requires
authentication to restore even public packages, which would force anyone cloning
`ai-research-blog` to configure a PAT just to build it. That contradicts the whole point of a
"starter kit," so nuget.org is the actual target feed, not a stopgap). Publishing requires a
`NUGET_API_KEY` repository secret to be configured before the `publish` GitHub Actions job (see
`.github/workflows/build.yml`) will do anything beyond build+test — not yet set up as of this
repo's creation.

## Versioning

Bump `<Version>` in each project's `.csproj` independently — `Adventures.Security` and
`Adventures.Data` don't have to move in lockstep, they just happen to live in one repo for now
because they're evolving at the same pace during early development. Split into separate repos
later if that stops being true; don't force it prematurely.

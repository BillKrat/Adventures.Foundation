# Adventures.Foundation

**The core of an AI starter kit for developers.** The mission is to give every developer a working
foundation for building with AI, so one person can run something with the potential to succeed for less
than $35 a month, without depending on expensive identity, data or AI service providers.

> **What the money buys, and from whom.** We do not provide hosting or AI services. The figures are what
> the other parties charge: shared Windows hosting from [SmarterASP.NET](https://www.smarterasp.net)
> (about $38 per quarter, billed by SmarterASP.NET) and an AI subscription from the provider of your
> choice (about $20 a month).

The `Adventures.*` libraries here are the meat of that kit.
[`ai-research-blog`](https://github.com/BillKrat/ai-research-blog) is a single, complete blog application
that shows how to use them: Angular client, ASP.NET Core API and an MCP server on top of these libraries.
Naming convention: **`Adventures.*`** for anything meant to be reused across products, **`<Product>.*`**
(for example `AiBlogResearch.*`) for code specific to one product. If you are building a project that
needs sign-in, a multi-tenant data store, a plug-in seam or a tool surface for AI clients, start here
instead of re-implementing it. The libraries were extracted from `ai-research-blog` on 2026-09-19.

## The libraries (`src/`)

Each feature is wired into a host by one `AddXxx()` call, and `test/Adventures.Architecture.Tests` pins the
reference graph so the layers cannot drift. Matching xUnit projects live in `test/`.

**Foundations**
- **`Adventures.Common`** and **`Adventures.Ioc`**: base types, diagnostics, and the dependency-injection
  scan that registers services by lifetime.
- **`Adventures.Entities`**: a storage-agnostic dynamic entity model (`IEntityBll`, `IEntityDal`,
  `IEntityRepository<T>`), with **`Adventures.Entities.Identity`** (users, roles, rights, tenants, first-run
  site creation, e-mail verification) and **`Adventures.Entities.Diagnostics`** (exception log, signatures).
- **`Adventures.Security`**: framework-free JWT issuing and validation, OAuth2 client-credentials (M2M)
  sign-in for service-to-service calls, scope policies, AES-256-GCM field encryption, and
  PBKDF2-HMAC-SHA256 password hashing for people. **`Adventures.Security.AspNetCore`** is the ASP.NET wiring.
  People's passwords and machine secrets deliberately use different hashers (`PasswordHasher` and
  `ClientSecretHasher`): a hard security requirement, so do not unify them.
- **`Adventures.Identity`** (+ `.Postgres`): the login flow, a credential vault with lockout and disabled
  status, password change and recovery. Tokens carry the user's id and tenant, never a secret.

**Data**
- **`Adventures.Data`** (contracts), **`Adventures.Data.Postgres`** and **`Adventures.Data.Sql`** (a legacy
  SQL Server mode).
- **`Adventures.Data.NQuad`** and **`Adventures.Data.NQuad.Postgres`**: a W3C N-Quads store behind
  `INQuadStore` (in memory for tests, Npgsql for real), with two Postgres layouts, a plain text table and a
  hybrid dictionary layout that is about half the size. A repository gives each tenant its own graph, so a
  request can only read and write its tenant's data.

**Web**
- **`Adventures.WebApi`**: controller bases, correlation ids, the diagnostics probes, exception handling,
  request hardening (fallback authorization, rate limits, security headers, trusted proxies), tenancy
  (the host picks the tenant, CORS from the registered sites) and the one-setting site domain.
- Controller libraries, each added with one call: **`Adventures.Identity.WebApi`** (sign-in, users, roles,
  setup, password recovery), **`Adventures.Diagnostics.WebApi`** (health, exceptions, client errors, MCP
  diagnostics), **`Adventures.Collaboration.WebApi`**, **`Adventures.Blog.WebApi`** (public reader, admin
  content, comments, languages, widgets) and **`Adventures.Tools.WebApi`** (the tool manifest, `GET /api/tools`).

**Features**
- **`Adventures.Blog`** (+ `.Postgres`, `.Legacy`): posts, pages, categories, tags, visitor comments with
  approval, HTML and Markdown content, site settings, language files, widgets, the default-on HTML sanitiser.
- **`Adventures.Collaboration`**: the review channel between a person and an AI (developer and research
  items, a conversation, recorded decisions).
- **`Adventures.MEF`** and **`Adventures.MEF.WebApi`**: the plug-in seam. Discovery from the host and an
  approved plug-in folder, per-tenant switches and settings, an in-process event bus, widgets, and the rules
  for plug-in routes and tools.
- **`Adventures.Tools`**: `IAppTool` (name, description, input schema, scope, route), a selector chain with a
  fallback, and the catalog an MCP server reads. It has no dependencies.
- **`Adventures.Client`**: a desktop and console client (sign-in session, remote presenter, view models) used by
  the WPF app in `ai-research-blog`.

## The schema browser (`apps/`)

Applications built only from these libraries: browse the schemas (the shape of the data, never the data) of the sites you have access to.

| Application | What it is |
|---|---|
| `Adventures.Browser.Host` | The browser API: forwards sign-in and read-only schema requests to the sites its settings list. No database, users or secrets |
| `Adventures.Browser.Desktop` | Windows (WPF) client |
| `Adventures.Browser.Web` | Blazor WebAssembly client, a static site |

Both clients share one set of view models (`Adventures.Browser.Client`). A site offers the browser by switching on `Browser:Enabled`; a person needs the `schema.read` right there.

```powershell
dotnet run --project apps/Adventures.Browser.Host
```

```powershell
dotnet run --project apps/Adventures.Browser.Web
```

## Consuming this from another repo

Consume the libraries by **project reference** from a sibling clone, as `ai-research-blog` does from its
solution file, or pack them to a **local folder feed** for your own use:

```bash
dotnet pack src/Adventures.Security -c Release -o ../local-nuget-feed
```

and add that folder as a package source in your `nuget.config`. We do not publish current versions to a
public package registry. Earlier versions that were published remain under the terms they were released
with, and are superseded by the source here. Keep this repository and its consumer on matching branch names.

## Build and test

```bash
dotnet build Adventures.Foundation.slnx
dotnet test
```

`Adventures.Data.NQuad.Tests` also has live-Postgres tests that need a `ConnectionStrings:Postgres`
user-secret; everything else runs offline. A GitHub Actions build runs on every push.

## Not for regulated use

This software is a general-purpose starter kit. It is **not designed, validated or licensed for regulated use**: health care and life sciences (including clinical or other human-subjects research), financial services, safety-critical or critical-infrastructure systems, government or defence compliance regimes, or any setting that needs a validated system, an audit trail, electronic signatures or a compliance certification. It has no regulatory approval and makes no compliance claim, and it keeps no audit trail. Production use in a regulated field is outside the licence, and no commercial licence is offered for it. We also do not accept contributions whose purpose is to make it usable in a regulated environment (see [CONTRIBUTING.md](CONTRIBUTING.md)), so please do not spend effort on that. The exact terms are in [NOTICE](NOTICE) (Regulatory disclaimer and field of use) and the Additional Use Grant in [LICENSE](LICENSE).

## License

Source-available under the Business Source License 1.1, with an additional use grant for small operators, changing to AGPL-3.0 after four years. Read [LICENSE](LICENSE) and [NOTICE](NOTICE) for the exact terms; they govern, not this summary.

# Decision note: exception handling direction (not built yet)

Recorded 2026-09-26 from the human's description, so it is not lost. Status: **direction only**; a separate concern to design together with Claude, after the store and DynamicEntity CRUDL layers. Nothing here is implemented.

## How the store layer relates
Store operations (`INQuadStore`) simply throw when they fail (for example a duplicate id). The contract tests deliberately do **not** pin the exception type; this design will define it. Exception handling is not the store's job.

## Intended flow (10,000-foot view)
1. An exception handler inspects the exception's **signature** (for example `Exception.Message`) and looks it up.
2. **Known signature:** reuse its existing Exception Id.
3. **Unknown signature:** create an `ErrorType : DynamicEntity` whose N-Quad schema holds a minimal set of signature fields (enough to group like exceptions), and use its new Exception Id.
4. Log the occurrence as an `ErrorEx : DynamicEntity`, tied to that Exception Id.
5. The exception is **rethrown** and captured at the BLL, which turns it into a proper message for the UI.

## `ErrorEx` schema (initial field list, not limited to)
| Field | Notes |
|---|---|
| Exception Id | UUID; identifies the exception type/signature (from step 2 or 3) |
| Timestamp | ISO 8601, UTC |
| Trace ID | token carried through the whole request chain; links the exception to a specific user request or API call |
| Error Type / Class | |
| Error Message | |
| Stack Trace | |
| Inner Exception | recursive |
| Application / Service Name | |
| Environment | DEV for now |
| Machine / Host Name | |
| App Version | |
| Web: Method and URL | |
| Web: User ID | |

## Open questions for the design session
- What makes up the "minimal signature" (message text, type, top stack frame), and how to keep messages that embed ids or values from splintering into many signatures.
- Where the handler lives (BLL boundary versus middleware) and how Trace ID is created and propagated.
- Redaction: messages, URLs and stack traces can carry personal data or secrets; decide what is stored and how it is protected.
- Failure of the logger itself (the handler must never mask the original exception).
- Depends on the DynamicEntity CRUDL layer, since `ErrorType` and `ErrorEx` are `DynamicEntity` classes.

# WhatYouSay

Entities, `WhatYouSayContext`, migrations, `Auth`, seed data. General rules are in the root [AGENTS.md](../AGENTS.md).

Nothing here may reference the web project or `HttpContext`. A service that needs one belongs in `WhatYouSay.Web`.

## Services

Plain classes taking `WhatYouSayContext`, registered scoped. No interfaces and no mocking: the tests run against real SQLite, so an abstraction here would only be indirection.

Throw `InvalidOperationException` for a rule a caller could have checked, and `SummaryGroundingException` for a grounding failure — it carries every problem found in one pass so a caller fixing them needs one retry rather than one per mistake.

## Invariants that span files

- **A citation is located in a response's current revision, frozen or not.** Freezing is a separate admin choice, and closing a topic freezes nothing.
- **A summary's `Revision` moves on with every write to its overview or tree, and nothing else.** It is a concurrency token, so a write saved over one it never saw fails instead of replacing it.
- **A summariser write archives the nodes it leaves out; only a person deletes one.** Every read of a current tree filters out archived nodes at the include, as withdrawn responses are filtered, and anything that loads them for display does so untracked.
- **Only the live summary is both published and open to the summariser.** A topic has at most one, the database enforces that, and it stays published while it is live.
- **A response's wording and timing live only in its revisions.** `Response.Revise` adds a revision for each changed wording, and nothing changes a revision afterwards. A reference names one revision, and its quote and offsets index that revision's body. Every read of a revision is filtered on a live response, so withdrawing a response hides all of its wordings together.
- **`Response.Id` is a v4 random Guid, never `CreateVersion7`.** A v7 Guid embeds a timestamp, and topics that record no timing order by `Id` — a time-ordered one would reconstruct submission order and roughly when each person answered, walking straight past the decision not to store a time.
- **Every stored time follows the topic's timing setting, not its identity.** A topic that records no timing stores null in every `CreatedAt` it owns: revisions, comments and reactions. Decide with `Topic.RecordsTiming`, never `IsAnonymous`.
- **One responder token owns every response a browser gives to a topic**, and the same token keys that browser's comments and reactions. Withdrawing one response leaves the token's other responses, comments and reactions alone.
- Enums are stored as strings and `DateTimeOffset` as UTC ISO-8601 text. Both exist so the file reads by hand in `sqlite3`, and the second so SQLite can `ORDER BY` it.

## Migrations

Checked in, applied with `Migrate()` on startup. Adding one:

```bash
dotnet ef migrations add <Name> --project WhatYouSay --startup-project WhatYouSay.Web
```

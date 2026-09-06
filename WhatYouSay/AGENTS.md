# WhatYouSay

Entities, `WhatYouSayContext`, migrations, `Auth`, seed data. General rules are in the root [AGENTS.md](../AGENTS.md).

Nothing here may reference the web project or `HttpContext`. A service that needs one belongs in `WhatYouSay.Web`.

## Services

Plain classes taking `WhatYouSayContext`, registered scoped. No interfaces and no mocking: the tests run against real SQLite, so an abstraction here would only be indirection.

Throw `InvalidOperationException` for a rule a caller could have checked, and `SummaryGroundingException` for a grounding failure — it carries every problem found in one pass so a caller fixing them needs one retry rather than one per mistake.

## Invariants that span files

- **A reference may only be created against a frozen response.** Closing a topic freezes every response then in it and the freeze never lifts, so quoting cannot rot underneath a summary. Reopening is free.
- **`Response.Id` is a v4 random Guid, never `CreateVersion7`.** A v7 Guid embeds a timestamp, and anonymous topics order by `Id` — a time-ordered one would reconstruct submission order and roughly when each person answered, walking straight past the decision not to store `CreatedAt`.
- Enums are stored as strings and `DateTimeOffset` as UTC ISO-8601 text. Both exist so the file reads by hand in `sqlite3`, and the second so SQLite can `ORDER BY` it.

## Migrations

Checked in, applied with `Migrate()` on startup. Adding one:

```bash
dotnet ef migrations add <Name> --project WhatYouSay --startup-project WhatYouSay.Web
```

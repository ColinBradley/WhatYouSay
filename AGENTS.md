# WhatYouSay

Blazor Server, EF Core, SQLite. Design and build order: [PLAN.md](PLAN.md).

## Projects

| Project | Contents |
|---|---|
| `WhatYouSay` | Entities, `WhatYouSayContext`, migrations, `Auth`, seed data |
| `WhatYouSay.Web` | Blazor Server front end and the summariser REST API |
| `WhatYouSay.Tests` | References `WhatYouSay` only, never the web project |

## Code style

`.editorconfig` enforces the mechanical parts as build errors via `EnforceCodeStyleInBuild`.

- Blank line between every property.
- Prefix instance members with `this.` — but not fields.
- Instance fields take an `m` prefix: `private bool mIsDisposed;`
- Static fields take an `s` prefix: `private static readonly FrozenSet<string> sNames = ["x"];`
- `const` stays PascalCase, unprefixed.
- Expression bodies suit a single value or a single call. Anything longer — a chained LINQ query especially — gets braces. Judgement, not enforced.
- An expression body always starts on the line after the `=>`. No exceptions.
- A wrapped parameter list closes on its own line, at the declaration's indent.
- Object initializers keep the constructor parentheses: `new Thing() { ... }`, never `new Thing { ... }`.
- Trailing commas everywhere they are legal: object, collection and array initializers, collection expressions, switch expressions, enums.
- Prefer `required` properties with `init` over constructor parameters, including on records. Positional records get miswired silently when several parameters share a type.
- Don't manually wrap text in md files.

## UI

- **Disable, don't hide.** A control someone cannot use stays on the page, disabled, with a `title` on the control itself saying why. Hiding it leaves people wondering whether the feature exists.
- Component code lives in a `.razor.cs` partial class, not an `@code` block. Only leave code inline when it is a line or two.

## Comments

Comment the non-obvious **why**: a constraint, a footgun, a decision the next person would otherwise undo. Nothing else.

Delete a comment if it does any of these:

- Restates what the code already says.
- Justifies a convention already written down here.
- Narrates the decision — alternatives weighed, what was considered and rejected, why one approach beats another.
- Editorialises: "worth knowing", "earns its place", "unusually well placed".

A doc comment on a type or member is for someone calling it, not for someone reviewing the choice to write it. If it reads as reasoning rather than as information needed to change the code safely, it goes.

## Packages

All versions live in `Directory.Packages.props`. Projects carry bare `PackageReference` entries with no `Version` attribute.

## Telemetry

**One `ActivitySource` per assembly**. Start spans with the `Start()` extension.

## Tests

- MSTest on Microsoft.Testing.Platform. `global.json` opts into MTP mode for `dotnet test`.
- MTP flags, not VSTest flags: `--report-trx` not `--logger trx`, `--no-banner` not `--nologo`, `--coverage` not `--collect`. A VSTest flag is forwarded to the test app unrecognised and fails the run as "Zero tests ran", exit 5.
- `Parallelize(Scope = ExecutionScope.MethodLevel)`, so every test must be parallel-safe: no shared database, no shared static state, no ordering dependencies.
- Real SQLite and real migrations. No mocking, no UI tests.

## Commands

```bash
dotnet test
```

```bash
dotnet run --project WhatYouSay.Web
```

```bash
dotnet ef migrations add <Name> --project WhatYouSay --startup-project WhatYouSay.Web
```

## Development data

Seeded on startup: four surveys, admin password `letmein`, summariser tokens `dev-retro`, `dev-lunch`, `dev-company`, `dev-diary`.

The summariser API names the survey in the path and takes the token in an `Authorization: Bearer` header, so the two vary independently. Start at `/api/surveys/{code}/ai-summary-start`, which returns the whole job as plain text.

```bash
curl -H "Authorization: Bearer dev-retro" http://localhost:5286/api/surveys/spr47ab/ai-summary-start
```

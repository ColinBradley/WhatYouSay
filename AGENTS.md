# WhatYouSay

Blazor Server, EF Core, SQLite. Design and build order: [PLAN.md](PLAN.md).

## Projects

| Project | Contents |
|---|---|
| `WhatYouSay` | Entities, `WhatYouSayContext`, migrations, `Auth`, seed data |
| `WhatYouSay.Web` | Blazor Server front end; later the MCP endpoint |
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
- Prefer `required` properties with `init` over constructor parameters, including on records. Positional records get miswired silently when several parameters share a type.
- Don't manually wrap text in md files.

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

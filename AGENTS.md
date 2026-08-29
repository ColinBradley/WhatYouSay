# WhatYouSay

Blazor Server, EF Core, SQLite. Design and build order: [PLAN.md](PLAN.md).

## Projects

| Project | Contents |
|---|---|
| `WhatYouSay` | Entities, `WhatYouSayContext`, migrations, `Auth`, seed data |
| `WhatYouSay.Web` | Blazor Server front end; later the MCP endpoint |
| `WhatYouSay.Tests` | References `WhatYouSay` only, never the web project |

## Code style

- Blank line between every property.
- Prefix instance members with `this.` — but not fields.
- Instance fields take an `m` prefix: `private bool mIsDisposed;`
- Static fields take an `s` prefix: `private static readonly FrozenSet<string> sNames = ["x"];`
- `const` stays PascalCase, unprefixed.

## Packages

All versions live in `Directory.Packages.props`. Projects carry bare `PackageReference`
entries with no `Version` attribute.

## Tests

- xUnit v3 on Microsoft.Testing.Platform.
- `ParallelMode.All` is on, so every test must be parallel-safe.
- Pass `Cancellation` to every async call.
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

Seeded on startup: four surveys, admin password `letmein`, summariser tokens `dev-retro`,
`dev-lunch`, `dev-company`, `dev-diary`.

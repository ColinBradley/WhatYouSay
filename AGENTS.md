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
- Favour immutable types wherever they fit: `record` for data, `init` over `set`, `readonly` fields, `ImmutableArray<T>` or `FrozenSet<T>` for static tables, `IReadOnlyList<T>` on anything a caller should not mutate. EF entities and Blazor `[SupplyParameterFromForm]` / `[Inject]` properties are the exceptions — both need settable properties.
- Don't manually wrap text in md files.

## Analyzers

Address every analyzer diagnostic, including `Info`/suggestion-level ones that don't fail the build (`MSTESTxxxx`, `CAxxxx`, `IDExxxx`). Take the suggested fix rather than suppressing it. Note that .editorconfig isn't fully fleshed out and so if a suggestion doesn't make sense and isn't explicitly decided on, query with the user.

## UI

- **Disable, don't hide.** A control someone cannot use stays on the page, disabled, with a `title` on the control itself saying why. Hiding it leaves people wondering whether the feature exists.
- **The exception is a dense set of repeated controls**, where one control per item across a long list buries the content it acts on. Those get `.on-demand` inside a `.hoverable`, which reveals on hover *and* on focus-within, and unconditionally where hover does not exist. `.hoverable` wraps the item's own head and never its children or its comment box, or crossing the gap between two children lights the parent up and a caret in a box holds it open. Anything the group produced — a reaction count above zero — is data rather than a control, and stays visible.
- Component code lives in a `.razor.cs` partial class, not an `@code` block. Only leave code inline when it is a line or two.
- Static SSR and form posts everywhere except `SummaryEditor`, the one `InteractiveServer` component. Its page is a static shell that does the `AdminSession` check, and it opens a DI scope per operation.

## CSS

No framework. `wwwroot/app.css` holds role-named tokens (`--control-padding`, `--panel-gap`), a small set of composable classes that read them, and base element styles. A component composes by overriding a token in its own scope, never by inventing a size.

- **A parent spaces its children.** `gap` on the container, never `margin` on the child to push the next one away — the only margins left are `margin: 0` resets and `margin-left: auto`, which is alignment rather than spacing.
- Sizes are `em`, so a scope that sets `font-size` rescales everything inside it. Set `font-size` only at deliberate anchors — never on a container that can contain itself, or it compounds through the recursion.
- Hairlines are `px`; a fractional `em` border rounds away at some zoom levels.
- Light and dark come from `light-dark()` against `color-scheme: light dark`. One declaration per token, no media query, no second block.
- Shared classes and tokens live in the global sheet. `.razor.css` is for genuinely component-private layout only, since scoped CSS cannot style a shared class without `::deep`.
- `App.razor` sets `<base href="/">`, so a bare `#fragment` href resolves against the base URL and navigates away. In-page anchors carry the full path. Links doing a same-document jump also need `data-enhance-nav="false"`, or Blazor patches the DOM and `:target` never matches.

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

Seeded on startup: four topics, admin password `letmein`, summariser tokens `dev-retro`, `dev-lunch`, `dev-company`, `dev-diary`. The retro carries a published summary with reactions on it, including two objections, so the admin editor has something to show.

The summariser API names the topic in the path and takes the token in an `Authorization: Bearer` header, so the two vary independently. Start at `/api/topics/{code}/ai-summary-start`, which returns the whole job as plain text.

```bash
curl -H "Authorization: Bearer dev-retro" http://localhost:5286/api/topics/spr47ab/ai-summary-start
```

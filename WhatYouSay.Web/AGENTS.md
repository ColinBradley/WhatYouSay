# WhatYouSay.Web

Blazor Server front end and the summariser REST API. General rules are in the root [AGENTS.md](../AGENTS.md).

## UI

- **Disable, don't hide.** A control someone cannot use stays on the page, disabled, with a `title` on the control itself saying why. Hiding it leaves people wondering whether the feature exists.
- **The exception is a dense set of repeated controls**, where one control per item across a long list buries the content it acts on. Those get `.on-demand` inside a `.hoverable`, which reveals on hover *and* on focus-within, and unconditionally where hover does not exist. Hidden means transparent, never `display: none` — a removed control leaves the tab order, and then focus-within can never fire to reveal it. Keep it out of the flow by position instead, so hiding still costs no space. `.hoverable` wraps the item's own head and never its children or its comment box, or crossing the gap between two children lights the parent up and a caret in a box holds it open. Anything the group produced — a reaction count above zero — is data rather than a control, and stays visible.
- Component code lives in a `.razor.cs` partial class, not an `@code` block. Only leave code inline when it is a line or two.
- Static SSR and form posts by default. `InteractiveServer` when refreshing the page is a bad idea.

## Interactive components

An interactive component sits inside a static shell, because a circuit has no response to set headers on: anything cookie- or auth-shaped happens in the shell before the circuit starts.

- Parameters are ids and primitives, never entities. The component loads its own data.
- Open a DI scope per operation. A scoped `DbContext` otherwise lives as long as the circuit and answers later reads from what it first tracked.
- Reaching a parent through a cascaded value is an ordinary method call the framework never sees, so the parent needs an explicit `StateHasChanged` or its own rendering will not move.
- `SummaryLiveUpdates` pushes a change to every circuit reading the same summary. A write publishes rather than refreshing itself, so the writer's screen updates by the same path as everyone else's.

## CSS

No framework. `wwwroot/app.css` holds role-named tokens (`--control-padding`, `--panel-gap`), a small set of composable classes that read them, and base element styles. A component composes by overriding a token in its own scope, never by inventing a size.

- **A parent spaces its children.** `gap` on the container, never `margin` on the child to push the next one away — the only margins left are `margin: 0` resets and `margin-left: auto`, which is alignment rather than spacing.
- Redefining a token on a page wrapper inherits it into every nested scope. Where only the page's own rhythm is meant, set `gap` directly: that is what `.sections` is for.
- Sizes are `em`, so a scope that sets `font-size` rescales everything inside it. Set `font-size` only at deliberate anchors — never on a container that can contain itself, or it compounds through the recursion.
- Hairlines are `px`; a fractional `em` border rounds away at some zoom levels.
- An author `display` outranks the user agent's `[hidden]` rule, so `app.css` forces it. Without that, `element.hidden = true` does nothing to anything styled here.
- Light and dark come from `light-dark()` against `color-scheme: light dark`. One declaration per token, no media query, no second block.
- Shared classes and tokens live in the global sheet. `.razor.css` is for genuinely component-private layout only, since scoped CSS cannot style a shared class without `::deep`.
- `App.razor` sets `<base href="/">`, so a bare `#fragment` href resolves against the base URL and navigates away.

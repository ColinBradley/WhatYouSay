# What You Say

Namespace / app name: `WhatYouSay`. Product name: **What You Say**.

A small, self-hosted tool for collecting free-text feedback from a group of people who trust each other, then making sense of it with an AI agent. Built for sprint retros, dev cycle reviews and architecture feedback at work. Not a service, not multi-tenant, no user accounts.

> **Status: building.** Steps 1–7 complete. The tree is stored, validated, served and rendered, and the drafting runs step 7 exists to force turned up an answer worth having: depth carries meaning, and [typed nodes did not](#node-kinds-tried-removed). A node is text, references and children. Next is step 8, the summary edit UI.

## Principles

- **One survey = one free-text prompt.** Title + Description *is* the question. There are no fields, no sub-questions, no form builder. Ever.
- **Quick and dirty, but faithful.** The whole value is in the summary being a true reflection of what people actually wrote. Fidelity is the thing to protect. Anonymity is a supported option, not the point of the tool.
- **The agent drafts, the human publishes.** AI never gets the last word.
- **Every claim is grounded in a real quote, and the app proves it.** A node that can't be traced to what someone actually wrote doesn't get stored. A heading is exempt only because the requirement lands on what hangs below it: every branch has to end in a citation. See [Grounding](#grounding-the-app-validates-the-agent).
- **The group gets to answer back.** A summary nobody can object to is just one person's reading with extra steps. Responders react to the nodes, including flagging ones that misrepresent them.
- **No vocabulary from one use of it.** The tool is for sprint retros, dev cycle reviews, holiday planning, takeaway votes and one-person diaries. The app's own words — labels, headings, entity names, endpoints, error text — never name any one of those. Example *content* is the exception, and earns it by coming as a varied set rather than a single case: the `/new` placeholders rotate per page load through a retro, a lunch vote, a deploy-process review, a trip, a reading group and a solo diary, so no one visit reads as what the tool is for. Vocabulary is what silently narrows a tool to the first thing it was used for.
- **The responses are the precious data.** Summaries are derived: a version can be deleted and redrawn from the responses at any time. That is what makes the summary model safe to change — [getting the node vocabulary wrong](#node-kinds-tried-removed) cost a migration and a re-run, not anyone's words. It has already been cashed in once.
- No frills. If a feature needs a design doc, it's deferred.

## Why not just paste the responses into a chat?

Worth answering explicitly, because if there isn't a good answer this shouldn't exist. Four things a chat session structurally cannot do:

1. **Grounded quotes.** A chat summary can't be checked. This one can't misattribute, because the app validates every quote against the response it claims to come from.
2. **It has a URL, versions and history.** Chat output is ephemeral and lives in one person's scrollback.
3. **Humans own the structure.** The summary is an artefact the group edits, not a block of text one person read once.
4. **The loop back to responders.** You cannot ask fifteen colleagues to react to a paragraph inside your Claude conversation.

The fourth is the one that matters most, and it's why responder reactions are v1 rather than a nice-to-have. Without them this really would be a wrapper.

**This cuts against the v1 workflow, and the tension is worth holding.** Drafting and review both happen in an agent session ([flow 4](#4-summarise)), which is the same ephemeral scrollback point 2 objects to. That's fine while the *artefact* is the tree in the app and the chat is only the workshop. It stops being fine the moment the reasoning that shaped a summary lives nowhere but one person's chat history — so if that starts happening, it's a signal the edit UI is overdue rather than a reason to relax the principle.

## Locked decisions

| Decision | Choice |
|---|---|
| AI integration | REST API hosted in the app. Agent authenticates with a survey-scoped token. No API key in the app. |
| Summaries | Versioned. Each generation run creates a new one; public page shows the newest published. |
| Secrets | Everything hashed. Summariser token shown once at creation; regenerate if lost. |
| Summary structure | A tree of `SummaryNode`, depth uncapped. A node is text, references and children — [nothing types it](#node-kinds-tried-removed), so grounding validates the shape of a branch rather than the sort of node. |
| Summary editing | Explicit edit page with forms. **No ordering concept** while the agent rewrites whole drafts; a real editor forces an ordinal. |
| Response identity | `ResponseIdentity` per survey: `Required` (default), `Optional`, `Anonymous`. Set at creation, immutable. Anonymous records **no timestamp at all**. |
| Response editing | Authors can edit until the survey stops accepting responses, then frozen. Admins never edit — soft-delete only. |
| Reopening | Allowed only while no summary exists. After that, run a new survey. |
| Responder reactions | **In v1.** Agree / Important / Misrepresents-me, per node, by people who responded. |
| Cross-survey work | Deferred, and solved with a `Collection` entity — **not** accounts. Auth seam built now. |
| Node relations | Deferred. What remains deferred is the *cross-cutting* link — the one a tree structurally cannot hold. Open vocabulary, unlike anything grounding keys off. |

### Response editing: a window, then frozen

Humans are squishy, slow things who will want to fix what they wrote thirty seconds after hitting submit. So authors *can* edit their own response — but only while `IsAcceptingResponses` is true. Closing the survey freezes every response, and closing is the natural step before summarising anyway.

Admins never edit responses, in any mode. Soft-delete is the only destructive power an admin gets. An admin quietly rewording someone's criticism is the single thing that would make the whole summary untrustworthy, and it's less code not to build it.

This gives the tool a workflow shape it didn't have before:

**collect → close → summarise → publish**

That ordering is doing real work. Because summary references are only ever created against frozen text, quote offsets cannot rot — the guarantee that immutability was buying us is preserved by sequencing instead.

**Closing is one-way once a summary exists.** A survey closed by accident can be reopened, but the moment any summary has been generated the survey stays closed for good. If more input is needed, run a new survey. This is less a restriction we have to police than the natural expression of the rule that keeps quotes honest: responses can only change while nothing references them. (Deleting every summary therefore unlocks reopening again — a consequence of the rule rather than a feature, and a harmless one, since with no references there is nothing left to rot.)

### Response identity

Anonymity is a supported option, not the default posture. In a work setting named feedback tends to be the more useful kind — people write differently when they're accountable for what they've said — so the default is to ask for a name.

That wants three states rather than a bool, so the model carries a `ResponseIdentity` enum — stored as a string, see [Tech](#tech):

| Value | Author field | Timestamps |
|---|---|---|
| `Required` (default) | shown, mandatory | recorded |
| `Optional` | shown, may be left blank | recorded |
| `Anonymous` | absent entirely | **not recorded** |

`Optional` on its own wouldn't cover it: a name box that people can skip gets skipped, which defeats the accountability that non-anonymous mode exists for. Names are self-declared and unverified either way — there are no accounts — but a required name still produces the social effect.

**Anonymous mode records no dates at all.** The earlier idea was to store exact timestamps and merely render them fuzzily, which is worse: the data still exists, so any future feature, export, API endpoint or `sqlite3` session can leak it. Don't collect what you don't want to be able to reveal. Two honest consequences:

- **Insertion order still leaks through the SQLite rowid.** Not recording the date isn't enough on its own. Anonymous surveys order responses by `Id`, giving a stable but meaningless order. Everything else orders by `CreatedAt`.
- **`Response.Id` must be a v4 random Guid, never `Guid.CreateVersion7`.** A v7 Guid embeds a Unix timestamp, so ordering by it would reconstruct submission order *and* leak roughly when each person answered — walking straight past the decision not to store `CreatedAt`. The other entities can use v7 happily; their timestamps are recorded anyway.
- **The Author field disappears entirely** rather than becoming optional. If the clock is worth worrying about, a name box is a far bigger leak than the clock ever was.

## Object model

Guid PKs where the id appears in a URL. `int` identity PKs elsewhere — this is how we get stable display order with no ordering concept in the UI: **insertion order is key order is display order.** In a tree that holds per sibling group, which is enough while the agent writes whole trees depth-first. It stops being enough when a human can move a node; see [step 8](#build-order).

### Survey
```
Id                    Guid       PK
Code                  string     unique, ~7 url-safe chars, the public URL segment
Title                 string
Description           string     the prompt
AdminPasswordHash     string     PBKDF2 (PasswordHasher<Survey>)
SummariserTokenHash   string     SHA-256, indexed
IsPubliclyListed      bool
IsAcceptingResponses  bool
AreResponsesPublic    bool
ResponseIdentity      enum       Required (default) | Optional | Anonymous; stored as string; immutable after creation
CreatedAt             DateTimeOffset
Responses             Response[]
Summaries             Summary[]
```

### Response
```
Id            Guid             PK
SurveyId      Guid
Body          string
Author        string?          self-declared, unverified; absent when Anonymous
AuthTokenHash string           SHA-256 of the cookie token, indexed
IsDeleted     bool             soft delete
CreatedAt     DateTimeOffset?  null when Anonymous
UpdatedAt     DateTimeOffset?  null when Anonymous, or when never edited
References    SummaryNodeReference[]
```

**`Body` stores `\n` line endings, normalised on write.** A browser normalises a `<textarea>` to CRLF on submission — that's in the HTML spec, not a quirk — so what arrives is not what was typed. Left alone, the CRLF reaches the summariser as an escaped `\r\n` inside a JSON string, where it is invisible, and an agent quoting across a line break reaches for `\n`, fails the exact-match check, and gets a diff that looks identical in a terminal. Same class of trap as curly punctuation, with none of the visibility.

Normalising at ingest is what makes it safe, because storage, reads, validation, quotes and offsets then all agree on one representation. Normalising only on the way out would be actively worse than doing nothing: quotes copied faithfully from the API would fail against the stored body, which is the one failure the grounding rules must never produce. Existing rows want a one-off renormalisation with the same migration, since stored offsets shift by one per preceding line.

### Summary
```
Id         Guid    PK
SurveyId   Guid
Body       string  narrative overview, markdown. NOT a duplicate of the tree.
IsDraft    bool    true until a human blesses it
IsPublic   bool
CreatedAt  DateTimeOffset
UpdatedAt  DateTimeOffset
CreatedBy  string? "agent" | "human", shown in the version list
Nodes      SummaryNode[]  every node in the tree, flat; roots are those with no parent
```

Summary timestamps are always recorded — a summary is a document about the group, not a trace of an individual.

Visibility: admins always. Everyone else only when `!IsDraft && IsPublic`. `/surveys/{code}/summary` resolves to the newest visible summary by `CreatedAt`.

`Body` is a short narrative overview only — two or three paragraphs. The node tree is the structured truth. Keeping `Body` narrative is what stops the two representations drifting apart when a human edits one of them.

The weakness of that arrangement is that a narrative overview of a good tree risks being a summary of a summary. [Relations](#deferred) are the fix rather than a threat to it: a causal chain running across three branches is exactly the kind of finding that reads well in prose and cannot be read off the tree, so a relations pass gives `Body` content that is genuinely its own. Rewriting `Body` afterwards needs no new mechanism — a published summary is immutable to the agent, so the rewrite lands in a new version like everything else.

### SummaryNode

The tree. One entity replaces `SummaryTopic` and `SummaryTopicPoint`, because the only thing separating those two was depth.

```
Id           int      identity PK
SummaryId    Guid     on every node, not just roots — one query loads the whole tree
ParentId     int?     null at a root
Text         string   terse; a few words to a sentence. The only thing a node says about itself
Children     SummaryNode[]
References   SummaryNodeReference[]
Reactions    NodeReaction[]
```

**Adjacency list, stitched in memory.** EF cannot eager-load an arbitrary depth, so a read pulls every node for the summary in one query filtered on `SummaryId` and assembles the tree in code. That is why `SummaryId` sits on every node instead of being inferred up the parent chain. A summary is a few hundred nodes at the outside, so there is no closure table, no materialised path and no recursive CTE — and this is less code than the two-level `Include` chain it replaces.

**No `Description` field.** A child node *is* the description. That is the shape real notes take — "UI testing is hard" with "Bothersome to maintain" underneath it, rather than one node carrying a paragraph — and it is what keeps nodes terse enough for depth to stay readable. Elaborating means descending.

**Depth is not capped.** Five levels is a lot and usually means the middle is thin, but a genuinely detailed topic earns it, and any limit that suits a five-response takeaway vote is wrong for a project plan. The [brief](#api-surface) says as much as guidance; validation does not enforce it. The admin UI shows node count and maximum depth instead, so a staircase is visible without being illegal.

**The QDA tradition disagrees**, which is worth recording rather than leaving as an unexamined difference. The standard advice for a code hierarchy is [not to nest more than three levels deep, and not to force codes into a hierarchy at all](https://support.alfasoft.com/hc/en-us/articles/360005281737-How-to-create-a-good-code-structure-in-NVivo). We reject the first half, because a codebook is a *retrieval index* applied across a corpus: depth costs a coder something every time they reach for the right code, where a summary tree is read top to bottom, once, by someone who did not build it. Different job, different cost curve. We take the second half — a one-off sibling next to a deep subtree is correct rather than sloppy, which is what an "Other" heading full of unrelated single nodes is for.

**Single-child nodes are legitimate.** "Puzzling things → What's next for 3D? → What's the plan?" sharpens a heading into a question in two steps and reads correctly. The staircase worth worrying about is the one with nothing at the bottom, and that is a judgement, which is exactly why it is guidance rather than a rule.

**A node has text, references and children, and nothing else.** No type, no scores. What a node means has to be in its text, which is also what the [brief](#api-surface) tells the agent: "A real budget held by the team" reads as a fact, so if it is something people want, write it as one.

**Grounding inherits down a branch.** A node with no references of its own resolves to its nearest cited ancestor's. Demanding a fresh citation at every level would only make the agent copy one quote four times — more tokens, one more chance to corrupt it per repeat, no more truth than citing it once. What must hold is that **every branch ends in a citation**: a leaf either cites for itself or sits under something that does.

The honest cost: a fabricated claim four levels under a real quote rides on that quote. Requiring re-citation would not catch it either, since the agent is holding the quote already. So the mitigation is display rather than validation — inherited support renders visibly weaker than direct support, and the human reviewing the draft can see how far a claim sits from its evidence.

#### Node kinds: tried, removed

A `Kind` on every node — `Frame | Facet | Claim | Detail | Question | Want` — shipped with the tree and was taken out again after a run of fresh drafting sessions. The reasoning for it was sound and is worth keeping: two levels used to supply that typing for free, a topic asserted nothing while a point had to be cited, and in an untyped tree an unreferenced node in the middle becomes the obvious hiding place for a claim nobody made, wearing a heading's clothes. `Kind` put the distinction back and made it checkable at any depth.

What killed it was not the theory but the drafting. **A vocabulary offered to an agent is a vocabulary it tries to satisfy.** Six named boxes turned out to be leading — the agent reached for a kind and then wrote a node to fit it — restrictive where the honest node was between two of them, and confusing in a way that cost attention the quotes needed more. Worse, the label leaked into the prose: nodes were written as sentence fragments completed by their kind, so `{ "text": "A real budget held by the team.", "kind": "Want" }` reads as a fact everywhere the kind is not also on screen. Six kinds also meant six chances to pick the wrong one, in a payload where the field was `required` and a miss was a bare `400`.

**What replaces it is one rule instead of two:** every branch of the tree ends in a citation. A node with children reads as the heading over them and needs none of its own, because the requirement lands on what hangs below it; a childless node with nothing above it cited is rejected whether it was meant as an empty section or an uncited finding. The untyped rule cannot tell those apart, and does not need to — both are wrong.

**The cost is exactly the one the typed design predicted.** An uncited node in the middle of a branch can now carry a claim nobody made, and nothing checkable stops it. That is a real loss of the [grounding](#grounding-the-app-validates-the-agent) guarantee, accepted because a rule the agent routinely trips over protects less in practice than a rule it can follow. The mitigations are the ones already there: inherited support renders visibly weaker than direct support, a human publishes, and [the responses are the precious data](#principles) — summaries can be redrawn.

If the hiding-place problem shows up in real drafts, the thing to reach for is not the six kinds again but a single boolean the agent is not asked to reason about — or display that makes an uncited middle node obvious to whoever is reviewing.

**Sentiment, Objectivity and Intensity: tried, removed** with `Kind` and for the same reason. They were collected from day one and never rendered, on the theory that storing them cost nothing. Storing them cost nothing; *asking for them* cost attention on every node and every quote, in a payload whose one job is character-exact citation. Nothing had been built that read them. Removed rather than left as dead weight in the contract; the shape of the number is recorded here if a display is ever designed that wants it.

### SummaryNodeReference
```
Id          int      identity PK
NodeId      int
ResponseId  Guid
Quote       string   snapshot of the referenced text
StartIndex  int      offset into Response.Body
EndIndex    int
```

`int`, not `uint` — `uint` maps badly through EF/SQLite. `Quote` earns its place three times over: it makes the API contract self-describing, it lets the UI render a quote without loading the whole response, it future-proofs response editing — and it's the key the app validates against on write. See below.

References to soft-deleted responses are filtered out of all rendering. The node itself survives — and where that leaves it with no direct support, it falls back to inherited support like any other node.

Rendering still guards rather than assumes: if `Body[StartIndex..EndIndex]` doesn't equal `Quote`, show the quote without a highlight instead of slicing blindly. Given the workflow rules that should be unreachable, which is precisely why it's a two-line guard and not a recovery mechanism.

### NodeReaction
```
Id                 int              identity PK
NodeId             int
ResponderTokenHash string           SHA-256 of the wys_resp cookie token
Kind               enum             Agree | Important | Misrepresents; stored as string
Note               string?          mainly for Misrepresents
CreatedAt          DateTimeOffset?  null when Anonymous
```

Unique on `(NodeId, ResponderTokenHash, Kind)`, so each is an independent toggle and `Agree` + `Important` can coexist. `Misrepresents` isn't mechanically exclusive with the others — someone can agree with a point in general and still object to how their words were used for it.

**Every node takes reactions.** Agreeing with "What went well" means nothing, but with [kinds gone](#node-kinds-tried-removed) there is nothing on a node that says it is a heading, and inventing a proxy — has children, has no references — would put reaction controls in the wrong place on a tree that does not obey the proxy. A meaningless control is cheaper than a missing one: what must not happen is a node someone wants to object to arriving without the button.

**Only people who responded can react.** The reaction is keyed on the response cookie for that survey, which is both the permission check and the dedupe key. Reactions from passers- by would dilute the signal, and "this misrepresents what I said" is meaningless from someone who didn't say anything.

That keying deliberately links a reaction back to the reactor's own response, so an admin sees *"the author of this response says this node misrepresents them"* and can read the two side by side. That's the entire value of the flag. It reveals nothing the admin couldn't already see: in `Required` mode the response carries a name anyway, and in `Anonymous` mode it stays a nameless response id.

`Misrepresents` is the fidelity loop closing. It gets prominent treatment in the admin summary editor — not a counter tucked in a corner — because it's the signal that the summary is wrong in the specific way this tool exists to prevent.

## Auth & secrets

Three secrets, three different treatments, for three different reasons.

| Secret | Storage | Why |
|---|---|---|
| Admin password | PBKDF2 via `PasswordHasher<Survey>` | Human-chosen, therefore reused elsewhere. Needs a slow KDF. |
| Summariser token | SHA-256, indexed | 256 bits of entropy. A slow KDF here would just make every API request slow for zero security gain. |
| Response auth token | SHA-256, indexed | Same reasoning. |

No ASP.NET Identity. Cookies are signed/encrypted with `IDataProtector`:

- `wys_admin_{surveyId}` — set after a correct password, sliding 12h expiry.
- `wys_resp_{surveyId}` — set on submit, holds the plaintext response token, long-lived. It authorises editing your own response while the survey is open, shows you your own submission on a return visit, and authorises reacting to summary nodes. Lose the cookie and you lose all three — acceptable, given the alternative is accounts.

### The admin seam

Admin checks go through one service — `AdminSession.CanAdministerAsync(surveyId)` — never by reading cookies inline in pages. Today it's backed by the survey password cookie. When cross-survey work arrives that class learns to check a collection password, and if accounts are ever genuinely warranted it's one class to rewrite rather than a hunt through every page. Costs nothing now; removes the refactor that would otherwise be the reason not to add collections later.

**A class, not an interface.** The seam is the centralisation, not the abstraction. `Survey.CollectionId` is nullable in the deferred design, so a collection-aware check is a branch *inside* this class rather than a second implementation chosen at composition time — and nothing mocks it, because the tests reference the domain library and this lives in Web. An interface here would be ceremony over a single implementation that is never selected between.

The summariser token is generated at survey creation and **displayed exactly once**, on the post-creation screen, with a copy button and a clear warning. Admins can regenerate it from settings, which invalidates the old one.

## Routes

Plural `/surveys/{code}`, following the Rails-style resource convention that most web frameworks inherited: `/surveys` is the collection, `/surveys/{code}` is one item in it.

### Public
```
/                                     home — publicly listed surveys, "New survey", open-by-code box
/surveys                              redirect to /
/new                                  create a survey
/surveys/{code}                       the prompt + response form
/surveys/{code}/summary               newest visible summary
/surveys/{code}/summary/{id}          a specific version
/surveys/{code}/responses             raw responses, only if AreResponsesPublic
```

### Admin
```
/surveys/{code}/admin                 password gate, then dashboard
/surveys/{code}/admin/responses       list, soft-delete
/surveys/{code}/admin/settings        toggles, regenerate summariser token
/surveys/{code}/admin/summaries       version list, publish/unpublish, delete, new blank
/surveys/{code}/admin/summaries/{id}  the edit page
```

### Machine
```
/api/surveys/{code}/ai-summary-start  the whole job as plain text; where an agent starts
/api/surveys/{code}                   survey prompt, settings and counts
/api/surveys/{code}/responses         live responses, paged, with the id to cite each by
/api/surveys/{code}/summaries         version list; POST creates a draft
/api/surveys/{code}/summaries/{id}    one version in full; PUT replaces a draft
/api/surveys/{code}/summaries/{id}/reactions

Survey in the path, `Authorization: Bearer <summariser token>` in the header.
```

## Flows

### The whole arc

**collect → close → summarise → ratify → react → relate → rewrite**

An extension of the [three-step ordering](#response-editing-a-window-then-frozen) that response freezing already imposed. *Ratify* is the group agreeing the tree by whatever means suits them, *relate* is the [relations pass](#deferred), and *rewrite* is bringing the narrative body back into line with whatever the last two turned up. The numbered flows below detail the steps that exist today.

**The app enforces invariants, not stages.** The arc is a recommended shape, not a state machine, and that distinction matters more than the shape does. Only one kind of rule earns enforcement: the kind where breaking it produces a summary that *looks* trustworthy and isn't. Everything else belongs to whoever is running the thing.

| Enforced | Why |
|---|---|
| No summary while responses are open | quotes taken from a moving target rot |
| No response editing once closed | the same rule from the other end |
| No reopening once a summary exists | nothing may change underneath a reference |
| Published summaries immutable to the agent | human edits are never stomped |
| Reactions only from people who responded | otherwise the signal dilutes |

| Not enforced | Why not |
|---|---|
| Reactions before or after relations | a matter of taste; both orders are defensible |
| Whether relations happen at all | most surveys will not want them |
| Whether the narrative gets rewritten afterwards | letting it go stale is a lapse, not a corruption |
| Whether anyone ratifies anything, or how | the app cannot tell whether that happened and should not try |

The test for the left column: does violating it produce something that reads as trustworthy and isn't? Then it belongs in the schema or the service layer. If it merely produces a worse process, it belongs in the brief, or nowhere.

**Adding a response is not the same as editing one, and the model conflates them.** The rule keeping a survey closed once a summary exists is justified by quote rot — but rot comes from *edits*, which move offsets underneath existing references. A brand-new response moves nothing: it makes a summary *incomplete*, not *wrong*. So a relaxation is available that the current design forbids outright — allow new responses after summarising, keep editing frozen, and mark summaries drawn before them as stale. That is the one case where a later stage genuinely would need invalidating, and it is the only invalidation the arc needs. Not taken; [duplicating a survey](#deferred) remains the answer for now. Worth recording because the present rule is stricter than its own justification requires, and nobody should re-derive that from scratch later.

### 1. Create a survey
1. `/new` — title, prompt, admin password, the four toggles.
2. Submit. Survey created, `Code` generated, admin cookie set immediately.
3. Land on a confirmation screen showing: the share link, and the summariser token with a copy button and "this is the only time you'll see this".
4. Continue to the admin dashboard.

`ResponseIdentity` is immutable after creation. There's no point changing it once responses are coming in: switching to `Anonymous` later can't retroactively unrecord timestamps or unsay names, and switching away from it mid-survey silently changes the deal earlier responders agreed to. Set once at creation, rendered read-only in settings thereafter.

### 2. Respond
1. `/surveys/{code}` — prompt, textarea, name field per `ResponseIdentity`, and a plain-English line stating exactly what is stored.
2. Submit. `wys_resp_{surveyId}` cookie set.
3. Confirmation inline, with a link to the summary if one is visible.
4. Returning with the cookie shows your own response back to you. While the survey is accepting responses it's editable in place, with a withdraw option; once closed it's read-only with a one-line note saying why.
5. If `!IsAcceptingResponses`, the form is replaced by a closed notice plus a summary link.

### 3. Admin
1. `/surveys/{code}/admin` with no cookie → password prompt.
2. Correct password → cookie set, dashboard: response count, summary versions, share link, quick toggles, and **Close survey** as the primary action once responses are in.
3. Closing is presented as the gateway to summarising rather than as a settings checkbox — "Close and summarise", explaining that it freezes responses so the quotes stay accurate. Reopen is offered while no summary exists and disappears once one does, so the close confirmation says which kind of close this is.
4. Responses list: anonymous surveys show no times at all and order by `Id`; everything else shows real timestamps in submission order, with an "edited" marker where `UpdatedAt` is set.

### 4. Summarise
1. Admin copies the base URL, survey code and summariser token into their agent session.
2. Agent calls `list_responses`, thinks, calls `create_summary` with the whole tree in one shot.
3. A **draft** summary appears in the version list, attributed to "agent".
4. **Review happens in the agent session**, with one or two people reading the tree in chat and asking for changes. The agent resubmits the whole thing with `PUT` each round. Full replacement is safe because reactions can only exist on published summaries and published summaries are immutable to the agent, so a rewrite has nothing to orphan.
5. Admin opens the edit page, makes whatever corrections are left, then publishes.
6. Publishing sets `IsDraft = false`. The agent can no longer touch that version — a re-run creates a new one. Human edits are never stomped.
7. Responders read the published summary and react. If nodes get flagged as misrepresenting, the admin runs a second pass — the agent reads `list_reactions`, sees the objections and the notes, and drafts a new version addressing them. Versioning makes this safe, and the old version stays readable alongside the objections that prompted the new one.
8. **Optionally, a relations pass** over that second draft — [a separate stage](#deferred), deliberately after the group has seen the tree once. Not every survey wants one.

### 5. Read a summary
1. `/surveys/{code}/summary` — narrative overview, then the tree as a nested list.
2. Each node: its text. Roots read as headings because that is what the top of a tree is, not because the node says so — depth is the only thing left to render by.
3. A node that has references carries an expander showing the supporting quotes. A node relying on inherited support says so, visibly more quietly than one citing its own.
4. If responses are visible to you, each quote links through to the full response with the quoted span highlighted.
5. If you responded to this survey, every node carries agree / important / misrepresents-me controls, the last opening a small note box. Counts are visible to everyone; the notes are for the admin and the next drafting pass.
6. Deep branches collapse below a sensible level rather than indenting off the screen. Which level is a rendering decision, not a stored one — nothing about depth is a property of the data.

## API surface

The survey is named in the path and the token travels in an `Authorization: Bearer` header, so the two vary independently: a longer-lived or differently scoped credential later does not change any URL. A token reaches exactly one survey.

An agent starts at `GET /ai-summary-start`, which returns the rules, the payload shape, the other endpoints and the survey's current state as plain text. Keeping the instructions server-side means they are versioned with the code, so improving them does not require anyone to re-paste a prompt.

Five things the brief carries that came out of watching an agent actually use it:

- **Name people who gave a name.** A response carrying an author may be attributed; an anonymous one is referred to as a response and nothing more. Unstated, the agent invents a policy per run, and the choices are not interchangeable — attributing only the named half of an `Optional` survey makes one group's opinions accountable while everyone else's stay deniable, which is a decision the tool should be making, not the drafting agent.
- **Check every quote is a substring of the response body before sending.** The agent already holds `/responses` in context, so this is a free local check that eliminates a whole class of `422` before it reaches the wire. Cheaper than a dry-run endpoint and strictly better than one, because it costs no round trip and can be done while drafting rather than only at the end.
- **Build the payload with code if you can; write it out directly if you can't.** An agent with an interpreter should construct the JSON programmatically and run the substring check before sending; one without should emit it and re-read each quote against the response first. Two sentences cover both kinds of agent, which is the cheap alternative to accepting a second input format for the benefit of the second kind.
- **Start a new summary; don't continue an old one.** A run produces a new version. `PUT` is for iterating on a draft within a session, not for picking up a previous agent's draft — prior drafts with no reactions on them are not an invitation to continue.
- **Nothing about how many nodes to produce, and no depth limit.** Deliberately absent. The right shape for a five-response takeaway vote and a sixty-response company review are not the same, and any general rule would be wrong for one of them. That tailoring belongs to whoever is prompting, per survey. The brief does carry the one piece of guidance that generalises: go as deep as the detail warrants, keep each node terse, and treat a long chain of single children as a sign the middle of it is thin. Guidance, not validation — see [SummaryNode](#summarynode).

**Read**
- `GET /` — title, prompt, settings, response count.
- `GET /responses` — id and body for all non-deleted responses; author and createdAt only when the survey isn't anonymous. The API gets no privileged view of anonymised data, because there isn't one to have. **Paged**, with the total always returned so an agent knows what it is dealing with before it starts. The 60–100 response survey is a real shape, and one unbounded array is both a context problem and an obstacle to splitting the work: an agent farming extraction out to sub-agents needs slices it can name and hand over. Plain `skip`/`take` is enough here — no cursor. `POST /summaries` refuses while the survey is still accepting responses, so by the time anything is paging this collection it is frozen, and the drift that cursors exist to solve cannot occur.
- `GET /summaries` — versions with id, createdAt, isDraft, isPublic, createdBy.
- `GET /summaries/{id}` — the full node tree with its references, nested.
- `GET /summaries/{id}/reactions` — per-node counts plus every `Misrepresents` note in full. This closes the loop: a second pass can be asked to *fix the nodes people objected to* rather than starting cold, which is a far better prompt than "try again". Objections are the highest-value input the agent can have and they only exist because the group answered back.

**Write**
- `POST /summaries` — one shot, nested payload, creates a draft and returns its id and edit URL. One atomic request beats `add_node` chatter: no partial state, no ordering to coordinate, and the agent gets to think about the whole structure at once. It is also what makes whole-tree resubmission the natural edit operation rather than a special case. **Refuses while `IsAcceptingResponses` is true**, with an error telling the agent to ask the admin to close the survey first. Summarising a moving target produces quotes that rot; this is the one line that enforces collect → close → summarise.
- `PUT /summaries/{id}` — **only while `IsDraft`**. Published versions are immutable to the agent.

**Deliberately absent:** deleting or editing responses, publishing a summary, changing survey settings, reading the admin password. The token is a summarising capability, not an admin capability.

### Why nested JSON

Worth recording, because both alternatives look attractive and both are wrong for reasons that aren't obvious until you try them.

**Not markdown.** Models are more fluent in markdown than JSON, and a strict text format is a tempting way to lower the bar for an agent writing the payload out by hand. It fails on the one field that matters. Markdown has no lossless container for verbatim text: blockquote `>` needs stripping per line and breaks when a quote begins with `>`, fenced blocks break when a quote contains a fence, backticks break on backticks, and trailing whitespace is both meaningful and invisible. Real quotes contain straight double quotes and line breaks. JSON has exactly one encoding for any string and every parser agrees on it — precisely what the field whose entire contract is character-exactness needs. Markdown is lossy by design, which is a virtue everywhere except here.

**Not flattened.** Sending a flat list of nodes each naming its parent removes the nesting and looks like it makes the payload easier to emit. It converts a structural error into a silent one: a mistyped parent name spawns a spurious node instead of failing loudly, and with a tree it can also produce a cycle. Nesting makes both unexpressible — the same reasoning that keeps offsets out of the contract. Prefer the shape where the mistake cannot be made over the shape that is marginally easier to type.

**The recursion costs us one property, and it is worth naming.** A two-level schema made "too deep" unexpressible; a recursive one cannot. Depth guidance therefore moves out of the shape and into the brief, where it is advice an agent can ignore. That is a real loss, accepted because the alternative — a cap in the schema — was measured against a real set of notes and would have truncated them at five levels. Grounding is what the shape still enforces, and grounding is the property that matters.

The cost markdown was meant to address is real: nesting is genuine load for an agent emitting a long payload token by token, and rejection is all-or-nothing. It is answered in the brief instead, with two sentences telling an agent to build the payload with code where it can — no second format to maintain.

### Grounding: the app validates the agent

The consistent finding in the attribution literature is that models which cite more tend to cite *less* accurately — fabricated and misattributed quotes are the normal failure mode, not an edge case. The recommended mitigation is boring: validate every citation programmatically and reject the ones that don't resolve.

We're unusually well placed to do this, because every quote is a span into text we already own. So the write endpoints **validate rather than trust**:

1. **Every branch resolves to a reference.** A leaf either carries references of its own or inherits its nearest cited ancestor's. A node with children needs none, because the requirement lands on what hangs below it. Enforced in the schema, not just the prompt: a branch with no citation anywhere in it is the agent inventing a theme nobody raised. This is weaker than the [typed rule it replaced](#node-kinds-tried-removed), which could demand a citation from the topmost *asserting* node rather than only from the bottom of the branch.
2. **Every `Quote` must actually occur in that response's `Body`.** Substring match after [normalisation](#match-normalisation), and what gets stored is the span taken from `Body` rather than the string the agent sent.
3. **Offsets are not accepted at all.** The request contract has no offset fields — the app locates the quote itself. That is stronger than validating supplied offsets and correcting them, because a wrong offset stops being something an agent can express. If a quote occurs twice in one response the first occurrence wins, which nobody has to think about. Testing later supplied a second and better reason to refuse them; see [below](#match-normalisation).
4. **Every `ResponseId` must belong to this survey and not be soft-deleted.**
5. **Failures reject the whole call** — atomically. No partial summaries.

This makes fabricated quotes structurally impossible rather than merely unlikely, and it costs maybe fifteen lines. Requiring references up front also forces extract-then-summarise ordering, which is independently the thing that most reduces hallucination.

Rejections are counted as `whatyousay.summaries.rejected` tagged by reason — the headline number for how often an agent tries to cite something it cannot substantiate, and so for whether any of this is worth the trouble.

A rejection has to be as useful as the rules are strict, or the agent retries with the same mistake. Validation therefore collects *every* problem in one pass rather than stopping at the first, and answers `422` with each failure located by a JSON Pointer into what was sent. For a quote that missed, the response carries the text the quote was probably reaching for, copied exactly, plus the first character where the two diverge, named by codepoint. Normalisation removes the curly-punctuation class of failure outright, so what still reaches a `422` is a genuine miss — a dropped word, a run-together line, a half-remembered sentence — and there, naming the diverging character remains the difference between a one-shot fix and a retry loop.

#### Match normalisation

Scheduled in [step 9](#build-order). Rule 2 compares *normalised* text rather than raw text: both the submitted quote and the body are normalised for the comparison only, and what gets stored is the canonical span taken from `Body` — never the string the agent sent.

Normalisation is NFKC, curly punctuation folded to straight, en and em dashes folded to hyphen, the ellipsis glyph folded to three dots, runs of whitespace collapsed to a single space, and the ends trimmed. This is a different thing from normalising `Response.Body` line endings on write: that one is about storage having a single representation, this one is about comparison tolerating presentation.

Measured against the seed retro responses, over eight realistic single-character corruptions of otherwise-correct quotes:

| | exact match | normalised |
|---|---|---|
| Verbatim quote | 2/19 | **14/19** |
| Prefix/suffix anchors | 4/19 | **17/19** |

The cases normalisation does *not* rescue are a dropped comma (0/2) and a typo (0/3) — content errors, which should still fail. So it forgives presentation and rejects substance, which is the right place to draw the line. Roughly ten lines of code, for the largest available reduction in false rejection.

**Offsets: tested, rejected.** Asked to produce `start` and `length` by reading rather than by counting in code, an agent scored 3/6, with errors between 0 and −2 characters. Being *close* is the problem, not the consolation. An offset that is wrong but in bounds still selects real text from the right response, so it validates — the app cannot tell that a different span was meant. Grounding would quietly degrade from "this quote supports this point" to "some text from this response was selected". Silent misattribution is the specific failure this section exists to prevent, which is a stronger reason to refuse offsets than rule 3 originally had.

**Line and sentence indices: tested, rejected.** The idea was that citing a line number would remove the copying burden entirely. On prose it does not survive contact: across the seed retro responses, half the spans worth quoting were *sub-sentence*, so an index can only ever address the other half. It fitted one response that happened to be written a point per line, which is exactly the shape not to design for. It shares the silent-misattribution flaw too, if less severely.

Verbatim quoting measured better than either alternative: 6/6 unassisted in the same test, 13/13 in the first real drafting run, and 818 characters of source transcribed exactly. The burden is real but small, and it is the only mechanism of the three that cannot fail quietly. **Keep the quote; make the comparison forgiving.**

If the burden ever does need reducing, prefix/suffix anchors are the mechanism to reach for — `{ "from": "...", "to": "..." }`, with the app taking the span between them. They cut the exactly-reproduced surface to 52% of a full quote, they fail loudly like a quote rather than quietly like an offset, and each anchor tested was unique within its response. The detail to get right is that `to` must resolve to the last candidate rather than the first, or a repeated phrase silently truncates the span. Not needed yet.

These rules are the responsibility of the service layer, not the API layer, so a future "Summarise" button in the UI inherits them for free.

## Tech

- .NET 10. Blazor Web App, **static SSR by default**, with pages opting into `InteractiveServer` individually. The original plan said global interactivity; that turns out to be wrong, because setting the responder cookie needs `HttpContext`, and an interactive circuit has no response to write headers to. Static SSR form posts get one, which is how the framework's own Identity pages sign people in. Interactivity can be added per page later where it actually earns itself.
- EF Core 10 + SQLite, `whatyousay.db`. Migrations checked in, `Migrate()` on startup.
- **`DateTimeOffset` is stored as a UTC ISO-8601 string** via a global value converter. SQLite refuses to `ORDER BY` EF's default mapping, because the offset lives in the text and rows with different offsets would sort wrongly. Normalising to UTC makes it sort lexicographically — which is chronologically — and keeps it readable by hand.
- **Enums stored as strings**, not ordinals (`.HasConversion<string>()`). The point of a SQLite file is that you can open it with `sqlite3` and read it — `Anonymous` tells you something, `2` doesn't. It also means reordering enum members can never silently reinterpret existing rows.
- Markdig for the summary narrative.
- MSTest over the service layer only, against real SQLite. No UI tests, no mocking.
- **OpenTelemetry traces and metrics over OTLP.** ASP.NET Core, HttpClient, EF Core and runtime instrumentation, plus a `WhatYouSay` source and meter for domain events.

### Telemetry is an anonymity hole unless it is closed deliberately

Spans and metrics carry timestamps by construction. Enabling ASP.NET Core instrumentation naively records `url.path` = `/surveys/allco26` next to a timestamp for every request, which reconstitutes exactly the per-survey submission log that anonymous mode gives up `CreatedAt` to avoid. The observability stack would quietly undo the privacy design.

Two rules close it:

1. **The survey code is stripped from recorded request paths** by a span processor. `http.route` keeps the template, so debugging still works.
2. **Domain spans and metrics never take a raw code.** They go through `WhatYouSayTelemetry.TagFor(survey)`, which substitutes `(anonymous)`, so the rule lives in one place instead of at every call site.

This reduces the leak rather than eliminating it — with a single anonymous survey running, request timing still says something. It stops the trace store being a per-survey log, which is the part that matters.

```
WhatYouSay.sln
Directory.Packages.props   central package management; no versions in csproj files
global.json                opts dotnet test into Microsoft.Testing.Platform mode
WhatYouSay/                domain library — the root name belongs to the actual thing
  Data/            WhatYouSayContext, entities, migrations
    Seed/          dev-only seed surveys of varying shape and size
  Services/        SurveyService, ResponseService, SummaryService
  Auth/            token generation, hashing, cookie helpers
WhatYouSay.Web/            Blazor Server front end
  Components/      Pages, Layout, shared bits
  Api/             summariser endpoints and contracts
WhatYouSay.Tests/          references the domain library only
PLAN.md
```

The web project is deliberately not the root project. Tests target the domain library and have no reason to build a front end, and the split keeps the service layer honest about what it depends on — a service that needs `HttpContext` won't compile there.

## Build order

Deliberately front-loads the open question. The thing worth knowing early is whether the generated summaries are any good, so the build reaches a real summary over real data before it builds any CRUD that assumes the answer is yes.

1. **Scaffold, model, seed data.** *(done)* Project, EF model, first migration, and a seeder producing surveys of deliberately different shapes:
   - a 12-response sprint retro
   - a 5-response "where shall we get food"
   - a 60–100 response company-wide one, many topics
   - a multi-day diary, as the single-author case

   The last three exist to stress the model against the non-work uses. Seeding also means `/new` doesn't have to exist yet.
2. **Minimal respond + summary read.** *(done)* `/surveys/{code}` and the summary page. Just enough to get data onto a screen.
3. **API + grounding validation.** *(done)* `/api/surveys/{code}`, bearer auth, the read endpoints, `POST /summaries` with the full validation rules. Then generate summaries over the seed data and read them properly.
4. **Decision point.** *(done)* Are these summaries better than pasting responses into a chat? If not, this is the cheapest possible place to have found that out.
5. **Reactions.** *(done)* Agree / important / misrepresents on the summary page, `GET /summaries/{id}/reactions`, and the second-pass loop. This is the feature that makes the answer to step 4 "yes", so it lands here rather than in polish.
6. **Admin.** *(done)* Password gate, the `AdminSession` seam, dashboard, response list, soft-delete, settings, close/reopen, and `/new`.
7. **The node tree.** *(done)* `SummaryTopic` and `SummaryTopicPoint` become `SummaryNode`; `SummaryTopicPointResponseReference` and `PointReaction` become `SummaryNodeReference` and `NodeReaction`. One migration, grounding rewritten to the branch rule, nested rendering on the summary page, and the brief updated to describe a tree. Lands before step 8 for one reason: an edit UI written against topics and points would have to be written twice.

   The depth check this step exists for was run, and answered a question nobody had asked: the tree carries meaning, but the `Kind` vocabulary shipped with it was leading the agent into writing nodes to fit a box. A second migration [drops `Kind`, `Sentiment`, `Objectivity` and `Intensity`](#node-kinds-tried-removed); a node is now text, references and children.
8. **Summary edit.** The forms page, publish/unpublish, delete, and prominent display of objections. This is where **no ordering concept** finally dies: moving a node makes insertion order wrong, so the editor brings an `Ordinal` column and a migration with it. Nothing before this step needs one, because the agent rewrites whole trees and depth-first insertion reproduces sibling order for free.
9. **Summariser hardening.** Everything below came out of watching an agent draft a real summary end to end, which is a different exercise from designing the endpoint and turned up things the design could not have predicted. Grouped as one step because it is all the same surface and wants one migration.
   - **Normalise `Response.Body` line endings to `\n` at ingest**, with a migration renormalising existing rows and repairing the reference offsets that shift as a result. Reasoning under [Response](#response). This one first: every quote the API validates depends on it.
   - **Page `GET /responses`** — `skip`/`take` plus a total. Frozen collection, so no cursor needed.
   - **Four additions to the brief** — name people who gave a name, check quotes are substrings locally before sending, build the payload with code where possible, and start a new summary rather than continuing an old one. Reasoning under [API surface](#api-surface).
   - ~~**Make the worked example in the brief valid JSON.**~~ *(done)* Landed with the step 7 rewrite of the brief.
   - **Say what is wrong with a payload that will not bind.** A missing `text`, or any malformed JSON, fails in the reader rather than in validation, so it comes back as a bare 400 with no body outside Development — no pointer, nothing to fix. Every other mistake an agent can make gets a located 422. This wants `RouteHandlerOptions.ThrowOnBadRequest` and a handler turning the `JsonException` path into the same shape. Less pressing since [`kind` was removed](#node-kinds-tried-removed), which was the field agents actually got wrong.
   - **Normalise both sides before matching a quote**, and store the span taken from `Body` rather than the string that was sent. Measured as the largest available reduction in false rejection — 2/19 to 14/19 against corrupted-but-correct quotes — for roughly ten lines. Reasoning and numbers under [Match normalisation](#match-normalisation).
10. **Polish.** Publicly listed home page, token regeneration, empty states, and the copy telling responders exactly what is and isn't stored.

Steps 1–3 stand alone as something usable with hand-written summaries, which makes them a reasonable stopping point if the weekend runs out.

## Prior art

Checked before building. Nothing free does this combination — self-hosted, no accounts, link-shared, one free-text prompt, agent-driven summarisation over a plain HTTP API — but several projects have solved adjacent pieces and are worth learning from.

- **[Talk to the City](https://github.com/AIObjectives/tttc-light-js/)** (AI Objectives Institute) — the near-hit. LLM extracts claims from free text, clusters them into topics/subtopics, links every claim to an exact quote. Independently arrived at essentially this object model, and has been used for government and union consultations. Their [write-up](https://ai.objectives.institute/blog/talk-to-the-city-an-open-source-ai-tool-to-scale-deliberation) argues the report *structure* is what mitigates LLM inaccuracy — drill-down from theme to verbatim opinion is what makes a summary trustworthy. They also concluded manual editing of AI output remained necessary and was "reasonable overhead". Both findings validate choices here. Not reusable: Next.js + Express + pipeline worker + Firebase + GCS + Redis
  + Pub/Sub.
- **[Parabol](https://github.com/ParabolInc/parabol)** — open source, self-hostable, air-gappable, AI theme-grouping for retros. Different shape: a *synchronous* meeting tool with multiplayer sticky-note grouping and accounts. Ours is async collect-then-summarise.
- **[Formbricks](https://github.com/formbricks/formbricks)** (AGPLv3) — closest on the survey side; self-hosted, open text, AI insights. But a full form builder with orgs, projects and question types. Enormous relative to "one prompt, one textarea".
- **[Fast Retro](https://fastretro.app/), QuickRetro, Postfacto** — free self-hosted retro tools with anonymous input and link sharing. Closest on the social model, no AI summarisation with quote grounding.
- **[Taguette](https://www.taguette.org/) / QualCoder** — qualitative data analysis. The intellectual ancestor of `SummaryNodeReference`: highlight a span, tag it with a code. Academia has done this by hand for decades and calls it *qualitative coding*. Useful vocabulary if we ever want to export somewhere.

**Convergence, in the end.** This originally read as a deliberate divergence: Talk to the City nests topics → subtopics → claims because it processes thousands of inputs, so at 6–20 responses flat topic → point looked like the right call and the extra level like noise. Two things overturned that.

The first is that depth tracks the *detail* of a topic, not the *volume* of input. A five-response conversation about one thorny thing goes deeper than a sixty-response survey that skims. Response count was the wrong axis to pick a shape on.

The second is a real retro's notes, written by hand years before this tool existed and reaching five levels at its deepest — "what did we learn → 3D → filing issues before they are done is problematic → level of detail in an issue can be tricky → doing investigation vs being clear for other people". Every level there earns itself, and the last one is a tension inside the point above it that a flat model cannot express at all. Two levels was a constraint invented for the model rather than found in the material.

Worth noting that TTTC's three levels are *fixed*. Ours are not, which is the actual divergence now: they have a pipeline shape to defend, we have notes to match.

### Typed nodes

Prior art for the [kind vocabulary](#node-kinds-tried-removed), checked after the fact rather than before. Almost none of it is ours. Kept now that the vocabulary is gone, because it is the reading anyone would have to redo before trying node typing again — and because the last line of this section turns out to be the interesting one.

- **[Compendium](https://www.cognexus.org/IBIS-A_Tool_for_All_Reasons.pdf)** — the close match, and the one that counts because it was used in anger. IBIS proper has Question, Idea, Pro and Con; Compendium [adds Lists and Maps as containers, plus Decisions, Notes and References](https://www.researchgate.net/figure/BIS-plus-additional-node-types-rendered-in-Compendium-Any-application-document-or_fig1_251532806). Same shape as ours and reached the same way — an argumentative core turned out to be insufficient for real capture, so non-asserting container types were added. `Frame` and `Facet` are their List and Map, `Want` is their Idea, `Question` is theirs unchanged.
- **[Toulmin](https://academics.umw.edu/speaking/resources/handouts/toulmin-argument-model/)** — claim, grounds, warrant, backing, qualifier, rebuttal. `Claim` is his word and our references are his grounds. We deliberately have no warrant, the reasoning connecting the two: a summary reports what people said rather than arguing for it.
- **[Rhetorical Structure Theory](https://www.sfu.ca/rst/pdfs/RST_Introduction.pdf)** — `Detail` was RST's Elaboration, and its nucleus/satellite asymmetry is our parent/child one. RST types the *edge* where Compendium types the *node*; with exactly one parent per node those are the same statement, which is what licensed `Kind` typing the edge above it.
- **[QOC](https://acawiki.org/Questions,_Options,_and_Criteria:_Elements_of_design_space_analysis)** (MacLean, 1991) — Questions, Options, Criteria. Another deliberately tiny fixed vocabulary. Options is `Want` again.
- **[W3C Web Annotation motivations](https://www.w3.org/TR/annotation-model/)** — commenting, describing, questioning, highlighting, tagging. A standardised vocabulary attached to a span of text, which is structurally what a reference is. Notable for being a fixed core *with* an extension mechanism rather than a closed set.
- **[Tana supertags](https://outliner.tana.inc/learn/features/supertags)** — the live commercial version: tag a node and it becomes a typed object with structure. Typed outliner nodes are current practice, not only an academic tradition.

**What had no obvious precedent was keying validation off the kind.** Everything above types nodes so a human can read the map. None of it makes the type decide whether something must be cited. Borrowed vocabulary, novel enforcement — which read as a comfortable place for a small tool to be, and turned out to be the load-bearing difference. Every project above types nodes for a *human* filling them in, and none of them hands the vocabulary to a model that will try to satisfy it. The absence of precedent was the warning.

`Want` has three established names: Position in IBIS, Idea in Compendium, Option in QOC. Kept as `Want` because it names the speech act more precisely, and because "idea" is one of the vaguest words in English.

### Graph-structured feedback

Background for the deferred relations feature. Four traditions have attempted this, largely independently of each other.

- **Axial coding** — the direct continuation of this tool. In grounded theory, open coding (quote → code) is followed by [axial coding](https://atlasti.com/research-hub/axial-coding): relating codes to each other with named relationships. ATLAS.ti implements precisely the idea, as "Networks", and importantly **custom relationships are saved in the project and reused across the analysis** rather than reinvented per pass. Our `SummaryNode` is a code, and hierarchical codebooks are the norm in QDA rather than the exception — NVivo nests parent and child nodes, ATLAS.ti and MAXQDA do the same. Their warning about over-nesting is answered under [SummaryNode](#summarynode). The sequencing lesson: relating comes *after* coding, so nodes need to be stable first.
- **Cognitive / causal mapping (SODA, Colin Eden, 1980s)** — the closest to our use case. Capture stakeholder statements as concept nodes, link them with causal arrows, then [merge individual maps by identifying concepts common to several people](https://www.sciencedirect.com/science/article/pii/S0377221720309784). That merge step is exactly "a graph of feedback from N people", with 35 years of practice behind it. A "stressor" relation is a causal-mapping arrow.
- **IBIS / dialogue mapping (Rittel, 1970)** — Issues, Positions, Arguments, for wicked problems; Compendium, Kialo, argdown. Relevant if the relations turn out argumentative rather than semantic. Notable that [IBIS uses a deliberately tiny fixed vocabulary](https://eight2late.com/2014/11/24/from-information-to-knowledge-the-what-and-whence-of-issue-based-information-systems/) — the constraint is the design, not an unfinished bit.
- **GraphRAG / LLM knowledge-graph construction** — the modern automated form: extract entities and relations, then [Leiden community detection and per-community summarisation](https://arxiv.org/html/2501.00309v2). Worth noting it *summarises via the graph* — the graph replaces the topic list rather than decorating it. That's the stand-alone-graph option, and it's a working architecture.

## Deferred

Not in v1, but the model shouldn't preclude them:

- **Sentiment / objectivity display.** [Removed from the model](#node-kinds-tried-removed) rather than collected unread. Wants a display designed first, then the fields back.

- **Node relations — the cross-cutting graph. The intended next stage.** Typed links between nodes: `causes`, `blocks`, `contradicts`. See [Graph-structured feedback](#graph-structured-feedback) for the traditions this draws on.

  ```
  RelationType       { Id, SurveyId?, Name, Description, IsDirected }
  NodeRelation       { Id, RelationTypeId, FromNodeId, ToNodeId, Note?, CreatedBy }
  NodeRelationRef    { Id, RelationId, ResponseId, Quote, StartIndex, EndIndex }
  ```

  Its own endpoint, `POST /summaries/{id}/relations`, not a field in the tree payload — see the sequencing note below.

  **The tree took a bite out of this.** Containment carries most of what the relation vocabulary was for: a node under another elaborates it, and "wants" is just something a node's text can say. What relations are left for is the link a tree structurally cannot hold: the one that crosses branches. "CI is slow" belongs under Tooling *and* under Morale, and a tree makes you duplicate it or pick one. That is the honest weakness of hierarchy — the intertwingled-information complaint, and the reason faceted tagging exists alongside trees rather than instead of them. It is also the one thing a graph adds that nesting cannot fake.

  **Open vocabulary, not a fixed schema.** This tool has to serve sprint retros, holiday planning, takeaway votes, company-wide feedback and personal diaries. A vocabulary broad enough for all of those is too vague for any of them, so relation types are invented as needed and consolidated afterwards.

  The known failure mode is *canonicalization*: unconstrained extraction yields `likes`, `enjoys` and `is fond of` as three distinct predicates, giving [redundancy and inconsistency](https://arxiv.org/html/2510.20345v1). The established fix is [EDC — Extract, Define, Canonicalize](https://arxiv.org/pdf/2404.03868). Applied here in three cheap layers:

  1. **Show the agent the vocabulary before it extracts.** `list_relation_types` with usage counts, and a tool description telling it to reuse an existing type where one fits and mint a new one only when none does. Most convergence happens at write time, for free.
  2. **`merge_relation_types(from, to)`** for explicit cleanup — repoint the relations, drop the dead type. A data operation, so it's reviewable.
  3. **Sort the admin's relation-type list by usage.** Singletons are the tell: a type used once is nearly always a synonym of one used twelve times.

  `SurveyId` nullable so a house vocabulary can accumulate globally while one-off types stay survey-local.

  **Grounding does not extend for free.** An earlier draft of this section claimed it did — that a relation between two nodes is already grounded because both endpoints are. That is wrong, and it is wrong in the exact way the rest of the design exists to prevent. Two real quotes joined by an invented arrow is a *new* assertion nobody made, wearing grounded clothes. "Slow CI causes batching" is a claim about the world; the quote saying CI is slow and the quote saying people batch commits do not, between them, support it.

  So an edge grounds like a node does. `NodeRelation` carries its own references, and there are three honest provenances:

  | | Meaning | Rendering |
  |---|---|---|
  | references | someone drew the connection themselves, in one response | the strong case; show the quote |
  | none, agent-drawn | the agent inferred it across two responses | marked as inference, never as finding |
  | none, human-drawn | an admin added it in the editor | marked as the admin's reading |

  `solves` is the canary. Almost nobody writes "that solves the other person's problem" — so a `solves` edge is nearly always an inference, and if the UI ever renders inferences indistinguishably from quoted connections, `solves` is where the tool starts making things up on the group's behalf.

  **Sort candidate relations by algebra, not by how useful they sound.** What a relation does when you chain it is what decides whether it produces conclusions or mush, and the obvious candidates behave completely differently:

  | Candidate | Algebra | Verdict |
  |---|---|---|
  | `causes`, `blocks` | directed, chains | **build these first.** Composition is the whole point: "slow CI → people batch commits → bigger reviews → slower reviews" is a finding no tree can express |
  | `contradicts` | symmetric, does not chain | safe and useful — it describes a tension in the group |
  | `refutes` | directed, adjudicates | **no.** See below |
  | `duplicates` | equivalence | usually a defect to fix by merging, not an edge to keep. Worth storing only when recurrence across branches is itself the finding |
  | `relates` | none | **no.** See below |

  **`refutes` is not `contradicts`.** If one person says CI is fine and another says CI is slow, those nodes do not refute each other — they *disagree*, and the disagreement is a fact about the group rather than about CI. A directed `refutes` edge has the agent deciding who was right, which is the tool taking a side in a summary whose entire job is fidelity. `contradicts` records the same pair honestly and leaves the adjudication to the reader.

  **`relates` is a trap, not a fallback.** Every specific relation is also a "relates", so the type carries no information; and because it always applies, it is what an agent reaches for under pressure. A graph of untyped edges costs a reader real attention and returns nothing. If it exists at all it should be a signal that the vocabulary is missing a type — surfaced in the admin list as work to do, alongside the singletons.

  **Keep edges rare.** Forty-five nodes is 990 possible pairs, so even sparse extraction can produce more edges than nodes. At that size a graph view is probably *less* legible than the tree — graphs earn their keep around where an outline stops fitting on a screen — so a run producing more relations than nodes is a bug signal rather than a rich analysis.

  **Nest once, link from elsewhere.** The cross-cutting case is what relations are *for*, so the answer to "CI is slow belongs under Tooling and Morale" is one node and one edge, never two nodes. That means the first rendering of a relation is an inline see-also on the node, not a graph view — the graph can wait until an outline genuinely stops fitting on a screen.

  **A separate stage, in the UX as well as the code.** A summary is built, agreed, and only then optionally moves to a relations pass — never both at once. That turns axial coding's "codes must be stable before relating" from advice into workflow, and it keeps the drafting prompt from asking an agent to do two different jobs in one call.

  Where the stage sits is the interesting choice. Drawing relations *before* the first publish gives one complete artefact and gets the group more to react to; drawing them *after* the first round of reactions means the edges are built over a tree the group has confirmed rather than one only the admin has read. The second is better, because an edge touching a node somebody flagged as misrepresenting them is an edge built on sand — so the recommended flow is [step 8](#4-summarise), over the second draft. It stays a recommendation rather than a rule: the mechanism is just a pass over a draft, and it works at either point.

  **Don't store the stage.** No `HasRelations` flag and no state enum — whether a summary has been through a relations pass is `Relations.Any()`, and "we decided not to bother" is indistinguishable from "we haven't yet" in every way that matters, because publishing is the signal that the admin is done. The stage is an affordance in the editor, not a column.

  **Feeding back into `Body`.** A relations pass is the natural moment to rewrite the narrative overview, and it is the one place the two representations are *meant* to converge — see [Summary](#summary). Nothing new is needed to make that safe: published versions are immutable to the agent, so a rewrite becomes a new version.

  **Being a separate stage is what forces stable node ids.** Edges outlive the pass that drew them, and they collide with [whole-tree resubmission](#4-summarise): in the summary payload every `PUT` destroys them, in their own endpoint the id churn of a rewrite orphans them. So **[stable node keys](#deferred) stop being optional and become a prerequisite** — and relations get their own endpoint rather than a field in the tree, since the tree is the thing being replaced.

- **Cross-survey summaries, via `Collection` — not accounts.**

  ```
  Collection { Id, Name, AdminPasswordHash, Surveys[] }
  Survey.CollectionId  (nullable)
  ```

  A cross-survey summary is one scoped to a collection rather than a survey. One table and one nullable FK, composing with everything already designed. This is where SODA's map-merging gets genuinely interesting: shared concepts across *sprints*, showing which stressors recur and which actually got resolved.

  **Explicitly not accounts.** Not because of the work — users, registration, login, password reset, email, invitations — but because signup friction destroys the property that makes the tool good. "Here's a link, chuck your thoughts in" stops working the moment anyone has to create an account, and so does spinning up a survey in twenty seconds. The [`AdminSession` seam](#the-admin-seam) exists so this stays a contained change rather than a refactor.
- **Markdown export of a summary.** Nested bullets, which is the format the tree came from in the first place. Lossy on purpose: reactions and the reference spans don't survive, and quotes become ordinary text. That is fine in this direction and not in the other — [markdown is refused as an input format](#why-nested-json) precisely because it has no lossless container for verbatim text, and the same lossiness is harmless once the app is the thing being copied *from* rather than written *to*.

- **Stable node keys.** An agent-supplied `key` per node, stable across submissions, so a resubmission can carry only what changed and "this is the same node, revised" becomes expressible. Two reasons, of quite different weight:

  - *Cost.* The agent re-emits every node and every verbatim quote on each `PUT`. Fine for a forty-node tree; it is tokens, and one more chance to corrupt a quote per repeat. On its own this has the same status as the [prefix/suffix anchors](#match-normalisation) — mechanism chosen, need not yet arrived.
  - *Prerequisite for relations.* Anything that points at a node from outside the tree — an edge, and eventually a reaction on a draft — cannot survive a whole-tree rewrite without one. This is the reason it will actually get built.

- **Duplicating a survey** for recurring sprint feedback — and now also the answer to "we need more input after summarising", which makes it more valuable than it first looked.
- **App-initiated generation** — a "Summarise" button calling the Anthropic API directly, for colleagues who don't have an agent session. The service layer should be shaped so the API endpoints and such a button would call the same code.

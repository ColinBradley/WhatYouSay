# What You Say

Namespace / app name: `WhatYouSay`. Product name: **What You Say**.

A small, self-hosted tool for collecting free-text feedback from a group of people who trust each other, then making sense of it with an AI agent. Built for sprint retros, dev cycle reviews and architecture feedback at work. Not a service, not multi-tenant, no user accounts.

> **Status: agreed.** No open questions. Ready to build from step 1.

## Principles

- **One survey = one free-text prompt.** Title + Description *is* the question. There are no fields, no sub-questions, no form builder. Ever.
- **Quick and dirty, but faithful.** The whole value is in the summary being a true reflection of what people actually wrote. Fidelity is the thing to protect. Anonymity is a supported option, not the point of the tool.
- **The agent drafts, the human publishes.** AI never gets the last word.
- **Every claim is grounded in a real quote, and the app proves it.** A summary point that can't be traced to something someone actually wrote doesn't get stored. See [Grounding](#grounding-the-app-validates-the-agent).
- **The group gets to answer back.** A summary nobody can object to is just one person's reading with extra steps. Responders react to the points, including flagging ones that misrepresent them.
- No frills. If a feature needs a design doc, it's deferred.

## Why not just paste the responses into a chat?

Worth answering explicitly, because if there isn't a good answer this shouldn't exist. Four things a chat session structurally cannot do:

1. **Grounded quotes.** A chat summary can't be checked. This one can't misattribute, because the app validates every quote against the response it claims to come from.
2. **It has a URL, versions and history.** Chat output is ephemeral and lives in one person's scrollback.
3. **Humans own the structure.** The summary is an artefact the group edits, not a block of text one person read once.
4. **The loop back to responders.** You cannot ask fifteen colleagues to react to a paragraph inside your Claude conversation.

The fourth is the one that matters most, and it's why responder reactions are v1 rather than a nice-to-have. Without them this really would be a wrapper.

## Locked decisions

| Decision | Choice |
|---|---|
| AI integration | MCP server hosted in the app. Agent connects with a survey-scoped token. No API key in the app. |
| Summaries | Versioned. Each generation run creates a new one; public page shows the newest published. |
| Secrets | Everything hashed. Summariser token shown once at creation; regenerate if lost. |
| Summary editing | Explicit edit page with forms. **No ordering concept** — insertion order is display order. |
| Response identity | `ResponseIdentity` per survey: `Required` (default), `Optional`, `Anonymous`. Set at creation, immutable. Anonymous records **no timestamp at all**. |
| Response editing | Authors can edit until the survey stops accepting responses, then frozen. Admins never edit — soft-delete only. |
| Reopening | Allowed only while no summary exists. After that, run a new survey. |
| Responder reactions | **In v1.** Agree / Important / Misrepresents-me, per point, by people who responded. |
| Cross-survey work | Deferred, and solved with a `Collection` entity — **not** accounts. Auth seam built now. |
| Point relations | Deferred. Open vocabulary, canonicalised by review pass, not a fixed schema. |

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

**Anonymous mode records no dates at all.** The earlier idea was to store exact timestamps and merely render them fuzzily, which is worse: the data still exists, so any future feature, export, MCP tool or `sqlite3` session can leak it. Don't collect what you don't want to be able to reveal. Two honest consequences:

- **Insertion order still leaks through the SQLite rowid.** Not recording the date isn't enough on its own. Anonymous surveys order responses by `Id`, giving a stable but meaningless order. Everything else orders by `CreatedAt`.
- **`Response.Id` must be a v4 random Guid, never `Guid.CreateVersion7`.** A v7 Guid embeds a Unix timestamp, so ordering by it would reconstruct submission order *and* leak roughly when each person answered — walking straight past the decision not to store `CreatedAt`. The other entities can use v7 happily; their timestamps are recorded anyway.
- **The Author field disappears entirely** rather than becoming optional. If the clock is worth worrying about, a name box is a far bigger leak than the clock ever was.

## Object model

Guid PKs where the id appears in a URL. `int` identity PKs elsewhere — this is how we get stable display order with no ordering concept in the UI: **insertion order is key order is display order.**

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
References    SummaryTopicPointResponseReference[]
```

### Summary
```
Id         Guid    PK
SurveyId   Guid
Body       string  narrative overview, markdown. NOT a duplicate of the topics.
IsDraft    bool    true until a human blesses it
IsPublic   bool
CreatedAt  DateTimeOffset
UpdatedAt  DateTimeOffset
CreatedBy  string? "agent" | "human", shown in the version list
Topics     SummaryTopic[]
```

Summary timestamps are always recorded — a summary is a document about the group, not a trace of an individual.

Visibility: admins always. Everyone else only when `!IsDraft && IsPublic`. `/surveys/{code}/summary` resolves to the newest visible summary by `CreatedAt`.

`Body` is a short narrative overview only — two or three paragraphs. The topics and points are the structured truth. Keeping `Body` narrative is what stops the two representations drifting apart when a human edits one of them.

### SummaryTopic
```
Id           int     identity PK
SummaryId    Guid
Name         string
Description  string?
Points       SummaryTopicPoint[]
```

### SummaryTopicPoint
```
Id           int      identity PK
TopicId      int
Description  string
Sentiment    double?  -1 (negative) .. +1 (positive)
Objectivity  double?  0 (pure opinion) .. 1 (verifiable fact)
References   SummaryTopicPointResponseReference[]
```

Sentiment and Objectivity are populated by the agent from day one but **not rendered in v1**. Storing them costs nothing and means the data is already there when we design a display for it.

### SummaryTopicPointResponseReference
```
Id          int      identity PK
PointId     int
ResponseId  Guid
Quote       string   snapshot of the referenced text
StartIndex  int      offset into Response.Body
EndIndex    int
Intensity   double?  0..1, how strongly this quote supports the point
```

`int`, not `uint` — `uint` maps badly through EF/SQLite. `Quote` earns its place three times over: it makes the MCP contract self-describing, it lets the UI render a quote without loading the whole response, it future-proofs response editing — and it's the key the app validates against on write. See below.

References to soft-deleted responses are filtered out of all rendering. The point itself survives.

Rendering still guards rather than assumes: if `Body[StartIndex..EndIndex]` doesn't equal `Quote`, show the quote without a highlight instead of slicing blindly. Given the workflow rules that should be unreachable, which is precisely why it's a two-line guard and not a recovery mechanism.

### PointReaction
```
Id                 int              identity PK
PointId            int
ResponderTokenHash string           SHA-256 of the wys_resp cookie token
Kind               enum             Agree | Important | Misrepresents; stored as string
Note               string?          mainly for Misrepresents
CreatedAt          DateTimeOffset?  null when Anonymous
```

Unique on `(PointId, ResponderTokenHash, Kind)`, so each is an independent toggle and `Agree` + `Important` can coexist. `Misrepresents` isn't mechanically exclusive with the others — someone can agree with a point in general and still object to how their words were used for it.

**Only people who responded can react.** The reaction is keyed on the response cookie for that survey, which is both the permission check and the dedupe key. Reactions from passers- by would dilute the signal, and "this misrepresents what I said" is meaningless from someone who didn't say anything.

That keying deliberately links a reaction back to the reactor's own response, so an admin sees *"the author of this response says this point misrepresents them"* and can read the two side by side. That's the entire value of the flag. It reveals nothing the admin couldn't already see: in `Required` mode the response carries a name anyway, and in `Anonymous` mode it stays a nameless response id.

`Misrepresents` is the fidelity loop closing. It gets prominent treatment in the admin summary editor — not a counter tucked in a corner — because it's the signal that the summary is wrong in the specific way this tool exists to prevent.

## Auth & secrets

Three secrets, three different treatments, for three different reasons.

| Secret | Storage | Why |
|---|---|---|
| Admin password | PBKDF2 via `PasswordHasher<Survey>` | Human-chosen, therefore reused elsewhere. Needs a slow KDF. |
| Summariser token | SHA-256, indexed | 256 bits of entropy. A slow KDF here would just make every MCP request slow for zero security gain. |
| Response auth token | SHA-256, indexed | Same reasoning. |

No ASP.NET Identity. Cookies are signed/encrypted with `IDataProtector`:

- `wys_admin_{surveyId}` — set after a correct password, sliding 12h expiry.
- `wys_resp_{surveyId}` — set on submit, holds the plaintext response token, long-lived. It authorises editing your own response while the survey is open, shows you your own submission on a return visit, and authorises reacting to summary points. Lose the cookie and you lose all three — acceptable, given the alternative is accounts.

### The admin seam

Admin checks go through a service — `ICanAdminister(surveyId)` — never by reading cookies inline in pages. Today it's backed by the survey password cookie. When cross-survey work arrives it'll be backed by a collection password, and if accounts are ever genuinely warranted it's one implementation swap rather than a hunt through every page. Costs nothing now; removes the refactor that would otherwise be the reason not to add collections later.

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
/mcp                                  streamable HTTP MCP endpoint, Bearer = summariser token
```

## Flows

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
2. Agent calls `list_responses`, thinks, calls `create_summary` with the whole nested structure in one shot.
3. A **draft** summary appears in the version list, attributed to "agent".
4. Admin opens the edit page, adjusts wording, deletes points that missed, then publishes.
5. Publishing sets `IsDraft = false`. The agent can no longer touch that version — a re-run creates a new one. Human edits are never stomped.
6. Responders read the published summary and react. If points get flagged as misrepresenting, the admin runs a second pass — the agent reads `list_reactions`, sees the objections and the notes, and drafts a new version addressing them. Versioning makes this safe, and the old version stays readable alongside the objections that prompted the new one.

### 5. Read a summary
1. `/surveys/{code}/summary` — narrative overview, then topics.
2. Each topic: name, description, its points.
3. Each point: the description, and an expander showing the supporting quotes.
4. If responses are visible to you, each quote links through to the full response with the quoted span highlighted.
5. If you responded to this survey, each point carries agree / important / misrepresents-me controls, the last opening a small note box. Counts are visible to everyone; the notes are for the admin and the next drafting pass.

## MCP surface

The token is survey-scoped, so every tool operates on that survey implicitly — there is no survey argument and no way to reach another survey.

**Read**
- `get_survey` — title, prompt, settings, response count.
- `list_responses` — id and body for all non-deleted responses; author and createdAt only when the survey isn't anonymous. The MCP gets no privileged view of anonymised data, because there isn't one to have.
- `list_summaries` — versions with id, createdAt, isDraft, isPublic, createdBy.
- `get_summary(id)` — the full topic/point/reference tree.
- `list_reactions(summaryId)` — per-point counts plus every `Misrepresents` note in full. This closes the loop: a second pass can be asked to *fix the points people objected to* rather than starting cold, which is a far better prompt than "try again". Objections are the highest-value input the agent can have and they only exist because the group answered back.

**Write**
- `create_summary(body, topics[])` — one shot, nested payload, creates a draft and returns its id and edit URL. One atomic call beats `add_topic`/`add_point` chatter: no partial state, no ordering to coordinate, and the agent gets to think about the whole structure at once. **Refuses while `IsAcceptingResponses` is true**, with an error telling the agent to ask the admin to close the survey first. Summarising a moving target produces quotes that rot; this is the one line that enforces collect → close → summarise.
- `update_summary(id, ...)` — **only while `IsDraft`**. Published versions are immutable to the agent.

**Deliberately absent:** deleting or editing responses, publishing a summary, changing survey settings, reading the admin password. The token is a summarising capability, not an admin capability.

### Grounding: the app validates the agent

The consistent finding in the attribution literature is that models which cite more tend to cite *less* accurately — fabricated and misattributed quotes are the normal failure mode, not an edge case. The recommended mitigation is boring: validate every citation programmatically and reject the ones that don't resolve.

We're unusually well placed to do this, because every quote is a span into text we already own. So the write tools **validate rather than trust**:

1. **Every point must carry at least one reference.** Enforced in the schema, not just the prompt. A point with no citation is the agent inventing a theme nobody raised.
2. **Every `Quote` must actually occur in that response's `Body`.** Exact substring match.
3. **Offsets are not accepted at all.** The tool contract has no offset fields — the app locates the quote itself. That is stronger than validating supplied offsets and correcting them, because a wrong offset stops being something an agent can express. If a quote occurs twice in one response the first occurrence wins, which nobody has to think about.
4. **Every `ResponseId` must belong to this survey and not be soft-deleted.**
5. **Failures reject the whole call** — atomically, with a message naming the offending point and quote, so the agent can fix it and retry. No partial summaries.

This makes fabricated quotes structurally impossible rather than merely unlikely, and it costs maybe fifteen lines. Requiring references up front also forces extract-then-summarise ordering, which is independently the thing that most reduces hallucination.

Rejections are counted as `whatyousay.summaries.rejected` tagged by reason — the headline number for how often an agent tries to cite something it cannot substantiate, and so for whether any of this is worth the trouble.

A rejection has to *reach* the agent to be useful. The MCP SDK hides ordinary exception detail from clients, so grounding failures are raised as `McpException`; otherwise the agent gets "an error occurred" and has nothing to fix.

These rules are the responsibility of the service layer, not the MCP layer, so a future "Summarise" button in the UI inherits them for free.

## Tech

- .NET 10. Blazor Web App, **static SSR by default**, with pages opting into `InteractiveServer` individually. The original plan said global interactivity; that turns out to be wrong, because setting the responder cookie needs `HttpContext`, and an interactive circuit has no response to write headers to. Static SSR form posts get one, which is how the framework's own Identity pages sign people in. Interactivity can be added per page later where it actually earns itself.
- EF Core 10 + SQLite, `whatyousay.db`. Migrations checked in, `Migrate()` on startup.
- **`DateTimeOffset` is stored as a UTC ISO-8601 string** via a global value converter. SQLite refuses to `ORDER BY` EF's default mapping, because the offset lives in the text and rows with different offsets would sort wrongly. Normalising to UTC makes it sort lexicographically — which is chronologically — and keeps it readable by hand.
- **Enums stored as strings**, not ordinals (`.HasConversion<string>()`). The point of a SQLite file is that you can open it with `sqlite3` and read it — `Anonymous` tells you something, `2` doesn't. It also means reordering enum members can never silently reinterpret existing rows.
- Markdig for the summary narrative.
- `ModelContextProtocol.AspNetCore` for `/mcp`.
- xunit over the service layer only, against real SQLite. No UI tests, no mocking.
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
  Mcp/             tool definitions
WhatYouSay.Tests/          references the domain library only
PLAN.md
```

The web project is deliberately not the root project. Tests target the domain library and have no reason to build a front end, and the split keeps the service layer honest about what it depends on — a service that needs `HttpContext` won't compile there.

## Build order

Deliberately front-loads the open question. The thing worth knowing early is whether the generated summaries are any good, so the build reaches a real summary over real data before it builds any CRUD that assumes the answer is yes.

1. **Scaffold, model, seed data.** Project, EF model, first migration, and a seeder producing surveys of deliberately different shapes:
   - a 12-response sprint retro
   - a 5-response "where shall we get food"
   - a 60–100 response company-wide one, many topics
   - a multi-day diary, as the single-author case

   The last three exist to stress the model against the non-work uses. Seeding also means `/new` doesn't have to exist yet.
2. **Minimal respond + summary read.** `/surveys/{code}` and the summary page. Just enough to get data onto a screen.
3. **MCP + grounding validation.** `/mcp`, bearer auth, the read tools, `create_summary` with the full validation rules. Then generate summaries over the seed data and read them properly.
4. **Decision point.** Are these summaries better than pasting responses into a chat? If not, this is the cheapest possible place to have found that out.
5. **Reactions.** Agree / important / misrepresents on the summary page, `list_reactions`, and the second-pass loop. This is the feature that makes the answer to step 4 "yes", so it lands here rather than in polish.
6. **Admin.** Password gate, the `ICanAdminister` seam, dashboard, response list, soft-delete, settings, close/reopen, and `/new`.
7. **Summary edit.** The forms page, publish/unpublish, delete, and prominent display of objections.
8. **Polish.** Publicly listed home page, token regeneration, empty states, and the copy telling responders exactly what is and isn't stored.

Steps 1–3 stand alone as something usable with hand-written summaries, which makes them a reasonable stopping point if the weekend runs out.

## Prior art

Checked before building. Nothing free does this combination — self-hosted, no accounts, link-shared, one free-text prompt, agent-driven summarisation over MCP — but several projects have solved adjacent pieces and are worth learning from.

- **[Talk to the City](https://github.com/AIObjectives/tttc-light-js/)** (AI Objectives Institute) — the near-hit. LLM extracts claims from free text, clusters them into topics/subtopics, links every claim to an exact quote. Independently arrived at essentially this object model, and has been used for government and union consultations. Their [write-up](https://ai.objectives.institute/blog/talk-to-the-city-an-open-source-ai-tool-to-scale-deliberation) argues the report *structure* is what mitigates LLM inaccuracy — drill-down from theme to verbatim opinion is what makes a summary trustworthy. They also concluded manual editing of AI output remained necessary and was "reasonable overhead". Both findings validate choices here. Not reusable: Next.js + Express + pipeline worker + Firebase + GCS + Redis
  + Pub/Sub.
- **[Parabol](https://github.com/ParabolInc/parabol)** — open source, self-hostable, air-gappable, AI theme-grouping for retros. Different shape: a *synchronous* meeting tool with multiplayer sticky-note grouping and accounts. Ours is async collect-then-summarise.
- **[Formbricks](https://github.com/formbricks/formbricks)** (AGPLv3) — closest on the survey side; self-hosted, open text, AI insights. But a full form builder with orgs, projects and question types. Enormous relative to "one prompt, one textarea".
- **[Fast Retro](https://fastretro.app/), QuickRetro, Postfacto** — free self-hosted retro tools with anonymous input and link sharing. Closest on the social model, no AI summarisation with quote grounding.
- **[Taguette](https://www.taguette.org/) / QualCoder** — qualitative data analysis. The intellectual ancestor of `SummaryTopicPointResponseReference`: highlight a span, tag it with a code. Academia has done this by hand for decades and calls it *qualitative coding*. Useful vocabulary if we ever want to export somewhere.

**Deliberate divergence:** Talk to the City nests topics → subtopics → claims because it processes thousands of inputs. At 6–20 responses, flat topic → point is the right call and the extra level would be noise.

### Graph-structured feedback

Background for the deferred relations feature. Four traditions have attempted this, largely independently of each other.

- **Axial coding** — the direct continuation of this tool. In grounded theory, open coding (quote → code) is followed by [axial coding](https://atlasti.com/research-hub/axial-coding): relating codes to each other with named relationships. ATLAS.ti implements precisely the idea, as "Networks", and importantly **custom relationships are saved in the project and reused across the analysis** rather than reinvented per pass. Our `SummaryTopicPoint` is a code. The sequencing lesson: relating comes *after* coding, so points need to be stable first.
- **Cognitive / causal mapping (SODA, Colin Eden, 1980s)** — the closest to our use case. Capture stakeholder statements as concept nodes, link them with causal arrows, then [merge individual maps by identifying concepts common to several people](https://www.sciencedirect.com/science/article/pii/S0377221720309784). That merge step is exactly "a graph of feedback from N people", with 35 years of practice behind it. A "stressor" relation is a causal-mapping arrow.
- **IBIS / dialogue mapping (Rittel, 1970)** — Issues, Positions, Arguments, for wicked problems; Compendium, Kialo, argdown. Relevant if the relations turn out argumentative rather than semantic. Notable that [IBIS uses a deliberately tiny fixed vocabulary](https://eight2late.com/2014/11/24/from-information-to-knowledge-the-what-and-whence-of-issue-based-information-systems/) — the constraint is the design, not an unfinished bit.
- **GraphRAG / LLM knowledge-graph construction** — the modern automated form: extract entities and relations, then [Leiden community detection and per-community summarisation](https://arxiv.org/html/2501.00309v2). Worth noting it *summarises via the graph* — the graph replaces the topic list rather than decorating it. That's the stand-alone-graph option, and it's a working architecture.

## Deferred

Not in v1, but the model shouldn't preclude them:

- **Sentiment / objectivity display.** Data is already being collected.

- **Point relations — a graph of feedback.** Typed, directed links between points: "causes", "blocks", "wants", "contradicts". See [Graph-structured feedback](#graph-structured-feedback) for the traditions this draws on.

  ```
  RelationType   { Id, SurveyId?, Name, Description, IsDirected }
  PointRelation  { Id, RelationTypeId, FromPointId, ToPointId, Note? }
  ```

  **Open vocabulary, not a fixed schema.** This tool has to serve sprint retros, holiday planning, takeaway votes, company-wide feedback and personal diaries. A vocabulary broad enough for all of those is too vague for any of them, so relation types are invented as needed and consolidated afterwards.

  The known failure mode is *canonicalization*: unconstrained extraction yields `likes`, `enjoys` and `is fond of` as three distinct predicates, giving [redundancy and inconsistency](https://arxiv.org/html/2510.20345v1). The established fix is [EDC — Extract, Define, Canonicalize](https://arxiv.org/pdf/2404.03868). Applied here in three cheap layers:

  1. **Show the agent the vocabulary before it extracts.** `list_relation_types` with usage counts, and a tool description telling it to reuse an existing type where one fits and mint a new one only when none does. Most convergence happens at write time, for free.
  2. **`merge_relation_types(from, to)`** for explicit cleanup — repoint the relations, drop the dead type. A data operation, so it's reviewable.
  3. **Sort the admin's relation-type list by usage.** Singletons are the tell: a type used once is nearly always a synonym of one used twelve times.

  `SurveyId` nullable so a house vocabulary can accumulate globally while one-off types stay survey-local. Grounding extends for free — a relation between two points is already grounded, because both endpoints are.

  **Build causal first.** At ~15 points a graph view is probably *less* legible than the list; graphs earn their keep around where a list stops fitting on a screen. The exception is causal chains — "slow CI → people batch commits → bigger reviews → slower reviews" is a real finding a flat list structurally cannot express, and it's what SODA exists for. One relation type, see if it earns the others.

- **Cross-survey summaries, via `Collection` — not accounts.**

  ```
  Collection { Id, Name, AdminPasswordHash, Surveys[] }
  Survey.CollectionId  (nullable)
  ```

  A cross-survey summary is one scoped to a collection rather than a survey. One table and one nullable FK, composing with everything already designed. This is where SODA's map-merging gets genuinely interesting: shared concepts across *sprints*, showing which stressors recur and which actually got resolved.

  **Explicitly not accounts.** Not because of the work — users, registration, login, password reset, email, invitations — but because signup friction destroys the property that makes the tool good. "Here's a link, chuck your thoughts in" stops working the moment anyone has to create an account, and so does spinning up a survey in twenty seconds. The [`ICanAdminister` seam](#the-admin-seam) exists so this stays a swap rather than a refactor.
- **Duplicating a survey** for recurring sprint feedback — and now also the answer to "we need more input after summarising", which makes it more valuable than it first looked.
- **App-initiated generation** — a "Summarise" button calling the Anthropic API directly, for colleagues who don't have an agent session. The service layer should be shaped so the MCP tools and such a button would call the same code.

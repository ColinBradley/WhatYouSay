# What You Say

Namespace / app name: `WhatYouSay`. Product name: **What You Say**.

A small, self-hosted tool for a group of people who trust each other to work out what they think about something, together. Someone raises a **topic**, and from there any of it is optional: collect free-text responses, have an AI agent draft a grounded summary of them, write the notes by hand, open the result up for reactions and comments. Built for sprint retros, dev cycle reviews and architecture feedback at work. Not a service, not multi-tenant, no user accounts.

> **Status: building.** Steps 1–9 complete. The flow is open: a topic is a prompt and everything after it is optional, a tree can be written by hand with nothing cited under it, node ids are stable, reactions and comments are separate things, and the freeze that keeps quotes honest lives on the response rather than on a topic that can never reopen. Step 10 is polish.

## Principles

- **One topic = one free-text prompt.** Title + Description *is* the question. There are no fields, no sub-questions, no form builder. Ever.
- **Everything after the prompt is optional.** Responses, an agent, a summary, reactions, comments — a topic can use all of them, or a title and a hand-written tree. The app holds the invariants that keep it honest and does not police which of the pieces you use. See [Flows](#flows).
- **Quick and dirty, but faithful.** The whole value is in the summary being a true reflection of what people actually wrote. Fidelity is the thing to protect. Anonymity is a supported option, not the point of the tool.
- **The agent drafts, the human publishes.** AI never gets the last word.
- **Anything the agent asserts is grounded in a real quote, and the app proves it.** Text the agent writes has to trace to what someone actually wrote, or it doesn't get stored. A person may assert without a quote — the asymmetry is deliberate, and [argued below](#why-a-person-may-assert-and-an-agent-may-not). What the app guarantees is not that every node is evidenced, but that every node's *distance from its evidence* is visible. See [Grounding](#grounding-the-app-validates-the-agent).
- **The group gets to answer back.** A summary nobody can object to is just one person's reading with extra steps. Anyone reading a published summary can react to its nodes and comment on them.
- **No vocabulary from one use of it.** The tool is for sprint retros, dev cycle reviews, holiday planning, takeaway votes and one-person diaries. The app's own words — labels, headings, entity names, endpoints, error text — never name any one of those. Example *content* is the exception, and earns it by coming as a varied set rather than a single case: the `/new` placeholders rotate per page load through a retro, a lunch vote, a deploy-process review, a trip, a reading group and a solo diary, so no one visit reads as what the tool is for. Vocabulary is what silently narrows a tool to the first thing it was used for.
- **Human words are the precious data, wherever they were typed.** This used to read "the responses are the precious data", on the grounds that a summary is derived and can be deleted and redrawn at any time. That stopped being true the moment [a person could write a node](#why-a-person-may-assert-and-an-agent-may-not): a hand-written tree, and every comment on it, exists nowhere else. Agent-authored nodes remain derivable and always will be. The practical consequences are that **deleting a summary version is now genuinely destructive**, and that the argument which made the summary model cheap to change — [getting the node vocabulary wrong](#node-kinds-tried-removed) cost a migration and a re-run, not anyone's words — no longer covers it.
- No frills. If a feature needs a design doc, it's deferred.

## Why not just paste the responses into a chat?

Worth answering explicitly, because if there isn't a good answer this shouldn't exist. Four things a chat session structurally cannot do:

1. **Grounded quotes.** A chat summary can't be checked. This one can't misattribute, because the app validates every quote against the response it claims to come from.
2. **It has a URL, versions and history.** Chat output is ephemeral and lives in one person's scrollback.
3. **Humans own the structure.** The summary is an artefact the group edits, not a block of text one person read once.
4. **The loop back to the group.** You cannot ask fifteen colleagues to react to a paragraph inside your Claude conversation.

The fourth is the one that matters most, and it's why [reactions and comments](#reaction-and-comment) are v1 rather than a nice-to-have. Without them this really would be a wrapper.

Only the first of the four needs an agent at all, which is the other half of why a topic doesn't require one. Three of the reasons this exists apply just as well to notes somebody typed themselves.

**This cuts against the v1 workflow, and the tension is worth holding.** Drafting and review both happen in an agent session ([flow 4](#4-summarise)), which is the same ephemeral scrollback point 2 objects to. That's fine while the *artefact* is the tree in the app and the chat is only the workshop. It stops being fine the moment the reasoning that shaped a summary lives nowhere but one person's chat history — and now that a tree can be written by hand, the answer to that is to write it in the app rather than to relax the principle.

## Locked decisions

| Decision | Choice |
|---|---|
| AI integration | REST API hosted in the app. Agent authenticates with a topic-scoped token. No API key in the app. |
| Summaries | Versioned. Each generation run creates a new one; public page shows the newest published. |
| Secrets | Everything hashed. Summariser token shown once at creation; regenerate if lost. |
| Summary structure | A tree of `SummaryNode`, depth uncapped. A node is text, references and children — [nothing types it](#node-kinds-tried-removed), and nothing records who wrote it, so grounding validates the shape of a branch rather than the sort or the author of a node. |
| Who may assert without a quote | A person, yes. An agent, never. [Argued below](#why-a-person-may-assert-and-an-agent-may-not); enforced per node in the write contract, not per author in the schema. |
| Summary editing | Explicit edit page with forms. Node ids are stable, so an agent revision preserves the nodes it isn't changing. |
| Agent write access | `Summary.IsAgentEditable`, a bool the admin owns. Publishing clears it. Not derived from `IsDraft` — [visibility and write access are different questions](#summary). |
| Response identity | `ResponseIdentity` per topic: `Required` (default), `Optional`, `Anonymous`. Immutable **once a response exists**, not from creation. Anonymous records **no timestamp at all**. |
| Response editing | Authors can edit until the response is frozen, which happens to every response when the topic closes. Admins never edit — soft-delete only. |
| Reopening | Freely. The rot guarantee lives on [`Response.IsFrozen`](#response), not on a topic that can never reopen. |
| Reactions | **In v1.** A small fixed set of light social signals, per node, from anyone who can see the topic. Conflicting ones allowed. |
| Comments | **In v1.** Free text on a node, standing alone rather than hanging off a reaction. Where a misrepresentation gets voiced. Hideable, never deleted. |
| Cross-topic work | Deferred, and solved with a `Collection` entity — **not** accounts. Auth seam built now. |
| Node relations | Deferred. What remains deferred is the *cross-cutting* link — the one a tree structurally cannot hold. Open vocabulary, unlike anything grounding keys off. |

### Response editing: a window, then frozen

Humans are squishy, slow things who will want to fix what they wrote thirty seconds after hitting submit. So authors *can* edit their own response — until it is frozen. Closing the topic freezes every response then in it, and closing is the natural step before summarising anyway.

Admins never edit responses, in any mode. Soft-delete is the only destructive power an admin gets. An admin quietly rewording someone's criticism is the single thing that would make the whole summary untrustworthy, and it's less code not to build it.

One rule follows from that and is the whole of the quote-rot guarantee:

> **A reference may only be created against a frozen response.**

Everything else about ordering is advice. This is not, because breaking it produces a summary that looks trustworthy and isn't: an offset into text that has since moved selects the wrong words while still validating.

**The freeze lives on the response, not on the topic.** `Response.IsFrozen` is set on every response when the topic closes and is never unset. Reopening therefore costs nothing — the responses already collected stay frozen forever, new ones arrive editable, and the next close freezes those too. A topic can go open → closed → open → closed as many times as its group wants.

**This replaced a much stricter rule, and the strictness was the tell.** Closing used to be one-way the moment any summary existed, so "we need more input" meant running a whole new topic. The justification was quote rot — but rot comes from *edits*, which move offsets underneath existing references, and a brand-new response moves nothing. It makes a summary [outdated](#summary), not wrong. Pinning the freeze to the response says exactly that and no more, where pinning it to the topic said it by forbidding a flow people legitimately want. The earlier version of this document already recorded that the rule was stricter than its own justification required; this is that note being cashed in.

**The author-facing story is what settles the shape.** The alternative — freeze a response the moment something cites it — is more precise and worse to be on the end of: you return to fix a typo and can't, because a stranger quoted you an hour ago. "Responses were frozen when this closed" is predictable, explicable in one line on the page, and a bool read rather than a citation lookup at every reference write. It over-freezes uncited responses, which costs nobody anything.

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

- **Insertion order still leaks through the SQLite rowid.** Not recording the date isn't enough on its own. Anonymous topics order responses by `Id`, giving a stable but meaningless order. Everything else orders by `CreatedAt`.
- **`Response.Id` must be a v4 random Guid, never `Guid.CreateVersion7`.** A v7 Guid embeds a Unix timestamp, so ordering by it would reconstruct submission order *and* leak roughly when each person answered — walking straight past the decision not to store `CreatedAt`. The other entities can use v7 happily; their timestamps are recorded anyway.
- **The Author field disappears entirely** rather than becoming optional. If the clock is worth worrying about, a name box is a far bigger leak than the clock ever was.

## Object model

Guid PKs where the id appears in a URL. `int` identity PKs elsewhere. Display order was key order for as long as only the agent wrote trees — depth-first insertion reproduced sibling order for free — and [step 8](#build-order) ended that, because a human moving a node makes insertion order a lie. Sibling order now lives in `SummaryNode.Ordinal`, scoped to a parent, with the key as the tie-break.

### Topic

Was `Survey`, renamed in [step 9](#build-order). The word described one of the entity's modes rather than the entity: a diary is not a survey, and neither is a page of meeting notes, and both were already meant to be supported. `Topic` reads for every use on the list — a retro, a lunch vote, a trip, a reading group — which is what [the vocabulary principle](#principles) asks of a name. Note that `SummaryTopic` was a *different*, since-removed thing, a node type; the collision is historical and lives only in this document.

```
Id                    Guid       PK
Code                  string     unique, ~7 url-safe chars, the public URL segment
Title                 string
Description           string     the prompt
AdminPasswordHash     string     PBKDF2 (PasswordHasher<Topic>)
SummariserTokenHash   string     SHA-256, indexed
IsPubliclyListed      bool
IsAcceptingResponses  bool
AreResponsesPublic    bool
ResponseIdentity      enum       Required (default) | Optional | Anonymous; stored as string; immutable once a response exists
CreatedAt             DateTimeOffset
Responses             Response[]
Summaries             Summary[]
```

A topic with no responses is ordinary, not a degenerate case: `IsAcceptingResponses`, `AreResponsesPublic` and `ResponseIdentity` simply describe a phase it may never enter. They stay on the entity rather than moving somewhere conditional, because a topic that starts as notes and later solicits responses needs them, and the switch has to be one toggle on the dashboard rather than a decision at creation. **Mode switching belongs on the dashboard, never in `/new`** — the twenty-second create is a property worth protecting, and a wizard asking which of four workflows you want is how it would be lost.

### Response
```
Id            Guid             PK
TopicId       Guid
Body          string
Author        string?          self-declared, unverified; absent when Anonymous
AuthTokenHash string           SHA-256 of the cookie token, indexed
IsFrozen      bool             set for every response when the topic closes; never unset
IsDeleted     bool             soft delete
CreatedAt     DateTimeOffset?  null when Anonymous
UpdatedAt     DateTimeOffset?  null when Anonymous, or when never edited
References    SummaryNodeReference[]
```

`IsFrozen` carries the [quote-rot guarantee](#response-editing-a-window-then-frozen) on its own: an author may edit while it is false, a reference may be created while it is true, and the two windows cannot overlap.

**`Body` stores `\n` line endings, normalised on write.** A browser normalises a `<textarea>` to CRLF on submission — that's in the HTML spec, not a quirk — so what arrives is not what was typed. Left alone, the CRLF reaches the summariser as an escaped `\r\n` inside a JSON string, where it is invisible, and an agent quoting across a line break reaches for `\n`, fails the exact-match check, and gets a diff that looks identical in a terminal. Same class of trap as curly punctuation, with none of the visibility.

Normalising at ingest is what makes it safe, because storage, reads, validation, quotes and offsets then all agree on one representation. Normalising only on the way out would be actively worse than doing nothing: quotes copied faithfully from the API would fail against the stored body, which is the one failure the grounding rules must never produce. Existing rows want a one-off renormalisation with the same migration, since stored offsets shift by one per preceding line.

### Summary
```
Id                    Guid    PK
TopicId               Guid
Body                  string  narrative overview, markdown. NOT a duplicate of the tree.
IsDraft               bool    true until a human blesses it
IsPublic              bool
IsAgentEditable       bool    may the summariser token write to this version
ResponseCountAtWrite  int     non-deleted responses when the agent last wrote it
CreatedAt             DateTimeOffset
UpdatedAt             DateTimeOffset
CreatedBy             string? "agent" | "human", who started this version
Nodes                 SummaryNode[]  every node in the tree, flat; roots are those with no parent
```

Summary timestamps are always recorded — a summary is a document about the group, not a trace of an individual.

Visibility: admins always. Everyone else only when `!IsDraft && IsPublic`. `/topics/{code}/summary` resolves to the newest visible summary by `CreatedAt`.

**`IsAgentEditable` is write access, and it is nobody's derived property.** It used to be `IsDraft` doing double duty — publishing both showed a version and locked the agent out of it. Separating them costs one column and buys two shapes that were unreachable: a published summary the agent is still iterating on, and a draft closed to the agent while a person works on it by hand. The default path is unchanged, because **publishing clears the flag**. Turning it back on afterwards is a deliberate act, and re-enabling it on a hand-written summary is a decision about whether you trust the model that will edit it — which is the admin's to make and not the app's to prevent.

`CreatedBy` says who started the version and stops there. With the flag decoupled, "who has touched this since" is not a question one string can answer honestly, and the version list answers the useful part by showing whether the agent currently has write access.

**`ResponseCountAtWrite` is how a summary is known to be outdated**, and it is the only mechanism that works in every identity mode — an anonymous topic records no response timestamps to compare `CreatedAt` against. Live non-deleted count differs from the stored one, and the version list says so. Nothing stores *outdated*: there is no flag, no state and no invalidation, because [the app enforces invariants, not stages](#there-is-no-whole-arc). It is a hint on a screen. The count is captured whenever the agent writes, since the agent is the actor that reads every response; a person who incorporates new responses by hand won't clear the hint, which is a cosmetic lapse rather than a corruption.

`Body` is a short narrative overview only — two or three paragraphs. The node tree is the structured truth. Keeping `Body` narrative is what stops the two representations drifting apart when a human edits one of them.

The weakness of that arrangement is that a narrative overview of a good tree risks being a summary of a summary. [Relations](#deferred) are the fix rather than a threat to it: a causal chain running across three branches is exactly the kind of finding that reads well in prose and cannot be read off the tree, so a relations pass gives `Body` content that is genuinely its own. Rewriting `Body` afterwards needs no new mechanism — publishing clears [`IsAgentEditable`](#summary), so unless an admin deliberately hands the version back the rewrite lands in a new one like everything else.

### SummaryNode

The tree. One entity replaces `SummaryTopic` and `SummaryTopicPoint`, because the only thing separating those two was depth.

```
Id           int      identity PK, stable for the life of the node
SummaryId    Guid     on every node, not just roots — one query loads the whole tree
ParentId     int?     null at a root
Ordinal      int      position among siblings; the key breaks ties
Text         string   terse; a few words to a sentence. The only thing a node says about itself
Children     SummaryNode[]
References   SummaryNodeReference[]
Reactions    NodeReaction[]
Comments     NodeComment[]
```

**Node ids are stable, and that is now load-bearing.** An agent revision no longer deletes the tree and rebuilds it; it names the nodes it is keeping by id. Three separate things were breaking without this and all three are the same problem — a whole-tree replacement destroying node identity:

| Broken by a rebuild | Because |
|---|---|
| Hand-written nodes | an agent iterating over notes it cannot cite would have to drop them or fail validation |
| Reactions and comments | they hang off `NodeId` and cascade away with it — and [`IsAgentEditable`](#summary) means a *published* summary carrying both can now be agent-written |
| A person editing during an agent write | the tree they are looking at gets new ids underneath them |

This was listed as deferred for a long time, on the grounds that the cost was tokens and the need hadn't arrived. It arrived three times at once. See the [write contract](#the-write-contract-text-or-id-never-both) for the shape it takes in the payload.

**Nothing on a node records who wrote it.** Considered and rejected: an author field would be muddied the first time a person edited an agent's node or an agent revised a person's, and it turns out to buy nothing. The three states a reader needs — cited directly, inherited from an ancestor, nothing anywhere in the branch — are all derivable from references alone. And the grounding rule that keeps the agent honest is [about the text in a payload, not about the author of a row](#the-write-contract-text-or-id-never-both), so it doesn't need one either.

**Adjacency list, stitched in memory.** EF cannot eager-load an arbitrary depth, so a read pulls every node for the summary in one query filtered on `SummaryId` and assembles the tree in code. That is why `SummaryId` sits on every node instead of being inferred up the parent chain. A summary is a few hundred nodes at the outside, so there is no closure table, no materialised path and no recursive CTE — and this is less code than the two-level `Include` chain it replaces.

**No `Description` field.** A child node *is* the description. That is the shape real notes take — "UI testing is hard" with "Bothersome to maintain" underneath it, rather than one node carrying a paragraph — and it is what keeps nodes terse enough for depth to stay readable. Elaborating means descending.

**Depth is not capped.** Five levels is a lot and usually means the middle is thin, but a genuinely detailed topic earns it, and any limit that suits a five-response takeaway vote is wrong for a project plan. The [brief](#api-surface) says as much as guidance; validation does not enforce it. The admin UI shows node count and maximum depth instead, so a staircase is visible without being illegal.

**The QDA tradition disagrees**, which is worth recording rather than leaving as an unexamined difference. The standard advice for a code hierarchy is [not to nest more than three levels deep, and not to force codes into a hierarchy at all](https://support.alfasoft.com/hc/en-us/articles/360005281737-How-to-create-a-good-code-structure-in-NVivo). We reject the first half, because a codebook is a *retrieval index* applied across a corpus: depth costs a coder something every time they reach for the right code, where a summary tree is read top to bottom, once, by someone who did not build it. Different job, different cost curve. We take the second half — a one-off sibling next to a deep subtree is correct rather than sloppy, which is what an "Other" heading full of unrelated single nodes is for.

**Single-child nodes are legitimate.** "Puzzling things → What's next for 3D? → What's the plan?" sharpens a heading into a question in two steps and reads correctly. The staircase worth worrying about is the one with nothing at the bottom, and that is a judgement, which is exactly why it is guidance rather than a rule.

**A node has text, references and children, and nothing else.** No type, no scores. What a node means has to be in its text, which is also what the [brief](#api-surface) tells the agent: "A real budget held by the team" reads as a fact, so if it is something people want, write it as one.

**Grounding inherits down a branch.** A node with no references of its own resolves to its nearest cited ancestor's. Demanding a fresh citation at every level would only make the agent copy one quote four times — more tokens, one more chance to corrupt it per repeat, no more truth than citing it once. What must hold is that **every branch ends in a citation**: a leaf either cites for itself or sits under something that does.

The honest cost: a fabricated claim four levels under a real quote rides on that quote. Requiring re-citation would not catch it either, since the agent is holding the quote already. So the mitigation is display rather than validation — inherited support renders visibly weaker than direct support, and the human reviewing the draft can see how far a claim sits from its evidence.

**A third state exists now, and it is the one the display has to earn.** A branch that ends in nothing cited anywhere was previously unstorable and is now legal, because [a person may assert without a quote](#why-a-person-may-assert-and-an-agent-may-not). So every node renders as one of three things, and the difference between them is the entire remaining guarantee:

| State | Renders as |
|---|---|
| cites a response itself | the strong case; the quote is one expander away |
| inherits from an ancestor | visibly quieter — supported, at a distance |
| nothing cited in the branch | plainly unevidenced; somebody's assertion, presented as one |

The third must never be renderable as the second. Inheritance is what a node borrows from *above* it, and a node with nothing above it has borrowed nothing — showing it as merely-quiet support would be the app lying about evidence, which is the one thing it exists not to do.

#### Node kinds: tried, removed

A `Kind` on every node — `Frame | Facet | Claim | Detail | Question | Want` — shipped with the tree and was taken out again after a run of fresh drafting sessions. The reasoning for it was sound and is worth keeping: two levels used to supply that typing for free, a topic node asserted nothing while a point had to be cited, and in an untyped tree an unreferenced node in the middle becomes the obvious hiding place for a claim nobody made, wearing a heading's clothes. `Kind` put the distinction back and made it checkable at any depth.

What killed it was not the theory but the drafting. **A vocabulary offered to an agent is a vocabulary it tries to satisfy.** Six named boxes turned out to be leading — the agent reached for a kind and then wrote a node to fit it — restrictive where the honest node was between two of them, and confusing in a way that cost attention the quotes needed more. Worse, the label leaked into the prose: nodes were written as sentence fragments completed by their kind, so `{ "text": "A real budget held by the team.", "kind": "Want" }` reads as a fact everywhere the kind is not also on screen. Six kinds also meant six chances to pick the wrong one, in a payload where the field was `required` and a miss was a bare `400`.

**What replaces it is one rule instead of two:** every branch of the tree ends in a citation. A node with children reads as the heading over them and needs none of its own, because the requirement lands on what hangs below it; a childless node with nothing above it cited is rejected whether it was meant as an empty section or an uncited finding. The untyped rule cannot tell those apart, and does not need to — both are wrong.

**The cost is exactly the one the typed design predicted.** An uncited node in the middle of a branch can now carry a claim nobody made, and nothing checkable stops it. That is a real loss of the [grounding](#grounding-the-app-validates-the-agent) guarantee, accepted because a rule the agent routinely trips over protects less in practice than a rule it can follow. The mitigations are the ones already there: inherited support renders visibly weaker than direct support, and a human publishes. The third mitigation used to be that summaries could always be redrawn from the responses; [that one is gone](#principles), and its loss is the price of letting people write nodes.

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

### Reaction and comment

Two entities, because they are two different acts. A reaction is a click; a comment is a sentence. They used to be one row with an optional `Note`, which meant you could not say anything without first picking a label for it, and could file the label without saying anything.

```
NodeReaction
Id                int              identity PK
NodeId            int
ReactorTokenHash  string           SHA-256 of the wys_resp cookie token
Kind              enum             stored as string
CreatedAt         DateTimeOffset?  null when Anonymous

NodeComment
Id                int              identity PK
NodeId            int
AuthorTokenHash   string           SHA-256 of the wys_resp cookie token
Author            string?          per ResponseIdentity, exactly as a Response
Body              string
IsHidden          bool
CreatedAt         DateTimeOffset?  null when Anonymous
```

Reactions stay unique on `(NodeId, ReactorTokenHash, Kind)` — each is an independent toggle, one click on and one click off.

**Reactions are a chat reaction bar, and they are meant to be light.** The starting set is agree, disagree, important, question, celebrate, laugh. It is not an analytics channel and should not be read as one: someone hits *disagree* to flag a thing worth raising, not to file a dissent. **Conflicting reactions are allowed** — agree and disagree together is a person being a person, and the important property is that removing one is as easy as adding it. `Kind` is [stored as a string](#tech), so the set can be extended without a migration; retiring one wants a data fix for the rows already using it.

**Every node takes reactions.** Agreeing with "What went well" means nothing, but with [kinds gone](#node-kinds-tried-removed) there is nothing on a node that says it is a heading, and inventing a proxy — has children, has no references — would put reaction controls in the wrong place on a tree that does not obey the proxy. A meaningless control is cheaper than a missing one: what must not happen is a node someone wants to object to arriving without the button.

**Comments are where a misrepresentation gets voiced**, and they are deliberately untyped. There used to be a `Misrepresents` reaction kind, described here as the fidelity loop closing and given prominent treatment in the editor rather than a counter in a corner. Dropping it costs a countable signal for the exact failure this tool exists to catch, which sounds like a serious objection and isn't one, because the button was never going to be pressed. Colleagues do not click "you have put words in my mouth". They hedge, especially at work, and what they actually write is three sentences that don't reduce to a flag — so the countable signal would have counted almost nothing while the real objections went unsaid for want of a place to put them. Six comments saying a node is off is perfectly legible without a counter above it, and reducing them to positive-or-negative would throw away the qualitative content that is the reason to read them. The tool is [for people](#principles); this is where that showed up most sharply.

**A comment is joined back to its author's own response** where they gave one — the cookie doing double duty, exactly as the reaction keying used to. That is what lets an admin read *"this is what they wrote, and this is what they say the summary did with it"* side by side, which was the whole value of the old flag and survives the move intact. It discloses nothing new: `Required` mode carries a name on the response anyway, and `Anonymous` mode stays a nameless response id. A comment from someone who never responded simply has no response beside it.

**Comments are hidden, never deleted.** An admin or the comment's own author can hide one. Stale comments are the price of [stable node ids](#summarynode): a "this misrepresents me" now outlives the edit that fixed it, and reads as a live objection when it is a settled one. Snapshotting the node text onto the comment was the considered alternative and is more machinery for a worse outcome — it preserves an argument nobody needs to relitigate, where hiding closes it. This is what Confluence does with resolved comments and for the same reason. **An agent can never hide a comment.** It is the group's channel for saying the summary is wrong; a model that could tidy those away would be marking its own homework.

**Anyone who can see a topic can react and comment.** This overturns the older rule that only responders could, which rested on two things: `Misrepresents` being meaningless from someone who said nothing, and passers-by diluting the signal. The first argument left with the reaction kind. The second presumes an audience that does not exist — access is a shared link among people who trust each other, and a topic that is nothing but hand-written notes has no responders at all, so the old rule made the notes-only case unreactable. The cookie stops being a permission check and becomes purely a dedupe key, set on first reaction for someone who never responded.

**Comments follow `ResponseIdentity` exactly as responses do**, including the anonymous rules: no author field at all, no timestamp, ordered by `Id`. A comment carries more identifying material than a reaction ever did, so it gets the same treatment as the thing it most resembles rather than a weaker one of its own. Where the commenter has a response, their name is prefilled from it.

## Auth & secrets

Three secrets, three different treatments, for three different reasons.

| Secret | Storage | Why |
|---|---|---|
| Admin password | PBKDF2 via `PasswordHasher<Topic>` | Human-chosen, therefore reused elsewhere. Needs a slow KDF. |
| Summariser token | SHA-256, indexed | 256 bits of entropy. A slow KDF here would just make every API request slow for zero security gain. |
| Response auth token | SHA-256, indexed | Same reasoning. |

No ASP.NET Identity. Cookies are signed/encrypted with `IDataProtector`:

- `wys_admin_{topicId}` — set after a correct password, sliding 12h expiry.
- `wys_resp_{topicId}` — long-lived, holds a plaintext token. Set on submitting a response, and also on a first reaction or comment by someone who never responded, since [reacting no longer requires having responded](#reaction-and-comment). It authorises editing your own response while it is unfrozen, shows you your own submission on a return visit, dedupes your reactions, and lets you hide your own comment. Lose the cookie and you lose all four — acceptable, given the alternative is accounts.

### The admin seam

Admin checks go through one service — `AdminSession.CanAdministerAsync(topicId)` — never by reading cookies inline in pages. Today it's backed by the topic password cookie. When cross-topic work arrives that class learns to check a collection password, and if accounts are ever genuinely warranted it's one class to rewrite rather than a hunt through every page. Costs nothing now; removes the refactor that would otherwise be the reason not to add collections later.

**A class, not an interface.** The seam is the centralisation, not the abstraction. `Topic.CollectionId` is nullable in the deferred design, so a collection-aware check is a branch *inside* this class rather than a second implementation chosen at composition time — and nothing mocks it, because the tests reference the domain library and this lives in Web. An interface here would be ceremony over a single implementation that is never selected between.

The summariser token is generated at topic creation and **displayed exactly once**, on the post-creation screen, with a copy button and a clear warning. Admins can regenerate it from settings, which invalidates the old one.

## Routes

Plural `/topics/{code}`, following the Rails-style resource convention that most web frameworks inherited: `/topics` is the collection, `/topics/{code}` is one item in it.

### Public
```
/                                     home — publicly listed topics, "New topic", open-by-code box
/topics                              redirect to /
/new                                  create a topic
/topics/{code}                       the prompt, and whatever this topic is currently for
/topics/{code}/summary               newest visible summary
/topics/{code}/summary/{id}          a specific version
/topics/{code}/responses             raw responses, only if AreResponsesPublic
```

**`/topics/{code}` has to decide what it is.** It was the response form and can no longer assume that: a topic accepting responses leads with the form, one that is closed with a visible summary leads with the summary, and one that is closed with nothing published says so. Whichever it leads with, the other is a link rather than a hidden thing — the [disable-don't-hide rule](AGENTS.md) applied to a page instead of a control.

### Admin
```
/topics/{code}/admin                 password gate, then dashboard
/topics/{code}/admin/responses       list, soft-delete
/topics/{code}/admin/settings        toggles, regenerate summariser token
/topics/{code}/admin/summaries       version list, publish/unpublish, delete, new empty
/topics/{code}/admin/summaries/{id}  the edit page, and agent write access
```

**"New empty" is the entry point for a hand-written tree**, and until [step 9](#build-order) it was in this list and unbuilt — every summary that has ever existed was born from an agent `POST`. It creates a summary with no nodes, `CreatedBy = "human"`, and [`IsAgentEditable`](#summary) off, on a topic in any state at all. No grounding check, because an empty tree has no branches, and no response check, because it cites nothing.

### Machine
```
/api/topics/{code}/ai-summary-start  the whole job as plain text; where an agent starts
/api/topics/{code}                   topic prompt, settings and counts
/api/topics/{code}/responses         live responses, paged, with the id to cite each by
/api/topics/{code}/summaries         version list; POST creates a draft
/api/topics/{code}/summaries/{id}    one version in full, node ids included; PUT revises it
/api/topics/{code}/summaries/{id}/reactions
/api/topics/{code}/summaries/{id}/comments

Topic in the path, `Authorization: Bearer <summariser token>` in the header.
```

`GET` of a version returns node ids, because the agent needs them to revise without destroying the tree — see [the write contract](#the-write-contract-text-or-id-never-both). `PUT` requires [`IsAgentEditable`](#summary) rather than `IsDraft`. Comments are readable by the agent for the same reason reactions are: they are what the group said about the last draft, and drafting the next one without them is the loop not closing. Neither is writable by it.

## Flows

### There is no whole arc

There used to be one — **collect → close → summarise → ratify → react → relate → rewrite** — and it was accurate about the flow the tool was built for and wrong about the tool. The pieces after the prompt are independent, and a group reaches for whichever it wants:

| Shape | What happens |
|---|---|
| **Collect first** | open, collect, close, agent drafts a grounded tree, publish, the group reacts. The original arc, still the common one |
| **Notes first** | create, write the tree by hand in the editor, publish. Responses are never opened; reactions and comments are the whole of the feedback |
| **Notes, then ask** | notes by hand, published, *then* open for responses — close, and let the agent draft a version that cites them |
| **Live** | responses open and someone typing nodes as people talk. The notes cite nothing yet, because [nothing is frozen yet](#response-editing-a-window-then-frozen); citations come after the close |
| **Solo** | one person, one topic, a tree, no responses and no audience. The diary case, and the one that proves none of the rest is required |

**The app enforces invariants, not stages** — the sentence that survived the arc it was written to qualify, and the reason opening the flow up cost so little. Only one kind of rule earns enforcement: the kind where breaking it produces a summary that *looks* trustworthy and isn't. Everything else belongs to whoever is running the thing.

| Enforced | Why |
|---|---|
| A reference only against a frozen response | quotes taken from a moving target rot. [The whole guarantee](#response-editing-a-window-then-frozen), and the only one about ordering |
| No response editing once frozen | the same rule from the other end |
| Text an agent writes must be cited | [the asymmetry](#why-a-person-may-assert-and-an-agent-may-not); a fabricated finding is the failure this app exists to prevent |
| The agent writes only where `IsAgentEditable` | human edits are never stomped by accident |
| An agent never hides a comment | it is the group's channel for objecting to the agent's work |

| Not enforced | Why not |
|---|---|
| Whether responses are ever collected | a topic is a prompt; everything after it is optional |
| Whether an agent is involved at all | three of the [four reasons this exists](#why-not-just-paste-the-responses-into-a-chat) don't need one |
| Whether a hand-written node is evidenced | a person may assert; the display says they did |
| Reopening, and how often | the freeze is on the response, so reopening threatens nothing |
| Who may react or comment | the shared link is already the access boundary; a second one inside it protects nothing |
| Whether a summary is redrawn once it is outdated | a stale reading is a lapse, not a corruption |
| Whether relations happen, or ratification, or a rewrite | the app cannot tell whether they did and should not try |

The test for the left column: does violating it produce something that reads as trustworthy and isn't? Then it belongs in the schema or the service layer. If it merely produces a worse process, it belongs in the brief, or nowhere. Two rows moved right in [step 9](#build-order) — reopening, and who may react — and both moved because the reason they were on the left had quietly stopped being true.

### 1. Create a topic
1. `/new` — title, prompt, admin password, the four toggles.
2. Submit. Topic created, `Code` generated, admin cookie set immediately.
3. Land on a confirmation screen showing: the share link, and the summariser token with a copy button and "this is the only time you'll see this".
4. Continue to the admin dashboard.

`ResponseIdentity` is immutable **once a response exists**, not from creation. The reasoning against changing it is entirely about responses already given: switching to `Anonymous` later can't retroactively unrecord timestamps or unsay names, and switching away from it mid-topic silently changes the deal earlier responders agreed to. At zero responses that argument is vacuous, and holding the rule anyway would trap a topic that started as hand-written notes with whatever default it was born under — the exact case [opening the flow up](#flows) exists to serve. Settings render it editable while the response count is zero and read-only forever after.

### 2. Respond
1. `/topics/{code}` — prompt, textarea, name field per `ResponseIdentity`, and a plain-English line stating exactly what is stored.
2. Submit. `wys_resp_{topicId}` cookie set.
3. Confirmation inline, with a link to the summary if one is visible.
4. Returning with the cookie shows your own response back to you. While it is unfrozen it's editable in place, with a withdraw option; once frozen it's read-only with a one-line note saying why.
5. If `!IsAcceptingResponses`, the form is replaced by a closed notice plus a summary link.

### 3. Admin
1. `/topics/{code}/admin` with no cookie → password prompt.
2. Correct password → cookie set, dashboard: response count, summary versions, share link, quick toggles, and **Close topic** as the primary action once responses are in.
3. Closing is presented as the gateway to citing rather than as a settings checkbox — "Close and summarise", explaining that it freezes responses so the quotes stay accurate. Reopening is always available and says what it does and doesn't do: new responses can arrive, the ones already frozen stay that way.
4. Responses list: anonymous topics show no times at all and order by `Id`; everything else shows real timestamps in submission order, with an "edited" marker where `UpdatedAt` is set.
5. The dashboard is **where a topic changes shape** — open or close responses, start an empty summary to write by hand, hand the agent write access or take it back. None of that belongs in `/new`.

### 4. Summarise
1. Admin copies the base URL, topic code and summariser token into their agent session.
2. Agent calls `list_responses`, thinks, calls `create_summary` with the whole tree in one shot.
3. A **draft** summary appears in the version list, attributed to "agent", with `IsAgentEditable` on.
4. **Review happens in the agent session**, with one or two people reading the tree in chat and asking for changes. The agent revises with `PUT` each round, [naming by id what it is keeping](#the-write-contract-text-or-id-never-both) rather than rebuilding the tree.
5. Admin opens the edit page, makes whatever corrections are left, then publishes.
6. Publishing sets `IsDraft = false` and clears `IsAgentEditable`. The agent can no longer touch that version until an admin deliberately hands it back. Human edits are never stomped by accident.
7. The group reads the published summary, reacts, and comments. If comments say the tree has misread people, the admin runs another pass — the agent reads the reactions and comments and drafts a version answering them. Versioning makes this safe, and the old version stays readable alongside the comments that prompted the new one.
8. **Optionally, a relations pass** over that second draft — [a separate stage](#deferred), deliberately after the group has seen the tree once. Not every topic wants one.

### 5. Write a summary by hand
1. Admin dashboard → summaries → **New empty**. No responses required, no agent required, and it works on a topic that has never accepted a response.
2. The editor is the same one the agent's drafts land in. Add nodes, nest them, reorder them; cite a response where one exists and there is something worth citing, or don't.
3. Publishing does not refuse an uncited tree. It used to, and that was right while only the agent could write nodes; a hand-written tree that had to cite something would be unwritable on a topic with nothing to cite.
4. Handing the agent write access afterwards is one toggle, and the agent's own rules apply to it from then on: it can restructure and add, and every node whose text it supplies has to be cited.

### 6. Read a summary
1. `/topics/{code}/summary` — narrative overview, then the tree as a nested list.
2. Each node: its text. Roots read as headings because that is what the top of a tree is, not because the node says so — depth is the only thing left to render by.
3. A node that has references carries an expander showing the supporting quotes. A node on inherited support says so, visibly more quietly. A node with nothing cited in its branch reads as [plainly unevidenced](#summarynode) — not as a quiet version of supported.
4. If responses are visible to you, each quote links through to the full response with the quoted span highlighted.
5. Every node carries the reaction bar and a comment thread, for anyone who can see the page. Counts and comments are visible to everyone; hidden comments are visible to the admin and to whoever wrote them.
6. Deep branches collapse below a sensible level rather than indenting off the screen. Which level is a rendering decision, not a stored one — nothing about depth is a property of the data.

## API surface

The topic is named in the path and the token travels in an `Authorization: Bearer` header, so the two vary independently: a longer-lived or differently scoped credential later does not change any URL. A token reaches exactly one topic.

An agent starts at `GET /ai-summary-start`, which returns the rules, the payload shape, the other endpoints and the topic's current state as plain text. Keeping the instructions server-side means they are versioned with the code, so improving them does not require anyone to re-paste a prompt.

Five things the brief carries that came out of watching an agent actually use it:

- **Name people who gave a name.** A response carrying an author may be attributed; an anonymous one is referred to as a response and nothing more. Unstated, the agent invents a policy per run, and the choices are not interchangeable — attributing only the named half of an `Optional` topic makes one group's opinions accountable while everyone else's stay deniable, which is a decision the tool should be making, not the drafting agent.
- **Check every quote is a substring of the response body before sending.** The agent already holds `/responses` in context, so this is a free local check that eliminates a whole class of `422` before it reaches the wire. Cheaper than a dry-run endpoint and strictly better than one, because it costs no round trip and can be done while drafting rather than only at the end.
- **Build the payload with code if you can; write it out directly if you can't.** An agent with an interpreter should construct the JSON programmatically and run the substring check before sending; one without should emit it and re-read each quote against the response first. Two sentences cover both kinds of agent, which is the cheap alternative to accepting a second input format for the benefit of the second kind.
- **Start a new summary; don't continue an old one.** A run produces a new version. `PUT` is for iterating within a session, not for picking up a draft somebody else left — a version being writable is the admin's decision, not an invitation.
- **You may find nodes you cannot cite, and that is not a defect to fix.** A person wrote them. Restructure them, nest them, move them; do not silently delete them because they are unsupported. There is nothing in the tree that says who wrote what, so the operative rule is the [write contract](#the-write-contract-text-or-id-never-both) rather than a rule about authorship: carry a node forward by its id and it survives untouched, supply text for it and that text is yours to cite.
- **Nothing about how many nodes to produce, and no depth limit.** Deliberately absent. The right shape for a five-response takeaway vote and a sixty-response company review are not the same, and any general rule would be wrong for one of them. That tailoring belongs to whoever is prompting, per topic. The brief does carry the one piece of guidance that generalises: go as deep as the detail warrants, keep each node terse, and treat a long chain of single children as a sign the middle of it is thin. Guidance, not validation — see [SummaryNode](#summarynode).

**Read**
- `GET /` — title, prompt, settings, response count.
- `GET /responses` — id and body for all non-deleted responses; author and createdAt only when the topic isn't anonymous. The API gets no privileged view of anonymised data, because there isn't one to have. **Paged**, with the total always returned so an agent knows what it is dealing with before it starts. The 60–100 response topic is a real shape, and one unbounded array is both a context problem and an obstacle to splitting the work: an agent farming extraction out to sub-agents needs slices it can name and hand over. Plain `skip`/`take` is enough here — no cursor. An agent pages this collection in order to cite it, and [only a frozen response can be cited](#response-editing-a-window-then-frozen); a response arriving mid-page can shift the window but cannot change anything already read, so the drift that cursors exist to solve does not occur.
- `GET /summaries` — versions with id, createdAt, isDraft, isPublic, isAgentEditable, createdBy.
- `GET /summaries/{id}` — the full node tree with its references, nested, **each node carrying its id**. The ids are not decoration: they are how the agent revises without destroying anything, so this endpoint is a prerequisite for `PUT` rather than a convenience.
- `GET /summaries/{id}/reactions` — per-node counts.
- `GET /summaries/{id}/comments` — every visible comment in full, with the commenter's own response beside it where they gave one. This is what closes the loop: a second pass can be asked to *answer what people said about the last draft* rather than starting cold, which is a far better prompt than "try again". Comments are the highest-value input the agent can have and they only exist because the group answered back. Hidden ones are not served — they were closed on purpose.

**Write**
- `POST /summaries` — one shot, nested payload, creates a draft and returns its id and edit URL. One atomic request beats `add_node` chatter: no partial state, no ordering to coordinate, and the agent gets to think about the whole structure at once. Every node in it is new, so every node in it must be cited.
- `PUT /summaries/{id}` — **only while `IsAgentEditable`**, which publishing clears. See [the write contract](#the-write-contract-text-or-id-never-both).

Neither refuses on `IsAcceptingResponses` any more. They don't need to: a reference against an unfrozen response is refused per reference, which is the actual invariant, and it produces a better error — *this response is still editable, so it cannot be cited yet* names the thing that is wrong instead of the stage the topic is in. A wholly uncited agent payload is refused anyway, by the rule that its text must be grounded.

**Deliberately absent:** deleting or editing responses, publishing a summary, hiding a comment, changing topic settings, reading the admin password. The token is a summarising capability, not an admin capability.

### The write contract: text or id, never both

`POST` builds a tree from nothing. `PUT` revises one that exists, and the payload distinguishes what the agent is authoring from what it is merely keeping:

| In the payload | Means | Grounding |
|---|---|---|
| `id` + `text` | revise this node | the text is a new assertion — cite it |
| `id`, no `text` | keep this node as it stands | none required; nothing was asserted |
| `text`, no `id` | a new node | cite it |
| an existing id, absent | delete this node | — |

Position and nesting come from the payload's shape in every case, so a bare `{ "id": 47 }` can be moved, reparented and reordered freely. An id must belong to *this* summary, checked the same way a `responseId` is checked to belong to this topic.

**The bare-id form is load-bearing, not a convenience.** Without it, keeping a node would mean re-emitting its text, and re-emitted text needs a citation — so an uncited node written by a person could never survive an agent pass at all. It also cuts the tokens and, with them, [one chance per repeat to corrupt a quote](#deferred).

**The rule is about text, not about authorship.** Nothing stored says who wrote a node, and nothing needs to: whatever text an agent puts in a payload is an agent assertion, whoever wrote the row before. The consequence worth naming is that **an agent cannot reword an uncited node**. It may carry it, move it, nest something under it or delete it, but the moment it supplies replacement text that text needs a quote. That is stricter than it first looks and it is the right strictness — a model quietly sharpening "we should maybe try pair programming" into "the team wants pair programming", with no citation and nothing recording that it was ever anyone else's sentence, is precisely the failure everything else here is arranged to prevent. A person fixing their own typo does it in the editor.

### Why a person may assert, and an agent may not

The asymmetry looks arbitrary and isn't. Three reasons, in ascending order of weight:

1. **Accountability.** A person writing an unevidenced node is somebody putting their name to a claim in a group that knows who they are. Even under the admin password there is a human holding it. An agent's unevidenced node has no author to answer for it.
2. **Volume.** A person types one node and feels the weight of it. An agent emits forty at once, cheaply, and the forty are visually indistinguishable from the forty that are cited.
3. **The failure mode is specific and documented.** [Models that cite more tend to cite less accurately](#grounding-the-app-validates-the-agent); fabrication and misattribution are the normal failure, not the edge case. The constraint exists because that is the thing being defended against, and a person hand-writing a node is not that thing.

The constraint also costs the two parties completely different amounts. For a person it removes an option they rarely want; for an agent it is the entire reason to trust the output.

And it is nearly free at the boundary, which is the part that made it easy to accept: **the grounding rule the agent faces is unchanged.** Everything it writes is validated exactly as before. What moved is on the human side — the editor stopped enforcing the agent's rule on a person, and publishing stopped refusing. The API changed around this rather than because of it: `PUT` learned [the id form](#the-write-contract-text-or-id-never-both) and both write endpoints check frozen-ness per reference instead of per topic.

### Why nested JSON

Worth recording, because both alternatives look attractive and both are wrong for reasons that aren't obvious until you try them.

**Not markdown.** Models are more fluent in markdown than JSON, and a strict text format is a tempting way to lower the bar for an agent writing the payload out by hand. It fails on the one field that matters. Markdown has no lossless container for verbatim text: blockquote `>` needs stripping per line and breaks when a quote begins with `>`, fenced blocks break when a quote contains a fence, backticks break on backticks, and trailing whitespace is both meaningful and invisible. Real quotes contain straight double quotes and line breaks. JSON has exactly one encoding for any string and every parser agrees on it — precisely what the field whose entire contract is character-exactness needs. Markdown is lossy by design, which is a virtue everywhere except here.

**Not flattened.** Sending a flat list of nodes each naming its parent removes the nesting and looks like it makes the payload easier to emit. It converts a structural error into a silent one: a mistyped parent name spawns a spurious node instead of failing loudly, and with a tree it can also produce a cycle. Nesting makes both unexpressible — the same reasoning that keeps offsets out of the contract. Prefer the shape where the mistake cannot be made over the shape that is marginally easier to type.

**The recursion costs us one property, and it is worth naming.** A two-level schema made "too deep" unexpressible; a recursive one cannot. Depth guidance therefore moves out of the shape and into the brief, where it is advice an agent can ignore. That is a real loss, accepted because the alternative — a cap in the schema — was measured against a real set of notes and would have truncated them at five levels. Grounding is what the shape still enforces, and grounding is the property that matters.

The cost markdown was meant to address is real: nesting is genuine load for an agent emitting a long payload token by token, and rejection is all-or-nothing. It is answered in the brief instead, with two sentences telling an agent to build the payload with code where it can — no second format to maintain.

### Grounding: the app validates the agent

The consistent finding in the attribution literature is that models which cite more tend to cite *less* accurately — fabricated and misattributed quotes are the normal failure mode, not an edge case. The recommended mitigation is boring: validate every citation programmatically and reject the ones that don't resolve.

We're unusually well placed to do this, because every quote is a span into text we already own. So the write endpoints **validate rather than trust**:

1. **Every branch of text the agent wrote resolves to a reference.** A leaf either carries references of its own or inherits its nearest cited ancestor's. A node with children needs none, because the requirement lands on what hangs below it. Enforced in the schema, not just the prompt: a branch with no citation anywhere in it is the agent inventing a theme nobody raised. This is weaker than the [typed rule it replaced](#node-kinds-tried-removed), which could demand a citation from the topmost *asserting* node rather than only from the bottom of the branch. Nodes [carried forward by bare id](#the-write-contract-text-or-id-never-both) are outside the rule and can ground a branch below them only if they are themselves cited.
2. **Every `Quote` must actually occur in that response's `Body`.** Substring match after [normalisation](#match-normalisation), and what gets stored is the span taken from `Body` rather than the string the agent sent.
3. **Offsets are not accepted at all.** The request contract has no offset fields — the app locates the quote itself. That is stronger than validating supplied offsets and correcting them, because a wrong offset stops being something an agent can express. If a quote occurs twice in one response the first occurrence wins, which nobody has to think about. Testing later supplied a second and better reason to refuse them; see [below](#match-normalisation).
4. **Every `ResponseId` must belong to this topic, be frozen, and not be soft-deleted.** Frozen is the [rot guarantee](#response-editing-a-window-then-frozen), and it lands here rather than as a check on the topic's state.
5. **Every node `id` must belong to this summary.** Same shape of rule as 4, and the same reason: an identifier from elsewhere is either a mistake or a way to reach across a boundary.
6. **Failures reject the whole call** — atomically. No partial summaries.

This makes fabricated quotes structurally impossible rather than merely unlikely, and it costs maybe fifteen lines. Requiring references up front also forces extract-then-summarise ordering, which is independently the thing that most reduces hallucination.

**Two implementations, and they have stopped doing the same job.** The rule above runs over a submitted payload and rejects the whole call. [`SummaryGrounding`](#summarynode) runs the branch rule over a *stored* tree, and it used to be the same gate in a second place — publishing refused while any branch was uncited. It no longer refuses anything. It classifies: which nodes are cited, which inherit, which have nothing. The editor shows the count and the summary page renders the three states differently. A validator became a classifier, which reads like a weakening and is the point — the guarantee moved from *this cannot be stored* to *you can see exactly what this rests on*, and only the second of those was ever available to a person writing their own notes.

Rejections are counted as `whatyousay.summaries.rejected` tagged by reason — the headline number for how often an agent tries to cite something it cannot substantiate, and so for whether any of this is worth the trouble.

A rejection has to be as useful as the rules are strict, or the agent retries with the same mistake. Validation therefore collects *every* problem in one pass rather than stopping at the first, and answers `422` with each failure located by a JSON Pointer into what was sent. For a quote that missed, the response carries the text the quote was probably reaching for, copied exactly, plus the first character where the two diverge, named by codepoint. Normalisation removes the curly-punctuation class of failure outright, so what still reaches a `422` is a genuine miss — a dropped word, a run-together line, a half-remembered sentence — and there, naming the diverging character remains the difference between a one-shot fix and a retry loop.

#### Match normalisation

Scheduled in [step 9](#build-order), with the rest of the summariser hardening. Rule 2 compares *normalised* text rather than raw text: both the submitted quote and the body are normalised for the comparison only, and what gets stored is the canonical span taken from `Body` — never the string the agent sent.

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

Spans and metrics carry timestamps by construction. Enabling ASP.NET Core instrumentation naively records `url.path` = `/topics/allco26` next to a timestamp for every request, which reconstitutes exactly the per-topic submission log that anonymous mode gives up `CreatedAt` to avoid. The observability stack would quietly undo the privacy design.

Two rules close it:

1. **The topic code is stripped from recorded request paths** by a span processor. `http.route` keeps the template, so debugging still works.
2. **Domain spans and metrics never take a raw code.** They go through `WhatYouSayTelemetry.TagFor(topic)`, which substitutes `(anonymous)`, so the rule lives in one place instead of at every call site.

This reduces the leak rather than eliminating it — with a single anonymous topic running, request timing still says something. It stops the trace store being a per-topic log, which is the part that matters.

```
WhatYouSay.sln
Directory.Packages.props   central package management; no versions in csproj files
global.json                opts dotnet test into Microsoft.Testing.Platform mode
WhatYouSay/                domain library — the root name belongs to the actual thing
  Data/            WhatYouSayContext, entities, migrations
    Seed/          dev-only seed topics of varying shape and size
  Services/        TopicService, ResponseService, SummaryService
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

1. **Scaffold, model, seed data.** *(done)* Project, EF model, first migration, and a seeder producing topics of deliberately different shapes:
   - a 12-response sprint retro
   - a 5-response "where shall we get food"
   - a 60–100 response company-wide one, many themes
   - a multi-day diary, as the single-author case

   The last three exist to stress the model against the non-work uses. Seeding also means `/new` doesn't have to exist yet.
2. **Minimal respond + summary read.** *(done)* `/topics/{code}` and the summary page. Just enough to get data onto a screen.
3. **API + grounding validation.** *(done)* `/api/topics/{code}`, bearer auth, the read endpoints, `POST /summaries` with the full validation rules. Then generate summaries over the seed data and read them properly.
4. **Decision point.** *(done)* Are these summaries better than pasting responses into a chat? If not, this is the cheapest possible place to have found that out.
5. **Reactions.** *(done)* Agree / important / misrepresents on the summary page, `GET /summaries/{id}/reactions`, and the second-pass loop. This is the feature that makes the answer to step 4 "yes", so it lands here rather than in polish.
6. **Admin.** *(done)* Password gate, the `AdminSession` seam, dashboard, response list, soft-delete, settings, close/reopen, and `/new`.
7. **The node tree.** *(done)* `SummaryTopic` and `SummaryTopicPoint` become `SummaryNode`; `SummaryTopicPointResponseReference` and `PointReaction` become `SummaryNodeReference` and `NodeReaction`. One migration, grounding rewritten to the branch rule, nested rendering on the summary page, and the brief updated to describe a tree. Lands before step 8 for one reason: an edit UI written against topic and point entities would have to be written twice.

   The depth check this step exists for was run, and answered a question nobody had asked: the tree carries meaning, but the `Kind` vocabulary shipped with it was leading the agent into writing nodes to fit a box. A second migration [drops `Kind`, `Sentiment`, `Objectivity` and `Intensity`](#node-kinds-tried-removed); a node is now text, references and children.
8. **Summary edit.** *(done)* The edit page, publish/unpublish, delete, and prominent display of objections. This is where **no ordering concept** finally died: moving a node makes insertion order wrong, so the editor brought an `Ordinal` column and a migration with it, backfilling existing rows from key order per sibling group. Nothing before this step needed one, because the agent rewrites whole trees and depth-first insertion reproduces sibling order for free.

   **The editor is the only interactive component in the app**, and deliberately the only one. Everything else is static SSR posting forms, which is right until a page has a dozen controls per row and a full reload between each one. Three consequences worth knowing before touching it.

   `AdminSession` reads the admin cookie off `HttpContext`, which is gone once a circuit starts, so the page is a static shell that does the auth check and renders the editor as a child — the check happens where it always did.

   A scoped `DbContext` lives as long as the circuit rather than as long as a request, so every operation opens a scope of its own; without that an editing session accumulates tracked entities and starts answering reads from the first one.

   **Blazor re-renders the component whose handler ran**, and nearly every control here belongs to `SummaryEditNode` rather than to the editor. Reaching the editor through the cascaded instance is an ordinary method call, so the framework never learns that the node count, the tree and the grounding warnings — all rendered by the parent — have changed: the write lands and the screen does not move. `EventCallback` parameters would re-render their receiver for free and are the idiomatic answer; eight of them threaded through a recursive component cost more than one `StateHasChanged` in the reload, which is what this does instead. Worth knowing because the symptom looks exactly like a stale read and is not one.

   **Grounding grew a second implementation, and a second place it is enforced.** The agent's copy runs over a submitted draft and rejects the whole call. It cannot serve a human editor, who breaks the rule a node at a time — adding a node always produces an uncited leaf, and refusing that would make editing impossible. So [`SummaryGrounding`](#grounding-the-app-validates-the-agent) runs the same branch rule over a stored tree, the editor marks the offending nodes live, and **publishing refuses** while any remain.

   That last clause did not survive step 9. The reasoning here was that an ungrounded draft is a work in progress and an ungrounded *published* summary is the thing the app exists to prevent — sound while the agent was the only thing that could write a node, and an unwritable page of meeting notes once that stopped being true. The check stays; it stopped being a gate.

   **Published means read-only to the human too**, not just to the agent. Unpublish to edit. The alternative — editing in place — silently destroys the reactions attached to any node deleted underneath the people who left them, and forking to a new draft strands them just as thoroughly since node ids do not survive a copy. Neither is better than making the admin take the summary down first, which is one click and says what is happening. ([Stable ids](#summarynode) removed the second half of that argument in step 9 — a fork could carry its nodes now. They did not remove the first: deleting a node still takes its reactions and comments with it, whoever does the deleting.)

   **The third visibility state went.** `IsDraft` and `IsPublic` are separate columns, and the version list rendered a badge for blessed-but-not-public that nothing could produce, because every caller set the pair together. Publishing is now one bool: it blesses and shows in the same move. The columns stay as they are; it is the UI that stopped implying a state nobody had built.

   **Editing references is included**, and reuses `QuoteLocator` rather than trusting what was typed — so a hand-typed quote with a curly apostrophe in it gets the same character-level diagnosis an agent gets, naming the codepoint and handing back the exact response text. The citation form shows the response body beside the box for precisely this reason. Seeded reactions were added at the same time, because the objection display had nothing to render in development and the headline feature of the step was therefore invisible.
9. **Opening the flow up.** *(done)* The tool was built for one workflow and turns out to hold several. A person can write a tree with nothing under it, a topic need never collect a response, and the pieces after the prompt become independent of each other.

    **This absorbed a step.** Summariser hardening was going to land first — everything in it came out of watching an agent draft a real summary end to end, which is a different exercise from designing the endpoint and turned up things the design could not have predicted. Then the vocabulary changed and so did the write payload, and half of that step was work on surfaces this one rewrites: the brief and the payload contract would have been written twice. Merged rather than resequenced, because once they are adjacent there is no seam between them worth keeping. One step, one migration.

    In the order it has to land:

    **The rename.** `Survey` → `Topic` through the entities, services, routes, telemetry redaction and cookie names, plus a data migration. Mechanical except for the [brief](#api-surface), which is prose an agent reads and wants a real pass rather than a find-and-replace. First because everything below touches files it renames.

    **`Response.Body` line endings normalised to `\n` at ingest**, with a migration renormalising existing rows and repairing the reference offsets that shift as a result. Reasoning under [Response](#response). Early, because every quote the API validates depends on it.

    **Stable node ids and the write contract.** `GET /summaries/{id}` returns ids; `PUT` accepts [text or id, never both](#the-write-contract-text-or-id-never-both); `PUT` is gated on `IsAgentEditable` instead of `IsDraft`. This is the part with teeth — it changes the shape of the agent's edit loop from replace-everything to name-what-you-keep, and every other item here assumes it. **Payload bind failures get a located error** in the same pass, since the payload shape is being defined anyway: a missing `text` or malformed JSON currently fails in the reader rather than in validation, so it returns a bare 400 with no body outside Development while every other mistake gets a 422 with a pointer. Wants `RouteHandlerOptions.ThrowOnBadRequest` and a handler turning the `JsonException` path into the same shape.

    **Reactions and comments.** `NodeReaction` loses `Note` and `Misrepresents` and gains the [light set](#reaction-and-comment); `NodeComment` arrives with `IsHidden`; both open to anyone who can see the topic. `GET /summaries/{id}/comments` for the second-pass loop. The seed data needs comments on the retro for the same reason it needed reactions in step 8 — the admin display has nothing to render without them.

    **Everything that was a stage and is now an invariant.** `Response.IsFrozen` set at close and checked at every reference write; `CanReopen` deleted; `ResponseIdentity` editable while the response count is zero; publishing clearing `IsAgentEditable`; `SummaryGrounding` demoted from gate to classifier, with the summary page rendering three states instead of two; `ResponseCountAtWrite` and the outdated hint; "New empty" on the summaries page; `/topics/{code}` deciding what it leads with.

    **Quote matching and paging**, the rest of what was the hardening step and independent of everything above. Normalise both sides before matching a quote and store the span taken from `Body` rather than the string that was sent — measured as the largest available reduction in false rejection, 2/19 to 14/19 against corrupted-but-correct quotes, for roughly ten lines; reasoning and numbers under [Match normalisation](#match-normalisation). Page `GET /responses` with `skip`/`take` plus a total.

    **The brief, rewritten once at the end.** New vocabulary, the write contract, the four additions that came out of the drafting run — name people who gave a name, check quotes are substrings locally before sending, build the payload with code where possible, start a new summary rather than continuing an old one — and the note that [uncitable nodes are not defects to fix](#api-surface). Last, so it describes what is actually there. (The worked example became valid JSON back in step 7.)

    **The order is not negotiable and the reason is the migration.** Comments and the freeze both touch tables the rename touches, and node ids have to be stable before anything hangs off them — a comment written against a node that a later `PUT` deletes and recreates is exactly the orphan this step exists to prevent.

10. **Polish.** Publicly listed home page, token regeneration, empty states, and the copy telling responders exactly what is and isn't stored.

Steps 1–3 stand alone as something usable with hand-written summaries, which makes them a reasonable stopping point if the weekend runs out.

## Prior art

Checked before building. Nothing free does this combination — self-hosted, no accounts, link-shared, one free-text prompt, agent-driven summarisation over a plain HTTP API — but several projects have solved adjacent pieces and are worth learning from.

- **[Talk to the City](https://github.com/AIObjectives/tttc-light-js/)** (AI Objectives Institute) — the near-hit. LLM extracts claims from free text, clusters them into topics/subtopics, links every claim to an exact quote. Independently arrived at essentially this object model, and has been used for government and union consultations. Their [write-up](https://ai.objectives.institute/blog/talk-to-the-city-an-open-source-ai-tool-to-scale-deliberation) argues the report *structure* is what mitigates LLM inaccuracy — drill-down from theme to verbatim opinion is what makes a summary trustworthy. They also concluded manual editing of AI output remained necessary and was "reasonable overhead". Both findings validate choices here. Not reusable: Next.js + Express + pipeline worker + Firebase + GCS + Redis
  + Pub/Sub.
- **[Parabol](https://github.com/ParabolInc/parabol)** — open source, self-hostable, air-gappable, AI theme-grouping for retros. Different shape: a *synchronous* meeting tool with multiplayer sticky-note grouping and accounts. Ours is async collect-then-summarise.
- **[Formbricks](https://github.com/formbricks/formbricks)** (AGPLv3) — closest on the topic side; self-hosted, open text, AI insights. But a full form builder with orgs, projects and question types. Enormous relative to "one prompt, one textarea".
- **[Fast Retro](https://fastretro.app/), QuickRetro, Postfacto** — free self-hosted retro tools with anonymous input and link sharing. Closest on the social model, no AI summarisation with quote grounding.
- **[Taguette](https://www.taguette.org/) / QualCoder** — qualitative data analysis. The intellectual ancestor of `SummaryNodeReference`: highlight a span, tag it with a code. Academia has done this by hand for decades and calls it *qualitative coding*. Useful vocabulary if we ever want to export somewhere.

**Convergence, in the end.** This originally read as a deliberate divergence: Talk to the City nests topics → subtopics → claims because it processes thousands of inputs, so at 6–20 responses flat topic → point looked like the right call and the extra level like noise. Two things overturned that.

The first is that depth tracks the *detail* of a topic, not the *volume* of input. A five-response conversation about one thorny thing goes deeper than a sixty-response topic that skims. Response count was the wrong axis to pick a shape on.

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
  RelationType       { Id, TopicId?, Name, Description, IsDirected }
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

  `TopicId` nullable so a house vocabulary can accumulate globally while one-off types stay topic-local.

  **Grounding does not extend for free.** An earlier draft of this section claimed it did — that a relation between two nodes is already grounded because both endpoints are. That is wrong, and it is wrong in the exact way the rest of the design exists to prevent. Two real quotes joined by an invented arrow is a *new* assertion nobody made, wearing grounded clothes. "Slow CI causes batching" is a claim about the world; the quote saying CI is slow and the quote saying people batch commits do not, between them, support it.

  So an edge grounds like a node does. `NodeRelation` carries its own references, and there are three honest provenances:

  | | Meaning | Rendering |
  |---|---|---|
  | references | someone drew the connection themselves, in one response | the strong case; show the quote |
  | none, agent-drawn | the agent inferred it across two responses | marked as inference, never as finding |
  | none, human-drawn | an admin added it in the editor | marked as the admin's reading |

  `solves` is the canary. Almost nobody writes "that solves the other person's problem" — so a `solves` edge is nearly always an inference, and if the UI ever renders inferences indistinguishably from quoted connections, `solves` is where the tool starts making things up on the group's behalf.

  **The middle row now contradicts a decision made after it was written**, and the contradiction is the interesting part. This table predates [the rule that an agent may not assert without a quote](#why-a-person-may-assert-and-an-agent-may-not), and its second row is exactly that — an agent-drawn edge, uncited, kept and labelled as inference. Two ways out. Either an agent-drawn edge must cite like agent-written text does, making the row disappear and the vocabulary poorer; or an edge is genuinely a different kind of object from a node, one whose whole purpose is to say something neither endpoint said, and the honest answer is that it may be inferred but must be labelled. This design leans to the second and should not assume it. Settle it when relations are built, not now — but settle it deliberately, because the row was written when the app could not store an unevidenced agent claim at all, and that is no longer why it looks safe.

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

  Where the stage sits is the interesting choice. Drawing relations *before* the first publish gives one complete artefact and gets the group more to react to; drawing them *after* the first round of reactions means the edges are built over a tree the group has confirmed rather than one only the admin has read. The second is better, because an edge touching a node the group has already said is a misreading of them is an edge built on sand — so the recommended flow is [step 8](#4-summarise), over the second draft. It stays a recommendation rather than a rule: the mechanism is just a pass over a draft, and it works at either point.

  **Don't store the stage.** No `HasRelations` flag and no state enum — whether a summary has been through a relations pass is `Relations.Any()`, and "we decided not to bother" is indistinguishable from "we haven't yet" in every way that matters, because publishing is the signal that the admin is done. The stage is an affordance in the editor, not a column.

  **Feeding back into `Body`.** A relations pass is the natural moment to rewrite the narrative overview, and it is the one place the two representations are *meant* to converge — see [Summary](#summary). Nothing new is needed to make that safe: publishing clears the agent's write access, so a rewrite becomes a new version unless an admin says otherwise.

  **Being a separate stage used to be what forced stable node ids**, and that argument has been overtaken: edges outlive the pass that drew them and would have been orphaned by every whole-tree `PUT`, which made stable ids a prerequisite for relations specifically. [They exist now](#the-write-contract-text-or-id-never-both), built for three reasons that had nothing to do with relations. What survives is the second half: relations get their own endpoint rather than a field in the tree, because the tree payload is about the tree.

- **Cross-topic summaries, via `Collection` — not accounts.**

  ```
  Collection { Id, Name, AdminPasswordHash, Topics[] }
  Topic.CollectionId  (nullable)
  ```

  A cross-topic summary is one scoped to a collection rather than a topic. One table and one nullable FK, composing with everything already designed. This is where SODA's map-merging gets genuinely interesting: shared concepts across *sprints*, showing which stressors recur and which actually got resolved.

  **Explicitly not accounts.** Not because of the work — users, registration, login, password reset, email, invitations — but because signup friction destroys the property that makes the tool good. "Here's a link, chuck your thoughts in" stops working the moment anyone has to create an account, and so does spinning up a topic in twenty seconds. The [`AdminSession` seam](#the-admin-seam) exists so this stays a contained change rather than a refactor.
- **Markdown export of a summary.** Nested bullets, which is the format the tree came from in the first place. Lossy on purpose: reactions, comments and the reference spans don't survive, and quotes become ordinary text. That is fine in this direction and not in the other — [markdown is refused as an input format](#why-nested-json) precisely because it has no lossless container for verbatim text, and the same lossiness is harmless once the app is the thing being copied *from* rather than written *to*.

  **This moved up the list without changing.** It was a convenience while every summary could be redrawn from the responses. [It no longer can be](#principles), so an export is now the only way words that exist nowhere else leave the SQLite file — and the fact that the lossy version is the one on offer is worth a second look before it gets built as specified.

- ~~**Stable node keys.**~~ **Built in [step 9](#build-order)**, and the manner of its arrival is worth keeping. It sat here a long while with the cost side of the case made — the agent re-emits every node and quote on each `PUT`, which is tokens and one more chance per repeat to corrupt a quote — and that on its own never justified building it, the same status the [prefix/suffix anchors](#match-normalisation) still hold. What was written here as the reason it would actually get built was *prerequisite for relations*, a deferred feature. In the event three unrelated things needed it in the same week: hand-written nodes, reactions and comments on an agent-editable version, and a person editing while an agent writes. A mechanism with one speculative caller and three real ones is not the same object, and the tell was that the speculative caller was never the argument that moved it.

- **Duplicating a topic** for recurring sprint feedback. It used to be the answer to "we need more input after summarising" too, which made it look more valuable than it was; [reopening](#response-editing-a-window-then-frozen) answers that now, and this is back to being about recurring work.
- **App-initiated generation** — a "Summarise" button calling the Anthropic API directly, for colleagues who don't have an agent session. The service layer should be shaped so the API endpoints and such a button would call the same code.

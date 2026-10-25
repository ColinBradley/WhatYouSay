# Live feedback and presentation

Project key: **LF**. Work items are numbered LF-1 to LF-9, and each one is delivered as its own pull request.

## Outcome

A presenter prepares a topic, gives their local agent the topic token and summarising guidance, starts a local script, and opens a presentation page. The audience scans a QR code, submits questions, ideas or experiences, and reacts to other responses. Responses and reaction counts appear on observing clients immediately. A grounded summary updates automatically as the local agent processes new feedback. The presenter can leave the whole setup running during the session.

The initial deployment remains one self-hosted Blazor Server application with SQLite. Each presenter runs their own authenticated Claude Code or Codex client on their own machine. The site stores a topic-scoped summariser token hash and topic guidance; provider credentials remain with the presenter's installed client. App-hosted model calls, provider account login on the site, and shared subscription accounts are later work.

The motivating event has about 30 topics with roughly 50 participants each over several days. Topics have independent runners and credentials. Concurrent sessions are supported; no event-wide generation queue is required.

## Requirements settled in the discussion

| Area | Direction |
|---|---|
| Cadence | A local script owns waiting and polling, with a default interval of about 10 seconds. Do not depend on agent `/loop` features or minute-based scheduling. |
| Hands-free use | Preparation happens before presenting. New feedback does not require owner review, topic closure, publication, or another prompt. |
| Workflow restrictions | Freezing responses, reviewing drafts, pausing AI updates and locking a summary remain choices. They are not prerequisites for ordinary live use. |
| Response feedback | Participants can toggle emoji reactions on responses. All observing clients receive count changes live. Existing summary-node reactions remain available. |
| Reading order | Responses occupy the inline start; the summary occupies the inline end. On a narrow screen, responses precede the summary in document order. |
| Projection | A QR code and short join URL sit at the block start of the response pane, followed by newest-first responses; the summary sits beside them. |
| Anonymous ordering | Newest-first anonymous feedback is supported, with timing/order collection disclosed before submission. The user explicitly chose this over preserving unordered anonymous feedback for new topics. |
| Provider settings | Provider, model and polling settings are local startup arguments/configuration. Topic owners can prepare summarising guidance on the site; that is separate from provider credentials. |

## Current code and the changes it needs

| Existing seam | Gap |
|---|---|
| [SummariserApi](../../WhatYouSay.Web/Api/SummariserApi.cs), [SummariserPrompt](../../WhatYouSay.Web/Api/SummariserPrompt.cs), [SummariserBriefing](../../WhatYouSay.Web/Api/SummariserBriefing.cs) | Topic-scoped read/write access already exists, but the briefing expects discussion and manual publication. Reads have no consistent input revision, and API writes do not notify summary readers. |
| [SummaryService](../../WhatYouSay/Services/SummaryService.cs) | Agent quotes currently require frozen responses. Freshness uses response count, so edits and reaction changes are missed. Writes have no expected summary revision. |
| [SummaryEditService](../../WhatYouSay/Services/SummaryEditService.cs), [SummaryNodeReference](../../WhatYouSay/Data/SummaryNodeReference.cs) | Citations point at mutable response bodies. Human citation writes must adopt the same immutable evidence model as agent writes. |
| [TopicAdminService](../../WhatYouSay/Services/TopicAdminService.cs) | Closing freezes responses, publishing disables agent editing, and neither is a suitable default for live use. |
| [ResponseService](../../WhatYouSay/Services/ResponseService.cs), [RespondPage](../../WhatYouSay.Web/Components/Pages/RespondPage.razor.cs) | The public flow assumes one response per browser. A later submission edits that response, and every fresh submission currently mints a new token. |
| [ReactionService](../../WhatYouSay/Services/ReactionService.cs) | Reactions target summary nodes only. Its fixed kinds and cookie-based deduplication are reusable for response reactions. |
| [SummaryLiveUpdates](../../WhatYouSay.Web/Live/SummaryLiveUpdates.cs), [SummaryReader](../../WhatYouSay.Web/Components/Shared/SummaryReader.razor.cs) | Notifications address an existing summary ID. An open page cannot discover its first summary or a newly selected summary, and its overview is rendered outside the interactive reader. |
| [SummaryReader markup](../../WhatYouSay.Web/Components/Shared/SummaryReader.razor), [SummaryEditor](../../WhatYouSay.Web/Components/Shared/SummaryEditor.razor), [app.css](../../WhatYouSay.Web/wwwroot/app.css) | The tree precedes the responses. There is no presentation route or QR code. |

## Recommended design

### Editable responses with immutable evidence

Give each response an immutable revision containing the exact normalised body and author attribution used for a citation. Editing creates another revision and updates the current response; it does not change previously cited text. References name both the response and its revision. Quote matching and offset calculation run against that revision, for human and agent writes alike.

Keep the response ID stable, keep random UUIDs where anonymous identifiers must not disclose timing, and retain the current response as the normal feed entry. A citation to an earlier revision opens that earlier text and says the response has since changed. Never apply its offsets to the current body. This replaces the need to freeze people out merely because they have been quoted.

Withdrawal and admin hiding remove the response and its quoted material from public reads immediately, including historical revision reads. They also mark the summary inputs as changed. Existing summary prose may still reflect the contribution until the next successful generation; the submission/withdrawal copy must describe that honestly. Revision storage is internal evidence history, not a public edit-history browser or a new promise of permanent deletion.

### Live publication and optional controls

New topics default to allowing live agent updates. A successful grounded write to the topic's active live summary becomes visible automatically and leaves that summary open to the agent. Use one stable live-summary ID across updates so links, node reactions and comments remain useful. Keep existing node IDs for material that is retained; do not create a summary version every 10 seconds.

When a live update removes a node, retain the node and its human comments/reactions outside the active tree instead of cascading those contributions away. Archived material is not rendered as current summary content; an owner can still inspect its context. Ordinary AI revision is not authorisation to delete someone's words. Deliberate owner deletion remains a separate operation.

Existing topics retain their current visibility, locks and reviewed workflow on migration. An owner can enable live updates once. Raw-response visibility stays an explicit topic choice; upgrading or enabling automatic summaries must not expose private responses. A presentation setup makes that choice clear before the event.

Stop accepting new responses, freeze existing responses, pause agent writes, and require review are separate actions. Stopping collection alone does not freeze existing text or prevent its owner from editing/withdrawing it. Explicitly frozen responses remain frozen until an owner deliberately allows editing again. Manual summaries and reviewed drafts continue to work.

All summary mutations carry a persisted summary revision. An agent submits the revision it read; an intervening human edit or another runner's accepted write rejects that replacement atomically. On conflict, the local runner discards its result and reads the latest tree before trying again. The owner can optionally pause AI while making changes. Continuing to grant AI editing permits subsequent intentional revisions; concurrency checks prevent lost writes, not permanent ownership of every human-written node.

### Freshness without generation starvation

Maintain an input revision for content that the model consumes: submissions, edits, withdrawals, admin hiding, response reactions, relevant visible summary feedback, and summarising guidance. Summary generation itself does not increment this revision. Use a separate view revision/event for visibility and presentation state changes that do not require new model output.

A summariser read returns a consistent snapshot: current responses with revision IDs, reaction tallies, relevant summary feedback, guidance, input revision, active summary and its revision. A write declares which input revision it used. Validate evidence against the named immutable revisions and commit the entire summary together with its applied input revision.

New input arriving during generation does not automatically reject a valid result. Publish the result for the snapshot actually read, show that newer feedback is waiting, and immediately take another pass. Otherwise a busy room could prevent any summary from appearing. Withdrawn evidence and summary-write conflicts still require a fresh read. Do not stamp a result with whatever input revision happens to be current when it finishes.

### Timing and local execution

The runner polls cheaply every 10 seconds and calls the model only when inputs changed. A poll does not itself consume model usage. The script controls sleeping, cancellation, retries and child-process lifetime; the model only produces the next summary. The copied setup prompt can help the presenter prepare and launch this supplied script. It does not ask a chat agent to improvise an indefinite loop during the presentation.

Run at most one generation at a time in a runner. Coalesce arrivals while it is busy. After a successful write, check for pending input immediately; otherwise sleep until the next poll. Use an optional short quiet period only within a maximum wait of 10 seconds from the first pending change. Continuous feedback must never keep postponing a pass. A fixed 10-second poll with no extra debounce is the initial default.

The 10-second interval bounds normal detection/scheduling delay, not model completion time. Generation speed, provider limits and networking add latency. Responses and reactions use server push and do not wait for the model. Keep the last successful summary visible during generation or errors, and replace it atomically when the next one succeeds.

Before presenting, the owner starts the client, confirms the correct work/personal account and model, and runs a short preflight generation. Keep the machine awake and connected. The runner accepts an explicit session end time or duration and Ctrl+C, and terminates its child process when cancelled. It records enough non-secret state to resume after a restart without creating a second live summary.

### Response ordering, identity and reactions

Support several contributions from the same browser, owned by the same existing topic cookie. An explicit edit targets one response; posting another question creates another response. Withdrawing one contribution must not clear the cookie and lose ownership of the others or reset reaction deduplication.

Separate whether names are collected from whether timing/order is collected. New topics, including anonymous ones, can use newest-first ordering with a visible pre-submit notice. Anonymous means no name is requested or stored; a live display is not a promise to hide when an answer arrived. Preserve the legacy no-timing policy on existing anonymous topics, and do not reconstruct their historical order from row IDs or invent timestamps. Fix a topic's collection policy once feedback exists, as with identity today.

Reuse the existing fixed emoji kinds initially. Response reactions attach to the stable response ID, survive edits, and use the topic cookie hash to deduplicate each kind. Show an edited marker; reactions are informal signals on a contribution, not certified approval of an immutable text version. Withdrawn/hidden responses cannot receive new reactions and do not expose their counts publicly. Multiple kinds per person remain allowed. Reaction counts inform the summary about emphasis without becoming a vote that silently removes minority feedback.

### Live views and presentation

Use topic-scoped notifications in the web assembly, building on the existing singleton subscription pattern. Every successful write path must publish after commit: form submissions, edits, withdrawal, admin changes, response reactions, summary API writes, manual summary edits, and relevant node feedback. Failed writes publish nothing. Keep domain services independent of Blazor and `HttpContext`.

Subscribers reload authoritative state using a fresh DI scope, recheck public visibility, and render on their circuit. Notifications are invalidations rather than shared per-viewer payloads. Subscribe before initial load, coalesce concurrent notifications, unsubscribe on disposal, and resynchronise after reconnect. Updating the overview, the active summary selection and the first-summary empty state belongs inside the interactive view. Keep a fixed historical-version URL pinned to that version.

Committing a response or reaction must not wait for every observer to query and render. Schedule coalesced invalidations after commit, isolate slow/disconnected circuits, and keep subscription work bounded. The current notifier awaits subscribers sequentially, so its fan-out behaviour needs attention as well as its topic scope.

Add `/topics/{code}/present` as a public presentation view that applies the same visibility rules as the normal topic page. Its QR code points directly to `/topics/{code}/respond`, never to an admin page or a URL containing the summariser token. Generate the QR locally from a configured public base URL; a remote QR service must not receive topic URLs. Include the readable join URL and topic code.

At wide sizes, the response pane occupies the inline start and the summary pane the inline end. The QR/join block stays at the block start of the response pane. The response pane follows newest arrivals while already at its start; a viewer who scrolls back keeps their position and gets a new-response indicator. Keep focus, typed drafts, expanded branches and selected citations stable across updates. Projection uses readable text and restrained motion rather than animating every arrival.

Put responses first in the DOM on existing paired reader/editor views too. Use CSS grid and logical properties such as `padding-inline-start`, `border-inline-start` and `inset-block-start`; avoid visual reversal that makes keyboard or screen-reader order disagree. Check `dir="rtl"` as well as LTR, narrow layouts, zoom and reduced motion. Full translation is outside this work.

## Work items

Each work item is independently reviewable and leaves the current application usable. Update the relevant plan/invariant guidance as behaviour actually changes, including the domain AGENTS rule that currently requires frozen evidence. Run `dotnet test` for domain changes and `dotnet build` for application/script integration changes, addressing analyzer diagnostics. Domain tests use real SQLite and migrations; the existing test project continues to reference the domain library only. UI and HTTP behaviour are verified through manual browser/API exercises, not a new UI-test suite.

| ID | Deliverable | Dependencies |
|---|---|---|
| LF-1 | ✅ Immutable response evidence | None |
| LF-2 | ✅ Optional restrictions and live-summary ownership | LF-1 |
| LF-3 | ✅ Multiple contributions and disclosed chronological ordering | LF-1 |
| LF-4 | ✅ Emoji reactions on responses | LF-3 |
| LF-5 | Consistent summariser inputs and safe live writes | LF-1 to LF-4 |
| LF-6 | Topic-wide live updates and response-first readers | LF-2 to LF-5 |
| LF-7 | Presentation page and QR join flow | LF-6 |
| LF-8 | Local Claude Code runner and ready-to-use setup | LF-5 to LF-7 |
| LF-9 | Codex runner adapter | LF-8 |

LF-1 to LF-8 provide the complete initial workflow for the Claude users at work. LF-9 adds provider choice without changing the website's authentication model.

### LF-1 — Immutable response evidence

**Change:** Add response revisions and reference-to-revision links. Backfill one revision from each existing response, preserve existing citation text/offsets, and update both agent and human citation writers/readers. Keep existing freeze/publication behaviour in this item; removing the citation freeze requirement follows when the new evidence path is proven. Account for deleted responses without exposing them through revision reads.

**Validation:** Existing summaries survive migration. After editing a response, an earlier citation still selects the original words and attribution. Current and earlier revisions cannot be confused or cited across topics. Withdrawal hides all quoted revisions. Line-ending normalisation and existing quote matching continue to work. Exercise an existing SQLite database as well as a fresh migrated one.

**Finish when:** Evidence can safely outlive a response edit, and all citation paths use the same rule.

**Implementation notes:**

- A response's wording lives only in its revisions. `Response` holds identity, ownership, author, flags and `RevisionNumber`, which names the current revision. `Body`, `CreatedAt` and `UpdatedAt` are gone from it.
- `ResponseRevision` is keyed by (`ResponseId`, `Number`) and holds the normalised body and a nullable `CreatedAt`. Numbers count up from 1 within one response, so they disclose edit count but not timing. `CreatedAt` is null wherever the topic records no timing.
- The submission time is revision 1's `CreatedAt`, and an edit's time is its revision's `CreatedAt`. Reading either, or the current body, requires the revisions to be loaded.
- The author is not versioned. Citations show the response's current author, rather than the immutable author attribution the design above describes.
- `Response.Revise` is the only way to change wording. It normalises the body, records nothing for an unchanged wording, and adds the next revision otherwise.
- `SummaryNodeReference.RevisionNumber` forms a composite foreign key with `ResponseId`, so the database refuses a reference naming another response's revision. Both foreign keys restrict deletion.
- Agent and human citation writers locate quotes in the current revision only (`SummaryService.CurrentRevisions`). Earlier wording cannot be newly cited, and existing citations keep their revision.
- Reads load each reference's revision through `SummaryService.WithCitations`, filtered on a live response, so withdrawal or admin deletion hides every revision together.
- Readers apply offsets only to references on the current revision. Citations of earlier wording render that wording in a collapsed block in the response pane, which opens when a citation is followed. The editor tags them.
- The summariser API reports `revision` on responses and `revision`/`isCurrentRevision` on references. The draft contract is unchanged and cites the current revision implicitly. Revision IDs in writes belong to LF-5.
- The `ResponseRevisions` migration copies every response's body and submission time into revision 1, withdrawn responses included, and points every existing reference at it. The last-edit time of a previously edited response is not kept, because its earlier wording was never stored.
- The frozen-response requirement for agent citations is still in place; LF-2 removes it.

### LF-2 — Optional restrictions and live-summary ownership

**Change:** Introduce the live/reviewed summary policy, a topic's active live-summary selection, and summary write revisions. Remove the prerequisite that cited responses be frozen. Separate collection closure from freezing, support an explicit owner-controlled freeze/unfreeze action, and expose independent AI pause/edit-access and review choices. Allow human editing of a live published summary. Archive removed live-tree nodes with their comments/reactions rather than deleting that feedback. New topics choose live updates by default; migrated topics retain existing settings and frozen state.

**Validation:** Open editable responses can be cited. A valid live write can remain visible and agent-editable. Reviewed drafts remain private until owner publication. Closing without freezing still permits authorised editing/withdrawal; explicit freezing prevents it until deliberately lifted. An in-flight write based on an older summary revision cannot overwrite a human change. Existing summary/node IDs and feedback survive migration. Removing a node from the live tree hides it from current output but preserves its human feedback and context for owner inspection.

**Finish when:** A topic can stay open while its one live summary is updated repeatedly, without owner intervention between writes.

**Implementation notes:**

- `Topic.SummaryPolicy` is `Live` or `Reviewed`, stored as a string. The entity default is `Reviewed`, which the migration gives existing topics and seed data keeps. `TopicAdminService.CreateAsync` sets `Live`. The create page offers no choice; the admin page switches with "Require review" and "Publish updates live".
- The live selection is `Summary.IsLive` rather than a topic column. A unique partial index on `TopicId` where `IsLive = 1` allows one per topic, so two creators racing cannot both win. `TopicId` keeps its plain index by explicit declaration, because EF would otherwise drop it in favour of the partial one.
- On a live topic with no live summary, the summariser's next `POST /summaries` creates the live summary, published and open to the summariser. A POST while one exists makes a private draft. The admin can make any summary live from the summaries list, which publishes it, opens it to the summariser and leaves the previous one as an ordinary published version closed to the summariser.
- The live summary stays published while it is live, so unpublishing it is refused. Switching to review ends live mode: the summary stays published, is closed to the summariser and loses `IsLive`. The summariser cannot write to any published summary but the live one, whatever `IsAgentEditable` says, and the admin cannot open one to it.
- Pausing the summariser is `IsAgentEditable = false` on the live summary. The admin page has Pause and Resume. The editor has an open/close button for any draft and for the live summary.
- `Summary.Revision` starts at 1 and moves on with every summariser write and every `SummaryEditService` operation. Visibility, access and policy changes leave it alone. It is an EF concurrency token, so every save of a summary row is conditional on the revision loaded, and a lost race is refused whole: `stale_revision` for the summariser, `InvalidOperationException` for people and admin actions.
- `SummaryDraft.BasedOnRevision` (`basedOnRevision` in JSON) names the revision read. A mismatch is `stale_revision` (409). It is required to revise the live summary (`revision_required`, 422) and optional elsewhere, so the reviewed `PUT` keeps working for clients that do not send it. `SummaryInfo` and `DraftResult` report `revision` and `isLive`.
- Human edits take no expected revision. They are targeted operations on the current tree, and their revision bump is what makes an older summariser write fail.
- Summariser citations no longer require frozen responses. Closing freezes nothing. `TopicAdminService.SetResponsesFrozenAsync` freezes or unfreezes every response the topic has now, and later responses arrive editable. Existing `IsFrozen` values are kept, and closed seed topics still arrive frozen.
- `SummaryNode.ArchivedAt` marks a node a summariser write left out. Summariser writes never delete nodes, on drafts as well as the live summary. An archived node keeps its parent, citations, reactions and comments. `WithCitations` and `SaveDraftAsync` filter archived nodes at the include, and `SaveDraftAsync` detaches what it archives so the summary it returns holds only the current tree. An archived id cannot be carried forward, and new reactions and comments on an archived node are refused. Deleting a node by hand still deletes its subtree, archived nodes under it included.
- `/topics/{code}/admin/summaries/{id}/archived` lists archived nodes with their parent's text, quotes, reaction counts and comments, hidden ones marked. The summaries list links to it with a count, and the editor has an Archived button.
- The topic URL shows the live summary ahead of any newer published version.
- The briefing drops "close the topic first". It gains an Updates line, a LIVE UPDATES or REVIEW section, `basedOnRevision`, and archiving in place of deletion. The copied prompt says whether the topic is live.
- The `LiveSummaries` migration adds the four columns and the partial index. Existing topics become `Reviewed`, and existing summaries are at revision 1 and not live.

### LF-3 — Multiple contributions and disclosed chronological ordering

**Change:** Reuse one responder cookie for multiple responses, add list-own and explicit response edit/withdraw operations, and update the form with a separate new-contribution action. Add the timing/order policy and newest-first service queries with a deterministic tie-break. New anonymous topics disclose timing/order before submission. Existing anonymous topics keep their original no-timing policy; their data is not reinterpreted.

The current comment-to-own-response join assumes one response and would duplicate comments with a shared cookie. Replace that singular association with an explicit list of the commenter's contributions for the admin context, returned once per comment, and update the summariser comment contract accordingly. Do not choose an arbitrary response and present it as the one a comment concerns.

**Validation:** The same browser can post several questions and edit/withdraw only its own selected contribution. Its cookie and reactions survive withdrawing one response. Another browser cannot edit those responses. Comments remain single entries with several own responses. New anonymous responses have no author and sort newest first under the disclosed policy; legacy anonymous topics still collect no timing metadata. Editing does not move an old response to the top.

**Finish when:** Participation feels like adding feedback throughout a session, while explicit editing still works.

**Implementation notes:**

- `Topic.ResponseTiming` is `Recorded` or `NotRecorded`, stored as a string and read through `Topic.RecordsTiming`. Every writer of a `CreatedAt` reads it instead of `IsAnonymous`: revisions, comments, reactions and seed data. Telemetry still redacts anonymous topic codes by identity.
- New topics take `Recorded` from the entity default, and the create page offers no choice. The admin settings have a timing checkbox beside Anonymous. Both are fixed once any response exists, withdrawn ones included (`TopicAdminService.CanChangeIdentityAndTimingAsync`). While they are fixed, the admin page passes the stored values, because a disabled checkbox posts nothing and used to read as unticked.
- The `ResponseTiming` migration sets anonymous topics to `NotRecorded` and the rest to `Recorded`. It rewrites no response data.
- `ResponseService.InListingOrder` is the one response order: newest first by revision 1's `CreatedAt`, then by `Id`. Untimed topics order by `Id` alone. Every response list uses it, including the summariser API, a responder's own list and a commenter's responses.
- `SubmitAsync` takes the responder token and returns the new `Response`. The respond page reuses the cookie's token and mints one only when there is none. `ListOwnAsync` and `FindOwnAsync(topicId, responseId, token)` are scoped to that token, and `EditAsync` and `WithdrawAsync` act on a response found that way. Withdrawal no longer clears the cookie, and `ResponderCookie.Clear` is gone.
- The respond page shows a new-response form, with the browser's own responses listed below it. Each has an Edit link (`?edit={id}`) that loads it into the form with save, withdraw and "Write a new response". Posts carry `ResponseId`. A new response prefills the name from the browser's last one.
- The storage notice sits above the buttons and states what is stored, whether times are kept and that responses are shown newest first. Anonymous topics that record timing show it as a boxed notice.
- `NodeCommentView.CommenterResponses` lists the commenter's live responses, with id and current body, once per comment. A second query reads them, because a join would repeat the comment per response. The summariser API's `commenterResponses` replaces `responseBody`. `GET /api/topics/{code}` also reports `responseTiming`, and the briefing gains a Timing line.
- The seeded anonymous topic stays `NotRecorded`, so the untimed shape stays exercised.

### LF-4 — Emoji reactions on responses

**Change:** Add response reaction storage, a database uniqueness constraint for response/cookie/kind, scoped toggle/tally operations, and accessible reaction controls on visible response cards. Reuse the current emoji vocabulary and presentation where practical. Allow reacting without submitting a response; mint the cookie in the static shell before the circuit begins.

**Validation:** Toggle on/off, multiple kinds, duplicate concurrent clicks, separate browsers and cross-topic IDs. Hidden/withdrawn responses cannot be reacted to or leak through tallies. Edits retain the intended response-level reactions. The browser immediately reflects its own successful toggle; notification to other observers lands in LF-6.

**Finish when:** Responses have useful, deduplicated emoji feedback without accounts or another identity mechanism.

**Note from LF-3:** The respond page writes the responder cookie only on a first submission, and the summary page mints one only when it shows a summary. Response reactions need it minted wherever the cards appear. Withdrawing a response no longer touches the cookie.

**Implementation notes:**

- `ResponseReaction` is its own table beside `NodeReaction`, keyed to `ResponseId` and cascading from the response. A unique index on (`ResponseId`, `ReactorTokenHash`, `Kind`) allows one of each kind per reactor. `CreatedAt` is null where the topic records no timing. The `ResponseReactions` migration only adds the table.
- `ReactionService.ToggleOnResponseAsync` refuses a response that is withdrawn, deleted by the admin or on another topic. A toggle that loses a race to an identical one stops quietly: a unique violation on add, or no row left to remove. So a double click counts once.
- `ReactionService.TallyResponsesAsync` returns counts and the viewer's own kinds for each live response on a topic. `NodeReactionTally` is now `ReactionTally`, shared by nodes and responses.
- Toggling does not check `AreResponsesPublic`, as node reactions do not check publication. Cards only render where responses are shown, and response ids are random.
- `ReactionButtons` renders either the used or the unused kinds of a bar, with `aria-pressed` and a label-and-count `aria-label`. The node view uses it too. Response cards in `ResponsePane` show used kinds after the byline and the rest as `.on-demand` in the same row, and they now show an edited marker.
- `ReactionCounts` shows counts read-only on the respond page's own responses and the admin responses list. The summary editor's response list shows none.
- Response cards only appear inside `SummaryReader`, and the summary page already mints the cookie whenever it shows a summary, so reacting needs no response of your own. The respond page offers no reaction controls and still writes the cookie only on a first submission.
- A response toggle goes through the reader's existing write path, which publishes to `SummaryLiveUpdates`. Other readers of the same summary therefore see it, but nothing else does.
- Reaction metrics carry a `reaction.target` tag, `Node` or `Response`.
- The summariser API does not report response reactions. That belongs to LF-5's snapshot.

### LF-5 — Consistent summariser inputs and safe live writes

**Change:** Persist input revisions in the same transaction as input changes, add topic summarising guidance, and expose a coherent summariser snapshot with conditional reads. Include response revision IDs, reaction counts, relevant visible node feedback, guidance and the current live-summary write revision. Replace response-count-only freshness for the live path.

Add a live-write operation addressing the topic's active summary, with atomic create/update and an expected summary revision. Concurrent first writes must not create two active summaries. Return the accepted input revision and new summary revision. An ambiguous network failure is resolved by reading the saved state before retrying. The summariser token cannot change visibility settings or acquire access to another topic; automatic visibility is decided by the owner's live policy.

Keep existing reviewed endpoints usable. Old clients without revision IDs may continue citing frozen responses through the compatibility path; live writes require the new contract. Rewrite the briefing and copied prompt for hands-free operation, including response edits, retained node IDs, minority feedback, guidance, and the reviewed alternative. Do not silently truncate input snapshots; larger inputs must have an explicit handling strategy rather than an omitted tail of responses.

**Validation:** Detect changes when counts stay equal, including editing a response, replacing one response with another, changing reaction counts and changing guidance. An unchanged conditional read transfers no full snapshot. Snapshot reads do not mix revisions from different pages/queries. A slow valid result can be accepted with an older applied input revision; newer input remains pending. Summary writes do not trigger themselves. Invalid evidence/conflicts leave the last accepted tree intact. Manually exercise HTTP authentication, errors and compatibility.

**Finish when:** A local process can decide whether to generate and update the live summary without guessing freshness or racing owner edits.

**Note from LF-4:** `ReactionService.TallyResponsesAsync` gives counts for every live response, but nothing in the summariser API reports them yet. A response reaction writes only a `ResponseReactions` row, so input-revision tracking has to move on those writes too.

**Note from LF-3:** `/responses` lists newest first on timed topics, so a response arriving between pages shifts every later page by one. Comments list the commenter's responses by id as `commenterResponses`.

**Note from LF-2:** The existing endpoints already cite any live response's current revision, frozen or not. The existing `POST` already creates the live summary on a live topic, guarded by the unique index (`live_summary_exists`), and `PUT` takes `basedOnRevision`, required on the live summary. The live-write operation can reuse `Summary.Revision`, `IsLive` and those rejections. `GET /summaries/{id}/comments` still includes comments on archived nodes, with the node's text.

### LF-6 — Topic-wide live updates and response-first readers

**Change:** Extend/replace summary-scoped notifications with topic-scoped invalidations and audit every web write path. Schedule bounded, coalesced subscriber reloads after commit so a slow observer cannot hold up submission. Add an interactive topic reader that can render responses before any summary exists, discover the first visible summary, refresh its overview/tree and tallies, and handle visibility changes. Reuse the response cards on the attendee flow so people can read/react without leaving their feedback draft. Flip existing paired reader/editor panes into response-first DOM order and logical CSS layout.

**Validation:** In several open browsers, observe a submission, edit, withdrawal, response reaction, direct API summary write, first summary, manual edit, visibility change and summary deletion without refresh. A rejected write changes no viewer. Public/private transitions recheck access and clear content that is no longer visible. Reconnect reloads authoritative state. Subscriber disposal does not retain closed circuits. A typed response or node comment survives unrelated notifications. Historical-version URLs stay pinned.

**Finish when:** Responses and reactions update independently of AI, and all visible parts of the current summary can change live.

**Note from LF-4:** Response reaction toggles publish to `SummaryLiveUpdates`, so only readers of the same summary see them. The summary page mints the responder cookie only when it shows a summary, so a reader that shows responses before any summary exists needs its shell to mint one too. The respond page and admin responses list show reaction counts read-only, loaded once per request.

**Note from LF-2:** Admin live, pause and review changes, human edits to the live summary, and summariser writes do not publish to `SummaryLiveUpdates`. The editor does not reload when the summariser writes, and a human edit that loses a race shows an error and reloads. Making another summary live changes which summary the topic URL shows, so an open reader has to notice the selection moving.

### LF-7 — Presentation page and QR join flow

**Change:** Add the presentation route, its QR/short URL block, viewport-sized response/summary panes, readable projection styles and live empty/pending states. Responses sit at the inline start with newest arrivals first; the summary sits at the inline end. Reuse the normal topic reader's data and permissions. Generate the QR in-process and keep package versions in `Directory.Packages.props`.

Label the automatically updated summary as live AI output, rather than presenting it as an owner-reviewed result. Show the last successful summary update and whether newer feedback is pending. Until there is an explicit runner status protocol, do not claim a runner is connected or generating merely because the inputs changed. A missing/stopped runner leaves the feedback feed working and the last summary readable.

**Validation:** Scan the QR from a real phone against the externally reachable configured URL. Test projector dimensions, narrow screens, zoom, keyboard navigation, `dir="rtl"`, reduced motion, long responses and a topic with no summary. Verify private raw responses never appear publicly, even on the projector route. Test scrolling back through responses while new entries arrive and preserving the summary's visible position.

**Finish when:** The owner can project one stable URL containing everything needed to join and observe the session.

**Note from LF-3:** Anonymous topics from before LF-3 record no timing, so their responses have no newest-first order. The presentation page needs to say so for them rather than imply an order.

**Note from LF-2:** `Summary.IsLive` identifies the live summariser output to label, and `UpdatedAt` its last write. New topics are live by default and the create page does not say so, so the presentation setup should.

### LF-8 — Local Claude Code runner and ready-to-use setup

**Change:** Supply the local script, the Claude Code invocation, startup configuration and an admin-copyable setup prompt/instructions. The script performs conditional polls, starts bounded `claude -p` calls only for changed inputs, validates the returned structure, and posts via the new live-write contract. The script owns the 10-second cadence; no agent scheduling feature is involved. Start with one maintained runtime, selected before coding this item from the actual presenter machines, and document installation requirements.

Keep the topic token in local configuration and HTTP headers rather than model input or logs. Feed the model the snapshot and guidance as data, request the existing structured summary shape, and use the installed unmodified client's own authentication. Provider/model/interval/timeouts are startup settings. Keep shell argument construction safe for quotes, Unicode, multiline guidance and untrusted response text.

Include preflight, cancellation/end time, checkpoint/resume, bounded retries, provider limit backoff and clear local errors. Do not overlap generations or retry a request that may already have succeeded without checking the server. Normal failures retain the current visible summary; invalid output gets a bounded repair attempt before waiting/retrying. A human-write conflict requires a fresh snapshot, not blind resubmission.

**Validation:** Run against the actual site with Claude Code authenticated to a work account. Exercise a 10-second poll, idle periods with no model invocation, sustained arrivals, model generation longer than the poll interval, withdrawal during generation, reaction-only changes, malformed output, network interruption, token rotation, provider limits, Ctrl+C and restart. Measure submit-to-feed latency separately from submit-to-summary latency. Rehearse a 50-person topic and representative concurrent sessions against the one server, measuring SQLite contention and notification fan-out.

**Finish when:** A presenter can prepare once and complete a session using Claude without touching the owner workflow during normal feedback collection.

### LF-9 — Codex runner adapter

**Change:** Add the equivalent official `codex exec` invocation behind the same local runner contract, plus provider selection and a Codex preflight. Share polling, HTTP writes, checkpoints and failure handling; keep provider-specific CLI flags and output parsing narrow. Use saved presenter authentication without exporting it to the site. Keep the same structured summary and evidence validation.

**Validation:** Repeat the runner's meaningful success, slow-generation, malformed-output, authentication/limit and cancellation cases with the real Codex client. Confirm provider switching does not reset the site's live-summary identity or create overlapping writers.

**Finish when:** Presenter choice of installed client changes setup only, not the audience or website workflow.

## Decisions to resolve within the relevant work item

These do not block the plan. Resolve them before their work item starts and record the selected values here.

| Decision | Suggested starting point | Item |
|---|---|---|
| Timing metadata | Record submission/edit times on new chronological topics, including anonymous ones; disclose timing and public display. Preserve legacy no-timing topics. Reaction/comment timing follows the collection policy. **Chosen:** as suggested, as `Topic.ResponseTiming`. The respond form discloses timing and newest-first display before posting. | LF-3 |
| Emoji set | Reuse Agree, Disagree, Important, Question, Celebrate and Laugh. **Chosen:** as suggested, with the node bar's emoji and labels. | LF-4 |
| Guidance/summary size | Store expected feedback and summarising instructions per topic. Allow an optional length target; do not add another mandatory review gate or a universal tree-depth limit. | LF-5 |
| Snapshot size | One coherent snapshot of current input for normal event topics. Measure larger topics before choosing paging/chunking; never silently omit feedback. | LF-5 |
| QR rendering | Use a small local QR encoder, with the public base URL supplied through site configuration. Verify its current package/license when choosing it. | LF-7 |
| Runner runtime/platforms | Confirm the work presenters' operating systems. Prefer a small supplied script in a runtime already installed there; add another platform when actually needed. | LF-8 |
| Model/timeouts | Choose a fast available model using a rehearsal; do not hard-code a model name or promise 10-second generation from a polling interval. | LF-8 |
| Failure visibility | Begin with summary update age/pending feedback on the site and actionable runner console errors. Add an explicit expiring heartbeat/status protocol if rehearsal shows presenters need clearer connection/error status on the projector. | LF-8 |

## Rollout and completion

Do not reinterpret existing topics as publicly live or change their anonymity collection policy during migration. Enable the new workflow explicitly on an existing rehearsal topic, then use a fresh topic created with the presentation settings and participant notices. Keep reviewed/manual use available throughout.

The workflow is complete when a presenter can set up their topic and account before a session, project the page, receive several responses per participant and live emoji feedback, and see summaries keep updating through sustained input without closing, freezing or republishing. Verify stopping/restarting a runner and reloading/reconnecting clients without losing accepted summaries, human contributions or ownership cookies.

Provider eligibility and limits still come from the presenter's plan and workplace configuration. The website has no shared provider account. Scale beyond one application process would require cross-instance notifications and a different SQLite/deployment assessment; that is not needed for the initial self-hosted event deployment unless the rehearsal measurements show otherwise.

## Progress log

| When | Entry |
|---|---|
| 2026-10-25 10:00 | LF-1 implemented, pending review. Build clean, all domain tests pass, model matches migrations, and the migration was checked against a copy of an existing development database. |
| 2026-10-25 10:38 | LF-1 reshaped in review: wording and timestamps moved from `Response` to revisions, and revisions no longer carry an author. Same checks repeated. |
| 2026-10-25 11:32 | LF-3 implemented, pending review. Build clean, all domain tests pass, and model matches migrations. The migration was checked on a fresh database and on a copy of the development database. The respond flow was exercised in the running app with two cookie jars. |
| 2026-10-25 14:41 | LF-2 implemented on top of LF-3, pending review. Build clean, all domain tests pass, and model matches migrations. The migration was checked on a fresh database and on a copy of the development database. Live writes, revision conflicts and the admin pages were exercised in the running app. |
| 2026-10-25 15:03 | LF-4 implemented on top of LF-2, pending review. Build clean, all domain tests pass, and model matches migrations. The migration was checked on a fresh database and on a copy of the development database. Toggling, counts, keyboard use and reacting without having responded were exercised in the running app with two cookie jars. |

## Provider guidance checked during planning

Checked 2026-10-11; recheck when implementing the runner, because authentication and billing guidance can change. These sources establish documented capabilities, not an event-specific approval from either provider.

- Anthropic documents [programmatic Claude Code execution](https://code.claude.com/docs/en/headless), [subscription allowance for `claude -p`](https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan), and [authentication/credential restrictions](https://code.claude.com/docs/en/legal-and-compliance). Use the official installed client under the presenter's own account; do not add third-party Claude account login or credential interception to the site.
- OpenAI documents [non-interactive Codex execution](https://learn.chatgpt.com/docs/non-interactive-mode) and [Codex authentication](https://learn.chatgpt.com/docs/auth). Confirm the presenter's authentication/billing mode in preflight; API-key authentication and subscription authentication are different usage paths.

Direct server-side API generation with immutable provider configuration, presenter account login on the site, response comments, expanded emoji choices, moderation queues, cross-topic summaries, translations and a multi-instance deployment remain separate follow-up work. None is required for this presentation workflow.

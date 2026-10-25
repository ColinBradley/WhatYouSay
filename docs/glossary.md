# Glossary

The terms this project uses in code, comments and docs. Use these words rather than synonyms, so a reader never wonders whether two words mean two things.

Terms marked *planned* come from the [live feedback plan](plans/live-feedback-plan.md) and don't exist in the code yet.

| Term | Meaning |
|---|---|
| Admin | Whoever holds a topic's admin password. There are no accounts; the password is per topic. |
| Agent | A model writing a summary through the summariser API. Contrast with a person editing in the admin editor. |
| Anonymous | The identity setting that collects no author name. Not collected, rather than collected and hidden. Whether times are kept is the separate timing setting. |
| Author | The name a responder or commenter gives. Self-declared and unverified. |
| Briefing | The plain-text job description served at `ai-summary-start`, written for a model to read. |
| Carry forward | Sending a node in a draft by id alone, so its text and citations stay exactly as stored. |
| Citation | A reference, seen from the node it supports. |
| Close | Stop a topic accepting responses. Closing currently also freezes every response. |
| Comment | Text a reader attaches to a node of a summary. |
| Current revision | A response's latest revision. New citations are only located in it. |
| Draft | A summary not yet published. Only admins can read it. |
| Freeze | Prevent a response's responder from editing or withdrawing it. A frozen response stays frozen. |
| Grounding | The rule that every branch of a summary tree ends in something a responder actually wrote. Ungrounded means a branch that doesn't. |
| Identity | A topic's setting for how much it asks of responders: Required, Optional or Anonymous. Fixed once anyone has responded. |
| Input revision | *Planned.* A counter that moves whenever anything the summariser reads changes. |
| Live summary | *Planned.* The one summary a topic keeps updating automatically during a session. |
| Node | One assertion in a summary tree: a few words to a sentence. Nodes are untyped. |
| Note tree | The tree of nodes in a summary, as readers see it. |
| Overview | A summary's markdown body: two or three paragraphs that complement the tree rather than restate it. |
| Publish | Make a summary readable by everyone with the link. Publishing ends its draft state. |
| Quote | The exact span of a response revision that a reference cites. |
| Reaction | A fixed emoji kind toggled on a node: Agree, Disagree, Important, Question, Celebrate or Laugh. |
| Reactor | Whoever toggles a reaction, identified only by a cookie token's hash. |
| Reference | A stored quote of one response revision, with its offsets, attached to a node. |
| Responder | Someone who submits responses. Identified only by the token in their cookie, which owns every response that browser gives to the topic. |
| Response | What a responder submits to a topic. A responder may submit several. Its id is a random Guid, so it reveals no timing. |
| Revision | One wording of a response, with when it was submitted where the topic records timing. Editing a response's wording adds a revision. |
| Runner | *Planned.* The local script that polls a topic and invokes the presenter's model client. |
| Summariser | Whatever writes summaries through the summariser API, usually an agent. |
| Summariser token | A topic-scoped bearer token for the summariser API. Only its hash is stored. |
| Summary | One version of a topic's overview and note tree. Each generation run creates a new one. |
| Timing | A topic's setting for whether it records when responses, edits, comments and reactions arrive: Recorded or NotRecorded. Recorded topics list responses newest first. New topics record timing. Fixed once anyone has responded. |
| Topic | A question put to a group. A title and description make up the whole prompt. |
| Topic code | The short url-safe code naming a topic in `/topics/{code}`. |
| Version | One summary among a topic's summaries. Human edits change a version in place; a new run makes a new one. |
| Withdraw | A responder removing one of their own responses. Withdrawn responses are soft-deleted and hidden from every read, citations included. |

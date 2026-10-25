# Writing comments and docs

This guide covers comments in code and build files, doc comments, and prose in `docs`.
The reader has the code in front of them, but none of the context you had while writing.
They may read it months later, when the task and its discussion are long gone.

## What a comment is for

A comment says why: the reason for a choice, a constraint, or a risk the code can't show.
It says what only when the code can't show that quickly.
A doc comment's summary, a format, a unit, or what a result means are all things the code can't show quickly.
Docs are the other way round: mostly what, with some why.

Leave out:

- what the code already says plainly;
- a list of the code's branches, when one sentence of intent would do;
- history and debate: what was tried, decided against or replaced;
- a justification for a convention that the project's guidance already states;
- editorialising, such as "worth knowing" or "earns its place";
- in a doc comment, reasoning aimed at someone reviewing the code rather than information for someone calling it;
- anything tied to the work in progress, such as the request or prompt, the failure that revealed a problem, a temporary or planning doc, or a work item.

Add a fact only when a reader would otherwise get something wrong.
If the code needs no comment, write none.
Design rationale lives in `docs/plans`, and code doesn't link to it.

## How to write a sentence

- **Lead with what the thing is or does.** Conditions and details come after the main statement. A comment above a block of code starts with what the block is for, then gives the steps.
- **One fact per sentence.** Give each case its own sentence, or use a list.
- **Name the thing.** "It", "its", "this", "that", "those" and "in that case" make the reader work out what you mean. So does a possessive that stands for a noun, such as "the peer's" for "the peer's hello".
- **Keep "that" and "which" in long phrases.** "The calls the host holds" reads fine, but "the bytes the calls in every queue hold" makes the reader hunt for where the subject ends.
- **Don't chain facts with commas.** A clause after a comma, such as ", which …", ", with …" or ", if any", usually wants to be a sentence of its own.
- **Give the reason beside the claim**, with "because", "so" or "otherwise".
- **Say what a mechanism achieves.** A comment that describes how something is done also says why. "Receiving a frame only notes the time" needs "which is cheaper than re-arming the timer for every frame".
- **Start similar sentences the same way.** Varied wording makes the reader look for a difference that isn't there.
- **Use the project's terms, not private idioms.** The [glossary](glossary.md) lists them.
- **Prefer plain words to colloquial ones**: "permanently", not "for good".
- **Avoid "at once".** It means both "immediately" and "at the same time".
- **Say what each result means.** Say when a method returns false or null, and what throws. Leave it out when the member follows a convention a reader already knows, such as a `Try` method that returns false on failure.
- **Give an example only where the idea is hard to follow without one.** Never list current class or method names as examples.
- **Longer is fine when it reads easily.** Several short sentences beat one packed sentence of fewer words.
- **Break comment lines at punctuation.** When a comment wraps, end each line at a sentence or clause break, not at the last word that fits.

## Examples

These come from another codebase, so the names in them don't matter here.
Code in backticks stands for `<see cref>`, `<paramref>` and `<c>` in doc comments.

A clause tucked inside a noun phrase:

- Before: "The curve the delays the policy is given follow."
- After: "How the delay before each redial grows. `Policy` receives each delay as `ReconnectContext.Delay`."

Pronouns with no name to anchor them:

- Before: "Once it has opened, the peer may have it, so it counts as sent whether it was refused or its connection has closed."
- After: "The peer may have the event as soon as its stream opens. So the send counts as done even if the peer later refuses it or the connection closes."

A condition in the way, and a result whose meaning is left to the reader:

- Before: "Takes a turn out of the queue, unless the gate has let it through or never had it, and returns whether it did."
- After: "Removes a turn that is still waiting in the queue. Returns false if the turn has already started or was never queued."

Similar facts, worded the same way:

- Before: "The calls a connection serves at once." "The calls in an instance's queue." "The body bytes the calls in an instance's queue hold."
- After: "How many calls a connection may serve at the same time." "How many calls an instance's queue may hold." "How many bytes of request body an instance's queue may hold."

The code's branches retold, where the intent would do:

- Before: "Hands a request to the application, or to the host as it arrives, or answers it at once when this side serves nothing at its address or no such method, when a cancel for it has arrived first, or when the connection serves as many calls as it may."
- After: "Passes a request on as soon as its header arrives: to the host, or to the application when there is no host. A request that can't be served is answered straight away instead."

A good reason, let down by an idiom and a "which" with an unclear referent:

- Before: "A response that carries an issue has no body, so it is read to its End rather than reset, which would fail its sender's Complete for nothing."
- After: "A response that carries an issue has no body. Read it to its `End` instead of resetting it, because a reset would make the sender's `Complete` fail when nothing went wrong."

A comment written for the task rather than the reader:

- Before: "Raised to 2 s, since 1 s made the liveness test flaky on the build server."
- After: "A ping takes up to a second to arrive on a busy machine, so allow twice that."

## Reviewing comments

Use these steps to review comments you were asked to review, and for the writing pass that ends every piece of work.

### The writing pass

Work ends with a writing pass, much as it ends with a test run.
Once no more changes are expected, review every comment, doc comment and doc that the work added or changed.
Do it once, at the very end, so that it covers the final text.
If the pass changes code files, build again.
Then say in your summary that you did a writing pass, and list any reasons you added that the author should confirm.
Review commit messages and pull request text the same way when you write them.

### Steps

1. **Read the code first.** Read the code the comment describes, and enough around it to know what it does and why.
2. **Work out what the comment means.** If you can't, trace the code until you can. Don't rewrite from a guess. If you still can't tell, leave the comment and ask its author.
3. **Decide to keep, rewrite or delete.** Delete a comment that only says what the code says plainly. Keep one that already follows this guide, even if more could be said.
4. **Rewrite from the code, not from the old comment.** Write the new comment from what the code does, by the rules above. Then check that each fact in the original survived, unless the code shows it plainly. Don't edit the old sentences into shape, because their phrasing comes along with them.
5. **Make related writing match.** When you change a comment, update other writing that says the same thing, such as other overloads or docs.
6. **Point out reasons you added.** If you give a reason the original didn't, say so when you hand over the change, so that the author can confirm it.
7. **Reread as a stranger.** Read each sentence once, as someone who knows the code but not your work. If a sentence needs a second pass, rewrite it, then go through the checklist.

### Checklist

- Does the first sentence say what the thing is or does?
- Is every "it", "its", "this", "that" and "those" one that only one thing could mean? Would a name be clearer?
- Is any sentence longer than about 20 words, or does it have more than one comma? Split it.
- Does a clause after a comma add another fact? Make it a sentence.
- Is a noun followed by a clause with no "that" or "which"?
- Does the comment retell the code's branches instead of giving the intent?
- Does it reuse a phrase from the original that only its author would understand?
- Does it mention the task, a prompt, a temporary doc, a failure you chased, or an option you rejected?
- Is the reason there, next to the statement it explains?
- Does it say what each result and each failure means?

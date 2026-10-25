using System.Text;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The whole job, in plain text, at one URL. Read by a model rather than parsed, so it is
/// prose with literal URLs rather than a schema document. It carries the topic's current
/// state inline so the obvious first call is already answered.
/// </summary>
public static class SummariserBriefing
{
    /// <summary>
    /// How a summary tree is built. Shared with the model instructions, so the briefing and
    /// a runner's model are held to the same rules.
    /// </summary>
    internal const string Shape =
        """
        THE SHAPE: A TREE OF NODES

        A node has text, references, and children.
        Meaning a node can be a category, a claim, a single detail - anything.

        One node carries one assertion. If a node's text needs "and", or a second
        sentence, it is probably two nodes. Elaboration happens through child nodes
        and references, never through longer prose inside a node. A sentence is fine
        where a sentence is what the assertion takes.

        Because nothing but the text says what a node means, the text has to carry it.
        "A real budget held by the team" reads as something that is the case. If it is
        something people want, write it as one: "A bigger budget wanted."

        If B is the reason to believe A, B is a child of A, not its sibling.
          wrong:  Managers change too often.
                  Four in two years.
          right:  Managers change too often.
                    Four in two years.

        A node with children reads as the heading over them, and needs no quotes of its
        own. A node with no children is where the summary meets the responses, so that is
        where the quotes have to be.

        One response usually becomes several nodes, often in different branches.
        Splitting is a judgement about meaning, not about punctuation.
        Text should never say something like "x people said.." etc, references should show that.

        Go as deep as the detail warrants. There is no cap, and no target for how many
        nodes to produce. A long chain of single children is a sign the middle of it is
        thin, not that depth is wrong.
        """;

    /// <summary>The grounding rules that every write is checked against. Shared with the model instructions.</summary>
    internal const string Rules =
        """
        TWO RULES, BOTH ENFORCED

        1. Every branch of the tree ends in a reference to a response. A node with no
           children must either cite for itself or sit under a node that does - support
           inherits downwards, so a node under a cited one need not cite again. What this
           rejects is a branch that never touches anything anybody wrote.
           All instances across all responses should be cited, not just exemplars.
           The responses are the main content to be exposed.
        2. Every quote is copied out of the response revision it names. Do not paraphrase
           inside a quote, do not drop a word, and do not correct a typo - those change
           what was said and are refused. Punctuation and spacing are forgiven: a curly
           apostrophe, an em dash typed as a hyphen, a doubled space or a different case
           will still match, and what gets stored is the response's own text.
        """;

    /// <summary>How a write treats the nodes of the summary it replaces. Shared with the model instructions.</summary>
    internal const string Revising =
        """
        REVISING AN EXISTING SUMMARY

        A write does not rebuild the tree. Each node says what you are doing with it:

          id + text     revise it - the text is yours now, so it needs a citation
          id, no text   carry it forward exactly as it stands
          text, no id   a new node - it needs a citation
          id left out   archive it - it leaves the tree, and the admin keeps any
                        reactions and comments people left on it

        Position and nesting always come from what you send, so a bare {"id": 12} can be
        moved, reparented and reordered freely.

        Keep the id of every node whose point still holds, even when you move it or reword
        it. Each id appears at most once: when you split a node, the id stays with one
        part and the others are new. Reactions and comments belong to the node id, so a node rebuilt without its id
        loses them. Leave settled wording alone: a summary that changes everywhere on each
        update is hard to follow.

        You will find nodes you cannot cite. A person wrote them - people may assert
        without a quote, and you may not. That is not a defect to tidy up. Carry them
        forward by id and they survive untouched; the moment you supply text for one,
        that text is your assertion and needs a quote of its own.

        Responses change while a topic is open. Each response has a revision number that
        goes up with every edit, and each reference names the revision its quote was
        copied from. A reference in the existing tree may name an earlier revision. If
        the current wording still supports that node, cite the current wording instead;
        if it no longer does, revise or archive the node. A withdrawn response simply
        disappears, along with every quote of it.
        """;

    /// <summary>What separates a useful summary from a merely valid one. Shared with the model instructions.</summary>
    internal const string Quality =
        """
        WHAT MAKES IT WORTH PUBLISHING

        - Let the responses decide the structure. Do not start from the question and hunt
          for support.
        - The body is prose about what people actually said. Not a restatement of the
          tree, and not a restatement of the question.
        - Where responses disagree, say so and cite both sides. Do not average them into
          a consensus nobody expressed.
        - Something one person said can still deserve a node. Reactions show emphasis,
          not votes: a point many people back can lead, but a point with no reactions is
          never dropped or merged away for that reason.
        - Don't suppress responses that answer a different question than asked.
        - Comments are readers saying what the summary got wrong or missed. Answer them
          in the next version, rather than starting cold.
        - Follow the organiser's guidance where it is given. A length target is a target:
          group points to approach it, but never drop a distinct point to meet it.

        Name people who gave a name, and refer to an anonymous response as a response
        and nothing more.
        """;

    /// <param name="liveSummaryId">The topic's live summary. Null when it has none.</param>
    public static string For(
        Topic topic,
        string baseUrl,
        int responseCount,
        int summaryCount,
        Guid? liveSummaryId
    )
    {
        var api = $"{baseUrl}/api/topics/{topic.Code}";
        var status = topic.IsAcceptingResponses ? "still open" : "closed to new responses";
        var live = topic.SummaryPolicy == SummaryPolicy.Live;

        // Doubled braces open an interpolation here, which leaves the single braces of the
        // JSON example and the {id} placeholders as literal text.
        var text = new StringBuilder(
            $$"""
            You are writing a summary of a WhatYouSay topic. Everything you need is below.

            HOW TO WORK

            {{(live ? LiveWork : ReviewedWork)}}

            THE TOPIC

              Title      {{topic.Title}}
              Question   {{topic.Description}}
              Responses  {{responseCount}}
              Summaries  {{summaryCount}} so far
              Status     {{status}}
              Identity   {{IdentityLine(topic.ResponseIdentity)}}
              Timing     {{TimingLine(topic.ResponseTiming)}}
              Updates    {{UpdatesLine(topic.SummaryPolicy)}}

            GUIDANCE FROM THE ORGANISER

              Expected feedback   {{topic.ExpectedFeedback ?? "none given"}}
              Instructions        {{topic.SummarisingInstructions ?? "none given"}}
              Length target       {{topic.SummaryLengthTarget ?? "none given"}}

            AUTHENTICATION

            Every endpoint below takes the token you already have:

              Authorization: Bearer <your token>

            The token is the credential. Keep it out of anywhere public. It is scoped to this
            one topic and cannot reach another.

            WHAT TO CALL

            {{(live ? LiveCalls(api, liveSummaryId) : ReviewedCalls(api))}}

            And, for reading round the edges:

              GET  {{api}}/responses?take=200
                   Live responses with the total, the input revision and reaction counts,
                   in pages. Pass after=<the last id you got> for the next page, so
                   responses arriving meanwhile do not shift it.

              GET  {{api}}/summaries
                   Every version, newest first, each saying whether you may write to it,
                   whether it is the live summary, and its revision.

              GET  {{api}}/summaries/{id}
                   One version in full: the tree, the quotes each node cites, and the
                   node ids you need to revise it.

              GET  {{api}}/summaries/{id}/reactions
                   Counts per node, by kind.

              GET  {{api}}/summaries/{id}/comments
                   What the group said back about the current tree, in their own words,
                   with every response each commenter gave listed beside their comment. A
                   comment is about its node, not about any one of those responses.

            {{Shape}}

            WHAT TO SEND

            A JSON object shaped like this, nested as deep as you need. References and
            children are both optional. Do not send offsets for quotes: the app finds them,
            so a wrong one is not expressible. Build the payload with code if you can and
            check each quote is a substring of the response revision before sending; you are
            holding the responses already, so that catches most rejections for free. If you
            cannot, write it out and re-read each quote against the response first.

              {
                "body": "Two or three paragraphs of markdown.",
                "nodes": [
                  {
                    "text": "What went well",
                    "children": [
                      {
                        "text": "What this node says.",
                        "references": [
                          {
                            "responseId": "a response id",
                            "revision": 1,
                            "quote": "copied exactly out of that revision's body"
                          }
                        ],
                        "children": [
                          {
                            "text": "A qualification on the node above.",
                            "references": [
                              {
                                "responseId": "another response id",
                                "revision": 2,
                                "quote": "also copied exactly"
                              }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }

            "revision" is the response revision the quote is copied from, as /live or
            /responses gave it. Live writes require it. Elsewhere it may be left out, and the
            quote is then looked for in the response's current revision.

            {{Rules}}

            {{Revising}}

            Nothing is saved unless the whole request passes. A rejection comes back as 422
            listing every problem found in one pass, each naming the node it is in by a JSON
            pointer, and for a quote that missed, the response text you were probably reaching
            for. Fix them all and send it again.

            {{Quality}}

            Apart from the live summary, start a new summary rather than continuing somebody
            else's. A version being open to you is the admin's decision, not an invitation.

            Example partial notes from a dev sprint retro:
              • What went well
                ○ John
                  § Training went well!
                ○ Atlas
                  § Got a lot done
                ○ 3D
                  § Knew what was happening (what people are working on)
              • What did we learn
                ○ Atlas
                  § UI testing is hard
                    □ Bothersome to maintain
                    □ Might be good to get tech to manage it all
                ○ 3D
                  § Filing issues before they are done is problematic
                    □ Splits mind share and takes time
                    □ Level of detail in an issue can be tricky
                      ® Doing investigation / vs being clear for other people
                    □ Yak shaving issues (finding issues while investigating)
                  § There can be slow downs when opening
              • Ideas for improvement
                ○ Shorter and simpler work
                ○ Improve documentation
                  § Include examples of work
                  § Make it part of the testing
                  § Feature is not done until really great documentation is made that could replace training
                ○ Finer planning/tracking
                  § Jira plugins to make it more like VSTS or something
                ○ Varied work (keep people moving around)
                  § Features/bugs, DW/Tools/Atlas, Web/3D/UI/backend/server, C#/TS/VB/F#
                ○ More pair programming
                  § Inexperienced with experienced only
            """
        );

        text.Append(live ? LiveSection(api) : ReviewSection(api));

        return text.ToString();
    }

    private const string LiveWork =
        """
        This topic updates its summary hands-free while people are still responding. Do
        one pass: read the snapshot, write the next version of the live summary, and stop.
        Nobody reviews it first and nobody is waiting to answer questions, so use your own
        judgement and send a complete summary. Do not loop or poll. Keeping the summary
        current through a session is the job of a separate script that calls a model only
        when the input changes.
        """;

    private const string ReviewedWork =
        """
        Read every response first. Propose a tree and agree the structure before you
        post. Posting is not final - a draft can be replaced with PUT as many times
        as you like, so re-submit freely rather than trying to land it in one shot.
        """;

    private static string LiveCalls(string api, Guid? liveSummaryId)
    {
        var current = liveSummaryId is { } id
            ? $"Its live summary is {id}."
            : "It has no live summary yet, so your first write creates it.";

        return $$"""
            {{current}}

              GET  {{api}}/live
                   Everything you summarise, read in one consistent snapshot. See LIVE
                   UPDATES below.

              PUT  {{api}}/live
                   The whole next version of the live summary. It is published as soon as
                   it is accepted.
            """;
    }

    private static string ReviewedCalls(string api) =>
        $$"""
          GET  {{api}}/live
               Everything you summarise, read in one consistent snapshot: the guidance,
               every response with its revision and reaction counts.

          POST {{api}}/summaries
               Your draft, whole, in one request. Returns the id and an edit URL.

          PUT  {{api}}/summaries/{id}
               Revises a draft that is open to you. Send "basedOnRevision", the revision
               you read from GET /summaries/{id}, alongside "body" and "nodes"; if anyone
               has written to it since, nothing is saved and you get a 409.
        """;

    private static string LiveSection(string api) =>
        $$"""


        LIVE UPDATES

        This topic keeps one live summary, which every accepted write revises in place and
        publishes straight away.

        GET {{api}}/live returns one snapshot, read in a single consistent read:

          inputRevision        Counts changes to anything you summarise: responses,
                               edits, withdrawals, reactions, comments and guidance.
                               Writing a summary never moves it.
          isPending            Whether the live summary was generated from older input.
          isWritable           Whether a write would be accepted now. An admin can pause
                               the summariser, or switch the topic to review.
          liveSummary          Its id, its revision, and the inputRevision it was
                               generated from. Null when there is no live summary yet.
          input                The topic, the guidance, every response with its revision
                               and reaction counts, and the live summary's tree with the
                               reactions and comments on each node.
          model                Instructions and an output JSON schema, for a script that
                               hands the input to a model.

        The response carries an ETag. Send it back as If-None-Match, and an unchanged
        topic answers 304 with no body.

        PUT {{api}}/live takes the whole summary, plus two numbers from the snapshot:

          "basedOnRevision"    liveSummary.revision, or null when there was no live summary
          "inputRevision"      the snapshot's inputRevision

        How a write is judged:

        - Anyone writing to the live summary after you read it, a person included, means
          nothing is saved: 409 stale_revision. Read again and redo your changes.
        - Somebody creating the live summary after you read it gets you 409
          live_summary_exists. Read again and revise that one.
        - New feedback arriving while you worked does not reject your write. It is
          accepted for the input you read, and isPending stays true until the next pass.
        - A quote of a response that has since been withdrawn or hidden is refused, with
          the rest of the write. Read again.
        - While an admin has paused the summariser, writes get 409
          summary_not_agent_editable.

        A successful write returns the summary's new revision, the inputRevision it was
        accepted for, and the topic's current inputRevision.

        The older PUT {{api}}/summaries/{id} still revises the live summary, but records no
        inputRevision, so the live summary shows as pending afterwards. A POST while a live
        summary exists makes a private draft for the admin to review.
        """;

    private static string ReviewSection(string api) =>
        $$"""


        REVIEW

        What you write is a draft. The admin reviews it and publishes it - you cannot.
        Send "inputRevision" from GET {{api}}/live with a draft, and the admin can see
        whether newer feedback has arrived since.
        """;

    private static string UpdatesLine(SummaryPolicy policy) =>
        policy == SummaryPolicy.Live
            ? "live - accepted writes to the live summary are published straight away"
            : "reviewed - what you write stays a draft until the admin publishes it";

    private static string IdentityLine(ResponseIdentity identity) =>
        identity switch
        {
            ResponseIdentity.Anonymous =>
                "anonymous - no names were ever recorded, so there is no privileged view to "
                + "ask for",
            ResponseIdentity.Optional => "optional - some responses carry a name, some do not",
            _ => "required - every response carries a name",
        };

    private static string TimingLine(ResponseTiming timing) =>
        timing switch
        {
            ResponseTiming.NotRecorded =>
                "not recorded - no times were ever stored, so the order of responses means "
                + "nothing",
            _ => "recorded - responses carry when they were submitted and are listed newest first",
        };
}

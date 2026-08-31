using System.Text;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The whole job, in plain text, at one URL. Read by a model rather than parsed, so it is
/// prose with literal URLs rather than a schema document. It carries the survey's current
/// state inline so the obvious first call is already answered.
/// </summary>
public static class SummariserBriefing
{
    public static string For(
        Survey survey,
        string baseUrl,
        int responseCount,
        int summaryCount
    )
    {
        var api = $"{baseUrl}/api/surveys/{survey.Code}";
        var status = survey.IsAcceptingResponses ? "still open" : "closed to new responses";

        // Doubled braces open an interpolation here, which leaves the single braces of the
        // JSON example and the {id} placeholders as literal text.
        var text = new StringBuilder(
            $$"""
            You are drafting a summary of a WhatYouSay survey. Everything you need is below.

            HOW TO WORK

            Read every response first. Propose a tree and agree the structure before you
            post. Posting is not final - a draft can be replaced with PUT as many times
            as you like, so re-submit freely rather than trying to land it in one shot.

            THE SURVEY

              Title      {{survey.Title}}
              Question   {{survey.Description}}
              Responses  {{responseCount}}
              Summaries  {{summaryCount}} so far
              Status     {{status}}
              Identity   {{IdentityLine(survey.ResponseIdentity)}}

            AUTHENTICATION

            Every endpoint below takes the token you already have:

              Authorization: Bearer <your token>

            The token is the credential. Keep it out of anywhere public. It is scoped to this
            one survey and cannot reach another.

            WHAT TO CALL

              GET  {{api}}/responses
                   Every live response. Each carries the id you must cite it by.

              POST {{api}}/summaries
                   Your draft, whole, in one request. Returns the id and an edit URL.

            And, for a second pass over an existing summary:

              GET  {{api}}/summaries
                   Every version, newest first. Drafts are editable, published ones are not.

              GET  {{api}}/summaries/{id}
                   One version in full, including the quotes each node cites.

              GET  {{api}}/summaries/{id}/reactions
                   What responders said back: agree and important counts per node, plus
                   every "this misrepresents me" objection in full.

              PUT  {{api}}/summaries/{id}
                   Replaces a draft you created. Same rules as POST.

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

            Iterate and clarify with users to come to an agreed structure.

            WHAT TO POST

            A JSON object shaped like this, nested as deep as you need. References and
            children are both optional. Do not send offsets for quotes: the app finds them,
            so a wrong one is not expressible.

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
                            "responseId": "an id from /responses",
                            "quote": "copied exactly out of that response's body"
                          }
                        ],
                        "children": [
                          {
                            "text": "A qualification on the node above.",
                            "references": [
                              {
                                "responseId": "another id from /responses",
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

            TWO RULES, BOTH ENFORCED

            1. Every branch of the tree ends in a reference to a response. A node with no
               children must either cite for itself or sit under a node that does - support
               inherits downwards, so a node under a cited one need not cite again. What this
               rejects is a branch that never touches anything anybody wrote.
               All instances across all responses should be cited, not just exemplars.
               The responses are the main content to be exposed.
            2. Every quote is copied character for character out of that response's body.

            Do not paraphrase inside a quote, do not tidy punctuation, do not correct a typo,
            and do not run text together across a line break. Straight quotes and apostrophes
            must stay straight; a curly one substituted in is the most common rejection there
            is, and it is invisible unless you look for it.

            Nothing is saved unless the whole request passes. A rejection comes back as 422
            listing every problem found in one pass, each naming the node it is in by a JSON
            pointer, and for a quote that missed, the response text you were probably reaching
            for. Fix them all and send it again.

            WHAT MAKES IT WORTH PUBLISHING

            - Let the responses decide the structure. Do not start from the question and hunt
              for support.
            - The body is prose about what people actually said. Not a restatement of the
              tree, and not a restatement of the question.
            - Where responses disagree, say so and cite both sides. Do not average them into
              a consensus nobody expressed.
            - Something one person said can still deserve a node.
            - don't suppress responses that answer a different question than asked.

            On a second pass, read the reactions first and fix the nodes people objected to
            rather than starting cold.

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

        if (survey.IsAcceptingResponses)
        {
            text.Append(
                $$"""


                BEFORE YOU START

                This survey is still accepting responses, so POST {{api}}/summaries will
                refuse. Summarising a moving target produces quotes that stop matching. Ask
                whoever gave you this token to close the survey first.
                """
            );
        }

        return text.ToString();
    }

    private static string IdentityLine(ResponseIdentity identity) =>
        identity switch
        {
            ResponseIdentity.Anonymous =>
                "anonymous - no names or timestamps were ever recorded, so there is no "
                + "privileged view to ask for",
            ResponseIdentity.Optional => "optional - some responses carry a name, some do not",
            _ => "required - every response carries a name",
        };
}

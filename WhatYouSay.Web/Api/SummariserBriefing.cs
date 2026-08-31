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
                   One version in full, including the quotes each point cites.

              GET  {{api}}/summaries/{id}/reactions
                   What responders said back: agree and important counts per point, plus
                   every "this misrepresents me" objection in full.

              PUT  {{api}}/summaries/{id}
                   Replaces a draft you created. Same rules as POST.

            WHAT TO POST

            A JSON object shaped like this. Sentiment runs -1 (negative) to +1 (positive),
            objectivity 0 (pure opinion) to 1 (verifiable fact), and intensity 0 to 1 for how
            strongly a quote carries its point. All three are optional. Do not send offsets
            for quotes: the app finds them, so a wrong one is not expressible.

              {
                "body": "Two or three paragraphs of markdown.",
                "topics": [
                  {
                    "name": "Topic name",
                    "description": "Optional one-liner, or null.",
                    "points": [
                      {
                        "description": "What this point says.",
                        "sentiment": -0.4,
                        "objectivity": 0.5,
                        "references": [
                          {
                            "responseId": "an id from /responses",
                            "quote": "copied exactly out of that response's body",
                            "intensity": 0.8
                          },
                        ]
                      },
                    ]
                  },
                ]
              }

            TWO RULES, BOTH ENFORCED

            1. Every point cites at least one response, by its id.
            2. Every quote is copied character for character out of that response's body.

            Do not paraphrase inside a quote, do not tidy punctuation, do not correct a typo,
            and do not run text together across a line break. Straight quotes and apostrophes
            must stay straight; a curly one substituted in is the most common rejection there
            is, and it is invisible unless you look for it.

            Nothing is saved unless the whole request passes. A rejection comes back as 422
            listing every problem found in one pass, each naming the field it is in, and for
            a quote that missed, the response text you were probably reaching for. Fix them
            all and send it again.

            WHAT MAKES IT WORTH PUBLISHING

            - Group what came back into topics, and each topic into a few points. Let the
              responses decide the topics. Do not start from the question and hunt for
              support.
            - The body is prose about what people actually said. Not a restatement of the
              topic list, and not a restatement of the question.
            - Where responses disagree, say so and cite both sides. Do not average them into
              a consensus nobody expressed.
            - Something one person said can still deserve a point. Say that it was one person.
            - If the responses answer a different question than the one that was asked,
              summarise what they actually said and say so plainly.

            On a second pass, read the reactions first and fix the points people objected to
            rather than starting cold.

            WHAT HAPPENS NEXT

            What you produce is a draft. A human reviews and publishes it. You cannot publish,
            and nothing you write reaches responders until they do.
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

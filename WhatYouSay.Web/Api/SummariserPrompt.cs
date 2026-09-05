using WhatYouSay.Data;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The hand-off an organiser copies out of the admin page and pastes into a chat. Says
/// only where to start: the rules, the shape and the endpoints live behind that URL, so
/// they can change without anyone re-pasting this.
/// </summary>
public static class SummariserPrompt
{
    // Paragraphs are single lines. A chat box reflows them, and hard wraps survive the
    // paste as ragged breaks.
    public static string For(Topic topic, string baseUrl, string token) =>
        $"""
        Please write me a draft summary of the topic "{topic.Title}", which lives in WhatYouSay.

        Start by fetching this, which explains the whole job and every other endpoint:

        curl -H "Authorization: Bearer {token}" {baseUrl}/api/topics/{topic.Code}/ai-summary-start

        That token is the credential, so keep it out of anywhere public. It reaches this one topic and nothing else.

        What you produce is a draft. I review it and publish it - you cannot.
        """;
}

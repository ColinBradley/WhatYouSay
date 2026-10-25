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
        {Opening(topic)}

        Start by fetching this, which explains the whole job and every other endpoint:

        curl -H "Authorization: Bearer {token}" {baseUrl}/api/topics/{topic.Code}/ai-summary-start

        That token is the credential, so keep it out of anywhere public. It reaches this one topic and nothing else.

        {Publishing(topic.SummaryPolicy)}
        """;

    private static string Opening(Topic topic) =>
        topic.SummaryPolicy == SummaryPolicy.Live
            ? $"Please update the live summary of the topic \"{topic.Title}\", which lives in WhatYouSay."
            : $"Please write me a summary of the topic \"{topic.Title}\", which lives in WhatYouSay.";

    private static string Publishing(SummaryPolicy policy) =>
        policy == SummaryPolicy.Live
            ? "This topic is live and hands-free: what you write to its live summary is published straight away, and I won't be reviewing it first. Make one complete pass and stop. Don't set up a loop or keep polling - keeping the summary current through a session is the job of a separate script."
            : "What you produce is a draft. I review it and publish it - you cannot.";
}

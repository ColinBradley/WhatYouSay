using System.Diagnostics;
using System.Diagnostics.Metrics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class WhatYouSayTelemetry
{
    public const string ServiceName = "WhatYouSay";

    public const string MeterName = ServiceName;

    /// <summary>
    /// Stands in for the code of an anonymous topic. Spans and metrics carry timestamps,
    /// so tagging one with the code would turn the trace store into the per-topic
    /// submission log that anonymous mode gives up <see cref="Response.CreatedAt"/> to
    /// avoid. Does not hide a lone anonymous topic's request timing.
    /// </summary>
    public const string RedactedTopic = "(anonymous)";

    /// <summary>One source per assembly, named after it.</summary>
    public static ActivitySource Source { get; } = new(ServiceName);

    private static readonly Meter sMeter = new(MeterName);

    private static readonly Counter<long> sResponsesSubmitted = sMeter.CreateCounter<long>(
        "whatyousay.responses.submitted",
        unit: "{response}",
        description: "Responses submitted to a topic.");

    private static readonly Counter<long> sResponsesEdited = sMeter.CreateCounter<long>(
        "whatyousay.responses.edited",
        unit: "{response}",
        description: "Edits made by responders to their own response while a topic is open.");

    private static readonly Counter<long> sResponsesWithdrawn = sMeter.CreateCounter<long>(
        "whatyousay.responses.withdrawn",
        unit: "{response}",
        description: "Responses withdrawn by their author.");

    private static readonly Counter<long> sSummariesViewed = sMeter.CreateCounter<long>(
        "whatyousay.summaries.viewed",
        unit: "{view}",
        description: "Summary page loads that found a published summary to show.");

    private static readonly Counter<long> sSummariesDrafted = sMeter.CreateCounter<long>(
        "whatyousay.summaries.drafted",
        unit: "{summary}",
        description: "Draft summaries accepted after passing grounding validation.");

    private static readonly Counter<long> sSummariesEdited = sMeter.CreateCounter<long>(
        "whatyousay.summaries.edited",
        unit: "{edit}",
        description: "Edits a human made to a draft summary, by kind.");

    private static readonly Counter<long> sTopicsCreated = sMeter.CreateCounter<long>(
        "whatyousay.topics.created",
        unit: "{topic}",
        description: "Topics created.");

    private static readonly Counter<long> sReactionsAdded = sMeter.CreateCounter<long>(
        "whatyousay.reactions.added",
        unit: "{reaction}",
        description: "Reactions a responder added to a summary node, by kind.");

    private static readonly Counter<long> sReactionsRemoved = sMeter.CreateCounter<long>(
        "whatyousay.reactions.removed",
        unit: "{reaction}",
        description: "Reactions a responder took back, by kind.");

    /// <summary>How often an agent cited something it could not substantiate.</summary>
    private static readonly Counter<long> sSummariesRejected = sMeter.CreateCounter<long>(
        "whatyousay.summaries.rejected",
        unit: "{summary}",
        description: "Draft summaries rejected by grounding validation, by reason.");

    /// <summary>
    /// The code, unless the topic is anonymous. Never tag with a raw code; use this or
    /// <see cref="ActivityExtensions.SetTopic"/>.
    /// </summary>
    public static string TagFor(Topic topic) =>
        topic.IsAnonymous 
            ? RedactedTopic 
            : topic.Code;

    public static void ResponseSubmitted(Topic topic) =>
        sResponsesSubmitted.Add(1, Tags(topic));

    public static void ResponseEdited(Topic topic) =>
        sResponsesEdited.Add(1, Tags(topic));

    public static void ResponseWithdrawn(Topic topic) =>
        sResponsesWithdrawn.Add(1, Tags(topic));

    public static void SummaryViewed(Topic topic) =>
        sSummariesViewed.Add(1, Tags(topic));

    public static void SummaryDrafted(Topic topic) =>
        sSummariesDrafted.Add(1, Tags(topic));

    public static void TopicCreated(Topic topic) =>
        sTopicsCreated.Add(1, Tags(topic));

    public static void SummaryEdited(Topic topic, string kind)
    {
        var tags = Tags(topic);
        tags.Add("edit.kind", kind);

        sSummariesEdited.Add(1, tags);
    }

    public static void ReactionAdded(Topic topic, ReactionKind kind) =>
        sReactionsAdded.Add(1, WithKind(topic, kind));

    public static void ReactionRemoved(Topic topic, ReactionKind kind) =>
        sReactionsRemoved.Add(1, WithKind(topic, kind));

    private static TagList WithKind(Topic topic, ReactionKind kind)
    {
        var tags = Tags(topic);
        tags.Add("reaction.kind", kind.ToString());

        return tags;
    }

    public static void SummaryRejected(Topic topic, string reason)
    {
        var tags = Tags(topic);
        tags.Add("rejection.reason", reason);

        sSummariesRejected.Add(1, tags);
    }

    private static TagList Tags(Topic topic)
    {
        return
        [
            new("topic.code", TagFor(topic)),
            new("topic.identity", topic.ResponseIdentity.ToString()),
        ];
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Web.Telemetry;

public static class WebTelemetry
{
    public const string ServiceName = "WhatYouSay.Web";

    public const string MeterName = ServiceName;

    /// <summary>One source per assembly, named after it.</summary>
    public static ActivitySource Source { get; } = new(ServiceName);

    private static readonly Meter sMeter = new(MeterName);

    /// <summary>
    /// Whether agents actually start where the prompt sends them, which is the whole bet
    /// of keeping the instructions server-side.
    /// </summary>
    private static readonly Counter<long> sBriefingsServed = sMeter.CreateCounter<long>(
        "whatyousay.api.briefings.served",
        unit: "{briefing}",
        description: "Fetches of the ai-summary-start briefing."
    );

    private static readonly Counter<long> sRequestsRefused = sMeter.CreateCounter<long>(
        "whatyousay.api.requests.refused",
        unit: "{request}",
        description: "Summariser API requests refused before reaching a handler, by reason."
    );

    private static readonly Counter<long> sRequestsServed = sMeter.CreateCounter<long>(
        "whatyousay.api.requests.served",
        unit: "{request}",
        description: "Summariser API requests that reached a handler, by endpoint."
    );

    public static void BriefingServed(Topic topic) =>
        sBriefingsServed.Add(1, Tags(topic));

    /// <summary>
    /// Carries no topic code: a refusal has no authenticated topic, and the code a bad
    /// token happens to name is not ours to record against an anonymous one.
    /// </summary>
    public static void RequestRefused(string reason) =>
        sRequestsRefused.Add(1, new KeyValuePair<string, object?>("refusal.reason", reason));

    public static void RequestServed(Topic topic, string endpoint)
    {
        var tags = Tags(topic);
        tags.Add("api.endpoint", endpoint);

        sRequestsServed.Add(1, tags);
    }

    private static TagList Tags(Topic topic)
    {
        return
        [
            new("topic.code", WhatYouSayTelemetry.TagFor(topic)),
            new("topic.identity", topic.ResponseIdentity.ToString()),
        ];
    }
}

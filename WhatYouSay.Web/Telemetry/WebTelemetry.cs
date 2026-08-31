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

    public static void BriefingServed(Survey survey) =>
        sBriefingsServed.Add(1, Tags(survey));

    /// <summary>
    /// Carries no survey code: a refusal has no authenticated survey, and the code a bad
    /// token happens to name is not ours to record against an anonymous one.
    /// </summary>
    public static void RequestRefused(string reason) =>
        sRequestsRefused.Add(1, new KeyValuePair<string, object?>("refusal.reason", reason));

    public static void RequestServed(Survey survey, string endpoint)
    {
        var tags = Tags(survey);
        tags.Add("api.endpoint", endpoint);

        sRequestsServed.Add(1, tags);
    }

    private static TagList Tags(Survey survey)
    {
        return
        [
            new("survey.code", WhatYouSayTelemetry.TagFor(survey)),
            new("survey.identity", survey.ResponseIdentity.ToString()),
        ];
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class WhatYouSayTelemetry
{
    public const string ServiceName = "WhatYouSay";

    public const string MeterName = ServiceName;

    /// <summary>
    /// Stands in for a survey code on anything to do with an anonymous survey. Telemetry
    /// carries a timestamp by construction, so tagging a span or metric with the code would
    /// record "someone answered survey allco26 at 14:32" in the trace store — precisely
    /// what declining to store <see cref="Response.CreatedAt"/> was meant to prevent.
    /// It reduces the leak rather than eliminating it: if only one anonymous survey is
    /// running, request timing still says something. It stops the trace store becoming a
    /// per-survey submission log.
    /// </summary>
    public const string RedactedSurvey = "(anonymous)";

    /// <summary>One source per assembly, named after it.</summary>
    public static ActivitySource Source { get; } = new(ServiceName);

    private static readonly Meter sMeter = new(MeterName);

    private static readonly Counter<long> sResponsesSubmitted = sMeter.CreateCounter<long>(
        "whatyousay.responses.submitted",
        unit: "{response}",
        description: "Responses submitted to a survey.");

    private static readonly Counter<long> sResponsesEdited = sMeter.CreateCounter<long>(
        "whatyousay.responses.edited",
        unit: "{response}",
        description: "Edits made by responders to their own response while a survey is open.");

    private static readonly Counter<long> sResponsesWithdrawn = sMeter.CreateCounter<long>(
        "whatyousay.responses.withdrawn",
        unit: "{response}",
        description: "Responses withdrawn by their author.");

    private static readonly Counter<long> sSummariesViewed = sMeter.CreateCounter<long>(
        "whatyousay.summaries.viewed",
        unit: "{view}",
        description: "Summary page loads that found a published summary to show.");

    /// <summary>
    /// The code, unless the survey is anonymous. Never tag with a raw code; go through
    /// this or <see cref="ActivityExtensions.SetSurvey"/> so the rule cannot be forgotten.
    /// </summary>
    public static string TagFor(Survey survey) => survey.IsAnonymous ? RedactedSurvey : survey.Code;

    public static void ResponseSubmitted(Survey survey) => sResponsesSubmitted.Add(1, Tags(survey));

    public static void ResponseEdited(Survey survey) => sResponsesEdited.Add(1, Tags(survey));

    public static void ResponseWithdrawn(Survey survey) => sResponsesWithdrawn.Add(1, Tags(survey));

    public static void SummaryViewed(Survey survey) => sSummariesViewed.Add(1, Tags(survey));

    private static TagList Tags(Survey survey)
    {
        return
        [
            new("survey.code", TagFor(survey)),
            new("survey.identity", survey.ResponseIdentity.ToString())
        ];
    }
}

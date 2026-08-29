using System.Diagnostics;
using System.Diagnostics.Metrics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class WhatYouSayTelemetry
{
    public const string ServiceName = "WhatYouSay";

    public const string MeterName = ServiceName;

    /// <summary>
    /// Stands in for the code of an anonymous survey. Spans and metrics carry timestamps,
    /// so tagging one with the code would turn the trace store into the per-survey
    /// submission log that anonymous mode gives up <see cref="Response.CreatedAt"/> to
    /// avoid. Does not hide a lone anonymous survey's request timing.
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

    private static readonly Counter<long> sSummariesDrafted = sMeter.CreateCounter<long>(
        "whatyousay.summaries.drafted",
        unit: "{summary}",
        description: "Draft summaries accepted after passing grounding validation.");

    /// <summary>How often an agent cited something it could not substantiate.</summary>
    private static readonly Counter<long> sSummariesRejected = sMeter.CreateCounter<long>(
        "whatyousay.summaries.rejected",
        unit: "{summary}",
        description: "Draft summaries rejected by grounding validation, by reason.");

    /// <summary>
    /// The code, unless the survey is anonymous. Never tag with a raw code; use this or
    /// <see cref="ActivityExtensions.SetSurvey"/>.
    /// </summary>
    public static string TagFor(Survey survey) =>
        survey.IsAnonymous 
            ? RedactedSurvey 
            : survey.Code;

    public static void ResponseSubmitted(Survey survey) =>
        sResponsesSubmitted.Add(1, Tags(survey));

    public static void ResponseEdited(Survey survey) =>
        sResponsesEdited.Add(1, Tags(survey));

    public static void ResponseWithdrawn(Survey survey) =>
        sResponsesWithdrawn.Add(1, Tags(survey));

    public static void SummaryViewed(Survey survey) =>
        sSummariesViewed.Add(1, Tags(survey));

    public static void SummaryDrafted(Survey survey) =>
        sSummariesDrafted.Add(1, Tags(survey));

    public static void SummaryRejected(Survey survey, string reason)
    {
        var tags = Tags(survey);
        tags.Add("rejection.reason", reason);

        sSummariesRejected.Add(1, tags);
    }

    private static TagList Tags(Survey survey)
    {
        return
        [
            new("survey.code", TagFor(survey)),
            new("survey.identity", survey.ResponseIdentity.ToString())
        ];
    }
}

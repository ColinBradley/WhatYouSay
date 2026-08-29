using System.Diagnostics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class ActivityExtensions
{
    /// <summary>
    /// The only way a survey should reach a span. Goes through
    /// <see cref="WhatYouSayTelemetry.TagFor"/>, so the anonymity rule is applied once
    /// rather than remembered at every call site.
    /// </summary>
    public static Activity? SetSurvey(this Activity? activity, Survey survey)
    {
        activity?.SetTag("survey.code", WhatYouSayTelemetry.TagFor(survey));
        activity?.SetTag("survey.identity", survey.ResponseIdentity.ToString());

        return activity;
    }

    public static Activity? RecordFailure(this Activity? activity, string errorType)
    {
        activity?.SetTag("error.type", errorType);
        activity?.SetStatus(ActivityStatusCode.Error);

        return activity;
    }
}

using System.Diagnostics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class ActivityExtensions
{
    /// <summary>
    /// The only way a survey should reach a span; applies the anonymity rule via
    /// <see cref="WhatYouSayTelemetry.TagFor"/>.
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

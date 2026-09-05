using System.Diagnostics;
using WhatYouSay.Data;

namespace WhatYouSay.Telemetry;

public static class ActivityExtensions
{
    /// <summary>
    /// The only way a topic should reach a span; applies the anonymity rule via
    /// <see cref="WhatYouSayTelemetry.TagFor"/>.
    /// </summary>
    public static Activity? SetTopic(this Activity? activity, Topic topic)
    {
        activity?.SetTag("topic.code", WhatYouSayTelemetry.TagFor(topic));
        activity?.SetTag("topic.identity", topic.ResponseIdentity.ToString());

        return activity;
    }

    public static Activity? RecordFailure(this Activity? activity, string errorType)
    {
        activity?.SetTag("error.type", errorType);
        activity?.SetStatus(ActivityStatusCode.Error);

        return activity;
    }
}

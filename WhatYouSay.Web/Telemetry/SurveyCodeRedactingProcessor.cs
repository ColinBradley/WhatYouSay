using System.Diagnostics;
using OpenTelemetry;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Web.Telemetry;

/// <summary>
/// Applies <see cref="SurveyPathRedaction"/> to the URL attributes that ASP.NET Core
/// instrumentation records. Our own spans attach the survey code deliberately, via
/// <see cref="WhatYouSayTelemetry.TagFor"/>, where the anonymity rule is applied.
/// </summary>
public class SurveyCodeRedactingProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.Kind != ActivityKind.Server)
        {
            return;
        }

        if (activity.GetTagItem("url.path") is not string path
            || SurveyPathRedaction.Redact(path) is not { } redacted)
        {
            return;
        }

        activity.SetTag("url.path", redacted);

        // Carries the code as well, and is redundant with url.path on a server span.
        activity.SetTag("url.full", null);
    }
}

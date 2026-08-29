using System.Diagnostics;

namespace WhatYouSay.Web.Telemetry;

public static class WebTelemetry
{
    public const string ServiceName = "WhatYouSay.Web";

    /// <summary>One source per assembly, named after it.</summary>
    public static ActivitySource Source { get; } = new(ServiceName);
}

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WhatYouSay.Tests;

/// <summary>
/// Every test opens an activity from <see cref="Source"/>, so spans from the code under
/// test nest underneath it and a run reads as one trace per test.
/// </summary>
[TestClass]
public static class TestTelemetry
{
    public const string ServiceName = "WhatYouSay.Tests";

    /// <summary>One source per assembly, named after it.</summary>
    public static ActivitySource Source { get; } = new(ServiceName);

    private static TracerProvider? sProvider;

    [AssemblyInitialize]
    public static void Initialise(TestContext context)
    {
        sProvider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .AddSource("*")
            .AddOtlpExporter(options => options.TimeoutMilliseconds = 2000)
            .Build();
    }

    /// <summary>
    /// A batch processor holds spans until flushed, and the test host raises no
    /// ProcessExit, so without this the whole run is dropped silently.
    /// </summary>
    [AssemblyCleanup]
    public static void Shutdown()
    {
        sProvider?.ForceFlush(timeoutMilliseconds: 5000);
        sProvider?.Dispose();
    }
}

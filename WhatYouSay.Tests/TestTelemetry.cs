using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WhatYouSay.Tests;

/// <summary>
/// Every test opens an activity from <see cref="Source"/>, so the spans the code under test
/// emits nest underneath it and a run shows up as one trace per test.
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
    /// The reason this is worth having a runner-level hook for: a batch processor holds
    /// spans until it is flushed, and nothing else in the process will flush it before the
    /// host exits.
    /// </summary>
    [AssemblyCleanup]
    public static void Shutdown()
    {
        sProvider?.ForceFlush(timeoutMilliseconds: 5000);
        sProvider?.Dispose();
    }
}

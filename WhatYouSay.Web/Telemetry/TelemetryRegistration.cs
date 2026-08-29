using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Web.Telemetry;

public static class TelemetryRegistration
{
    public static IServiceCollection AddWhatYouSayTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: WebTelemetry.ServiceName,
                serviceVersion: typeof(TelemetryRegistration).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing => tracing
                // Everything, so a new ActivitySource needs declaring and nothing else.
                .AddSource("*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Recorded SQL is the parameterised text, so response bodies and author
                // names never reach the trace store through query spans.
                .AddEntityFrameworkCoreInstrumentation()
                .AddProcessor<SurveyCodeRedactingProcessor>()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(WhatYouSayTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}

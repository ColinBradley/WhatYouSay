using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Tests;

[TestClass]
public class TelemetryRedactionTests
{
    [TestMethod]
    public void Anonymous_surveys_are_never_tagged_with_their_code()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = Survey(ResponseIdentity.Anonymous, "allco26");

        // Spans and metrics carry timestamps by construction, so tagging with the code
        // would rebuild the per-survey submission log that anonymous mode gives up.
        Assert.AreEqual(WhatYouSayTelemetry.RedactedSurvey, WhatYouSayTelemetry.TagFor(survey));
        Assert.IsFalse(WhatYouSayTelemetry.TagFor(survey).Contains("allco26", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(ResponseIdentity.Required)]
    [DataRow(ResponseIdentity.Optional)]
    public void Identified_surveys_keep_their_code(ResponseIdentity identity)
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = Survey(identity, "spr47ab");

        Assert.AreEqual("spr47ab", WhatYouSayTelemetry.TagFor(survey));
    }

    [TestMethod]
    [DataRow("/surveys/allco26", "/surveys/{code}")]
    [DataRow("/surveys/allco26/summary", "/surveys/{code}/summary")]
    [DataRow("/surveys/allco26/admin/responses", "/surveys/{code}/admin/responses")]
    public void The_survey_code_is_stripped_from_recorded_request_paths(string path, string expected)
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.AreEqual(expected, SurveyPathRedaction.Redact(path));
    }

    [TestMethod]
    [DataRow("/api/surveys/allco26/ai-summary-start", "/api/surveys/{code}/ai-summary-start")]
    [DataRow("/api/surveys/allco26/responses", "/api/surveys/{code}/responses")]
    [DataRow("/api/surveys/allco26", "/api/surveys/{code}")]
    public void The_survey_code_is_stripped_from_api_paths_too(string path, string expected)
    {
        using var activity = TestTelemetry.Source.Start();

        // The summariser token travels in a header, so the path itself carries only the
        // code; redacting it keeps anonymous surveys unidentifiable in traces.
        Assert.AreEqual(expected, SurveyPathRedaction.Redact(path));
    }

    [TestMethod]
    [DataRow("/")]
    [DataRow("/new")]
    [DataRow("/surveys")]
    [DataRow("/surveys/")]
    [DataRow("/api/surveys")]
    [DataRow("/api/surveys/")]
    public void Paths_carrying_no_code_are_left_alone(string path)
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.IsNull(SurveyPathRedaction.Redact(path));
    }

    private static Survey Survey(ResponseIdentity identity, string code) =>
        new()
    {
        Id = Guid.CreateVersion7(),
        Code = code,
        Title = "Test",
        Description = "A prompt",
        AdminPasswordHash = "hash",
        SummariserTokenHash = "hash",
        ResponseIdentity = identity,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}

using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Tests;

[TestClass]
public class TelemetryRedactionTests
{
    [TestMethod]
    public void Anonymous_topics_are_never_tagged_with_their_code()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = Topic(ResponseIdentity.Anonymous, "allco26");

        // Spans and metrics carry timestamps by construction, so tagging with the code
        // would rebuild the per-topic submission log that anonymous mode gives up.
        Assert.AreEqual(WhatYouSayTelemetry.RedactedTopic, WhatYouSayTelemetry.TagFor(topic));
        Assert.IsFalse(WhatYouSayTelemetry.TagFor(topic).Contains("allco26", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(ResponseIdentity.Required)]
    [DataRow(ResponseIdentity.Optional)]
    public void Identified_topics_keep_their_code(ResponseIdentity identity)
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = Topic(identity, "spr47ab");

        Assert.AreEqual("spr47ab", WhatYouSayTelemetry.TagFor(topic));
    }

    [TestMethod]
    [DataRow("/topics/allco26", "/topics/{code}")]
    [DataRow("/topics/allco26/summary", "/topics/{code}/summary")]
    [DataRow("/topics/allco26/admin/responses", "/topics/{code}/admin/responses")]
    public void The_topic_code_is_stripped_from_recorded_request_paths(string path, string expected)
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.AreEqual(expected, TopicPathRedaction.Redact(path));
    }

    [TestMethod]
    [DataRow("/api/topics/allco26/ai-summary-start", "/api/topics/{code}/ai-summary-start")]
    [DataRow("/api/topics/allco26/responses", "/api/topics/{code}/responses")]
    [DataRow("/api/topics/allco26", "/api/topics/{code}")]
    public void The_topic_code_is_stripped_from_api_paths_too(string path, string expected)
    {
        using var activity = TestTelemetry.Source.Start();

        // The summariser token travels in a header, so the path itself carries only the
        // code; redacting it keeps anonymous topics unidentifiable in traces.
        Assert.AreEqual(expected, TopicPathRedaction.Redact(path));
    }

    [TestMethod]
    [DataRow("/")]
    [DataRow("/new")]
    [DataRow("/topics")]
    [DataRow("/topics/")]
    [DataRow("/api/topics")]
    [DataRow("/api/topics/")]
    public void Paths_carrying_no_code_are_left_alone(string path)
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.IsNull(TopicPathRedaction.Redact(path));
    }

    private static Topic Topic(ResponseIdentity identity, string code) =>
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

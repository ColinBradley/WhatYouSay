using System.Diagnostics;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class ActivitySourceExtensionsTests
{
    [TestMethod]
    public void An_unnamed_activity_takes_the_name_of_the_calling_member()
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.IsNotNull(activity);
        Assert.AreEqual(
            $"{nameof(ActivitySourceExtensionsTests)}.{nameof(An_unnamed_activity_takes_the_name_of_the_calling_member)}",
            activity.DisplayName);
    }

    [TestMethod]
    public void An_explicit_name_wins()
    {
        using var activity = TestTelemetry.Source.Start();

        using var named = TestTelemetry.Source.Start("summary.generate");

        Assert.IsNotNull(named);
        Assert.AreEqual("summary.generate", named.DisplayName);
    }

    [TestMethod]
    public void Code_attributes_are_recorded()
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.IsNotNull(activity);
        Assert.AreEqual(
            "WhatYouSay.Tests.ActivitySourceExtensionsTests.Code_attributes_are_recorded",
            activity.GetTagItem("code.function.name"));
        Assert.AreEqual(
            "WhatYouSay.Tests/ActivitySourceExtensionsTests.cs",
            activity.GetTagItem("code.file.path") as string);
        Assert.IsGreaterThan(0, Convert.ToInt32(activity.GetTagItem("code.line.number")));
    }

    [TestMethod]
    public void Source_paths_are_relative_to_the_repository_root()
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.AreEqual(
            "WhatYouSay/Services/ResponseService.cs",
            ActivitySourceExtensions.SourcePath(@"C:\Users\x\Code\WhatYouSay\WhatYouSay\Services\ResponseService.cs"));

        // Falls back to the bare file name rather than leaking a build machine path.
        Assert.AreEqual(
            "Elsewhere.cs",
            ActivitySourceExtensions.SourcePath(@"D:\some\other\place\Elsewhere.cs"));
    }

    [TestMethod]
    public void Setting_a_survey_applies_the_anonymity_rule()
    {
        using var activity = TestTelemetry.Source.Start();

        using var anonymous = TestTelemetry.Source.Start().SetSurvey(Survey(ResponseIdentity.Anonymous, "allco26"));
        using var named = TestTelemetry.Source.Start().SetSurvey(Survey(ResponseIdentity.Required, "spr47ab"));

        Assert.AreEqual(WhatYouSayTelemetry.RedactedSurvey, anonymous!.GetTagItem("survey.code"));
        Assert.AreEqual("spr47ab", named!.GetTagItem("survey.code"));
    }

    [TestMethod]
    public void Recording_a_failure_marks_the_activity_as_an_error()
    {
        using var activity = TestTelemetry.Source.Start();

        using var failed = TestTelemetry.Source.Start().RecordFailure("survey_closed");

        Assert.IsNotNull(failed);
        Assert.AreEqual(ActivityStatusCode.Error, failed.Status);
        Assert.AreEqual("survey_closed", failed.GetTagItem("error.type"));
    }

    private static Survey Survey(ResponseIdentity identity, string code)
    {
        return new Survey()
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
}

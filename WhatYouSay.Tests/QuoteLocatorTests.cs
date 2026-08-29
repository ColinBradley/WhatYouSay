using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class QuoteLocatorTests
{
    private const string Body = "CI is slow. A full run is 22 minutes and it fails often. That is the problem.";

    [TestMethod]
    public void Locate_finds_the_offsets_of_a_quote()
    {
        using var activity = TestTelemetry.Source.Start();

        var location = QuoteLocator.Locate(Body, "22 minutes");

        Assert.IsNotNull(location);
        Assert.AreEqual("22 minutes", Body[location.Value.StartIndex..location.Value.EndIndex]);
    }

    [TestMethod]
    public void Locate_returns_null_for_text_that_is_not_there()
    {
        using var activity = TestTelemetry.Source.Start();

        Assert.IsNull(QuoteLocator.Locate(Body, "45 minutes"));
        Assert.IsNull(QuoteLocator.Locate(Body, string.Empty));
    }

    [TestMethod]
    public void Matches_accepts_offsets_that_still_select_the_quote()
    {
        using var activity = TestTelemetry.Source.Start();

        var location = QuoteLocator.Locate(Body, "22 minutes")!.Value;

        Assert.IsTrue(QuoteLocator.Matches(Body, "22 minutes", location.StartIndex, location.EndIndex));
    }

    [TestMethod]
    public void Matches_rejects_offsets_that_have_drifted()
    {
        using var activity = TestTelemetry.Source.Start();

        var location = QuoteLocator.Locate(Body, "22 minutes")!.Value;

        Assert.IsFalse(QuoteLocator.Matches(Body, "22 minutes", location.StartIndex + 1, location.EndIndex + 1));
        Assert.IsFalse(QuoteLocator.Matches(Body, "22 minutes", location.StartIndex, location.EndIndex - 1));
    }

    [TestMethod]
    [DataRow(-1, 5)]
    [DataRow(0, 10_000)]
    public void Matches_rejects_offsets_outside_the_body_rather_than_throwing(int start, int end)
    {
        using var activity = TestTelemetry.Source.Start();

        // The rendering path slices the body on the strength of this check, so it has to
        // survive nonsense rather than throw an IndexOutOfRange at the user.
        Assert.IsFalse(QuoteLocator.Matches(Body, "22 minutes", start, end));
    }
}

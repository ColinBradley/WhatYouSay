using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class QuoteMismatchTests
{
    private const string Body = "CI is slow. A full run isn't 22 minutes - it's worse.\r\nOn-call was brutal.";

    [TestMethod]
    public void A_curly_apostrophe_is_named_rather_than_only_reported_as_missing()
    {
        using var activity = TestTelemetry.Source.Start();

        // The single most common rejection, and invisible in a diff, so the character has
        // to be named or an agent retries with the same quote.
        var mismatch = QuoteLocator.Diagnose(Body, "A full run isn’t 22 minutes");

        Assert.AreEqual("A full run isn't 22 minutes", mismatch.Nearest);
        Assert.IsTrue(mismatch.Detail.Contains("U+2019", StringComparison.Ordinal), mismatch.Detail);
        Assert.IsTrue(mismatch.Detail.Contains("U+0027", StringComparison.Ordinal), mismatch.Detail);
    }

    [TestMethod]
    public void An_em_dash_swapped_for_a_hyphen_is_named()
    {
        using var activity = TestTelemetry.Source.Start();

        var mismatch = QuoteLocator.Diagnose(Body, "22 minutes — it's worse.");

        Assert.AreEqual("22 minutes - it's worse.", mismatch.Nearest);
        Assert.IsTrue(mismatch.Detail.Contains("U+2014", StringComparison.Ordinal), mismatch.Detail);
    }

    [TestMethod]
    public void Text_run_together_across_a_line_break_comes_back_with_the_break_intact()
    {
        using var activity = TestTelemetry.Source.Start();

        var mismatch = QuoteLocator.Diagnose(Body, "it's worse. On-call was brutal.");

        // Nearest is exact response text, so pasting it back is a valid quote.
        Assert.AreEqual("it's worse.\r\nOn-call was brutal.", mismatch.Nearest);
        Assert.IsNotNull(QuoteLocator.Locate(Body, mismatch.Nearest!));
    }

    [TestMethod]
    public void A_quote_that_diverges_partway_still_anchors_on_what_matched()
    {
        using var activity = TestTelemetry.Source.Start();

        var mismatch = QuoteLocator.Diagnose(Body, "A full run isn't 45 minutes");

        Assert.IsNotNull(mismatch.Nearest);
        Assert.IsTrue(mismatch.Nearest!.StartsWith("A full run isn't", StringComparison.Ordinal));
        Assert.IsTrue(mismatch.Detail.Contains("character", StringComparison.Ordinal), mismatch.Detail);
    }

    [TestMethod]
    public void Wholly_invented_text_reports_no_near_match_rather_than_guessing()
    {
        using var activity = TestTelemetry.Source.Start();

        var mismatch = QuoteLocator.Diagnose(Body, "the deployment pipeline needs replacing");

        Assert.IsNull(mismatch.Nearest);
        Assert.IsTrue(mismatch.Detail.Contains("resembles", StringComparison.Ordinal), mismatch.Detail);
    }

    [TestMethod]
    public void Case_alone_is_reported_against_the_real_text()
    {
        using var activity = TestTelemetry.Source.Start();

        var mismatch = QuoteLocator.Diagnose(Body, "ci is slow.");

        Assert.AreEqual("CI is slow.", mismatch.Nearest);
    }
}

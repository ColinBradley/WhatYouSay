using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// Matching forgives presentation and refuses substance. The line between the two is the
/// point: a curly apostrophe is the same quote, a dropped word is a different claim.
/// </summary>
[TestClass]
public class QuoteMatchingTests
{
    private const string Body =
        "CI is slow — a full run is 22 minutes, and it fails on flaky tests.\n"
        + "People have started batching commits to dodge the wait.";

    [TestMethod]
    public void An_exact_quote_still_matches_exactly()
    {
        var location = QuoteLocator.Locate(Body, "22 minutes");

        Assert.IsNotNull(location);
        Assert.AreEqual("22 minutes", Body[location.Value.StartIndex..location.Value.EndIndex]);
    }

    [TestMethod]
    [DataRow("CI is slow - a full run", "an em dash typed as a hyphen")]
    [DataRow("CI is slow — a  full run", "a doubled space")]
    [DataRow("ci is slow — a full run", "different casing")]
    [DataRow("flaky tests.\nPeople have started", "a line break where the body has one")]
    public void Presentation_is_forgiven(string quote, string why)
    {
        Assert.IsNotNull(QuoteLocator.Locate(Body, quote), why);
    }

    [TestMethod]
    [DataRow("a full run is 22 minutes and it fails", "a dropped comma changes the sentence")]
    [DataRow("a full run is 22 minuets", "a typo is a different word")]
    [DataRow("People have stopped batching commits", "the opposite claim")]
    public void Substance_is_not(string quote, string why)
    {
        Assert.IsNull(QuoteLocator.Locate(Body, quote), why);
    }

    /// <summary>
    /// The span always indexes the original, so what a caller stores is the response's own
    /// characters. That is what keeps a forgiving match from quietly rewriting somebody.
    /// </summary>
    [TestMethod]
    public void A_forgiven_quote_resolves_to_the_bodys_own_characters()
    {
        var location = QuoteLocator.Locate(Body, "ci is slow - a full run")!.Value;
        var stored = Body[location.StartIndex..location.EndIndex];

        Assert.AreEqual("CI is slow — a full run", stored);
        Assert.IsTrue(QuoteLocator.Matches(Body, stored, location.StartIndex, location.EndIndex));
    }
}

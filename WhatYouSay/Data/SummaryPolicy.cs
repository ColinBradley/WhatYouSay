namespace WhatYouSay.Data;

/// <summary>Whether the summariser's work reaches readers straight away or waits for the admin.</summary>
public enum SummaryPolicy
{
    /// <summary>The summariser writes drafts, and the admin publishes them.</summary>
    Reviewed,

    /// <summary>
    /// The summariser keeps one live summary up to date.
    /// Each accepted write to it is published straight away.
    /// </summary>
    Live,
}

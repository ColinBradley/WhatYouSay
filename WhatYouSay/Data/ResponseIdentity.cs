namespace WhatYouSay.Data;

/// <summary>
/// How much identity a survey asks of its responders. Set at creation and immutable
/// thereafter: switching to <see cref="Anonymous"/> cannot retroactively unrecord
/// timestamps or unsay names, and switching away from it changes the deal earlier
/// responders agreed to.
/// </summary>
public enum ResponseIdentity
{
    /// <summary>Author name required. Timestamps recorded.</summary>
    Required,

    /// <summary>Author name offered but skippable. Timestamps recorded.</summary>
    Optional,

    /// <summary>No author field at all, and no timestamps recorded anywhere.</summary>
    Anonymous
}

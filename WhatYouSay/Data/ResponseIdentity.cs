namespace WhatYouSay.Data;

/// <summary>
/// How much identity a topic asks of its responders. Fixed once anyone has responded,
/// because switching to <see cref="Anonymous"/> cannot unsay names, and switching away
/// from it changes the deal earlier responders agreed to.
/// Whether times are recorded is a separate setting, <see cref="ResponseTiming"/>.
/// </summary>
public enum ResponseIdentity
{
    /// <summary>Author name required.</summary>
    Required,

    /// <summary>Author name offered but skippable.</summary>
    Optional,

    /// <summary>No author field at all, and no name stored anywhere.</summary>
    Anonymous,
}

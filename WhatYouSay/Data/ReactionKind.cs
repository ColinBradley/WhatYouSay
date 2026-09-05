namespace WhatYouSay.Data;

/// <summary>
/// A chat reaction bar. Light by design, and not an analytics channel: someone hits
/// <see cref="Disagree"/> to flag a thing worth raising, not to file a dissent. Anything
/// that needs saying goes in a <see cref="NodeComment"/>.
/// </summary>
public enum ReactionKind
{
    Agree,

    Disagree,

    Important,

    Question,

    Celebrate,

    Laugh,
}

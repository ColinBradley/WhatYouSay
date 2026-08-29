namespace WhatYouSay.Data;

public enum ReactionKind
{
    /// <summary>"Yes, that matches what I meant."</summary>
    Agree,

    /// <summary>"This one matters to me" — weight rather than agreement.</summary>
    Important,

    /// <summary>"This does not represent what I said." The fidelity loop closing.</summary>
    Misrepresents
}

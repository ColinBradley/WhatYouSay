namespace WhatYouSay.Data;

/// <summary>
/// Whether a topic records when anything in it was submitted.
/// Independent of <see cref="ResponseIdentity"/>, so an anonymous topic may record times.
/// Fixed once anyone has responded, for the same reasons identity is.
/// </summary>
public enum ResponseTiming
{
    /// <summary>
    /// Responses, edits, comments and reactions carry the time they were submitted.
    /// Responses are listed newest first.
    /// </summary>
    Recorded,

    /// <summary>
    /// No time is stored anywhere in the topic. Responses are listed by their random id,
    /// which reveals nothing about the order they arrived in.
    /// </summary>
    NotRecorded,
}

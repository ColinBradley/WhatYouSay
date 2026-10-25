using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// One set of a reaction bar's toggle buttons: either the kinds in use or the rest.
/// A bar renders the two sets in different places, so the reactions in use stay visible
/// while the others wait to be asked for.
/// </summary>
public partial class ReactionButtons
{
    /// <summary>Every kind, in display order.</summary>
    public static readonly ImmutableArray<ReactionKind> Kinds =
        [.. Enum.GetValues<ReactionKind>()];

    [Parameter]
    [EditorRequired]
    public ReactionTally Tally { get; set; } = ReactionTally.None;

    /// <summary>
    /// True renders the kinds that someone has used or that the viewer has chosen.
    /// False renders the rest.
    /// </summary>
    [Parameter]
    public bool Lit { get; set; }

    [Parameter]
    [EditorRequired]
    public EventCallback<ReactionKind> OnToggle { get; set; }

    /// <summary>Extra classes for every button in the set.</summary>
    [Parameter]
    public string? Class { get; set; }

    internal static string Emoji(ReactionKind kind) =>
        kind switch
        {
            ReactionKind.Agree => "\U0001F44D",
            ReactionKind.Disagree => "\U0001F44E",
            ReactionKind.Important => "❗",
            ReactionKind.Question => "❓",
            ReactionKind.Celebrate => "\U0001F389",
            ReactionKind.Laugh => "\U0001F604",
            _ => "?",
        };

    internal static string Label(ReactionKind kind) =>
        kind switch
        {
            ReactionKind.Agree => "Agree",
            ReactionKind.Disagree => "Disagree",
            ReactionKind.Important => "Important",
            ReactionKind.Question => "Not sure about this",
            ReactionKind.Celebrate => "Worth celebrating",
            ReactionKind.Laugh => "Made me laugh",
            _ => kind.ToString(),
        };

    /// <summary>
    /// The label with its count, read in place of the emoji.
    /// A screen reader would otherwise announce the emoji's own name, such as "thumbs up".
    /// </summary>
    internal static string AccessibleName(ReactionKind kind, int count) =>
        count > 0 ? $"{Label(kind)}, {count}" : Label(kind);
}

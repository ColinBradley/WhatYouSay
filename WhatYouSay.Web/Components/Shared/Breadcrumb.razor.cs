using Microsoft.AspNetCore.Components;

namespace WhatYouSay.Web.Components.Shared;

public record Crumb
{
    public required string Text { get; init; }

    /// <summary>Null renders as plain text rather than a link.</summary>
    public string? Href { get; init; }
}

public partial class Breadcrumb
{
    /// <summary>Where <see cref="MainLayout"/> renders whatever trail the page declared.</summary>
    public static readonly object HeaderSection = new();

    [Parameter]
    public IReadOnlyList<Crumb> Items { get; set; } = [];

    public static Crumb Home()
    {
        return new Crumb { Text = "Home", Href = "/" };
    }

    public static Crumb Topic(string code, string title)
    {
        return new Crumb { Text = title, Href = $"/topics/{code}" };
    }
}

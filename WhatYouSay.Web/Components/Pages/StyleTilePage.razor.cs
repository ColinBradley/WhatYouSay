using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class StyleTilePage
{
    private static readonly IReadOnlyList<Crumb> mCrumbs =
    [
        Breadcrumb.Home(),
        new Crumb { Text = "Style" },
    ];

    private static readonly IReadOnlyList<Swatch> Grounds =
    [
        new Swatch { Name = "Page", Token = "--page-bg" },
        new Swatch { Name = "Panel", Token = "--panel-bg" },
        new Swatch { Name = "Sunken", Token = "--sunken-bg" },
        new Swatch { Name = "Text", Token = "--text" },
        new Swatch { Name = "Quiet", Token = "--text-quiet" },
        new Swatch { Name = "Line", Token = "--line" },
        new Swatch { Name = "Accent", Token = "--accent" },
    ];

    private sealed record Swatch
    {
        public required string Name { get; init; }

        public required string Token { get; init; }
    }
}

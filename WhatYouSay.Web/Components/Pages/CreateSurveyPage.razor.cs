using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class CreateSurveyPage
{
    /// <summary>Placeholder copy for the title and question boxes, shown as a matched pair.</summary>
    private sealed record Example
    {
        public required string Title { get; init; }

        public required string Prompt { get; init; }
    }

    // A visitor sees one of these, so the spread across work, social and single-author uses is
    // what stops the app reading as a tool for one of them. Keep it varied when adding.
    private static readonly ImmutableArray<Example> sExamples =
    [
        new Example()
        {
            Title = "Sprint 48 retro",
            Prompt = "What went well, what didn't, and what would you change? Be as blunt as you like.",
        },
        new Example()
        {
            Title = "Where are we eating on Friday?",
            Prompt = "Somewhere new, somewhere familiar, or somewhere with food you can actually eat. Say what you'd rather avoid too.",
        },
        new Example()
        {
            Title = "The new deploy process, one month on",
            Prompt = "What is better than before, what is worse, and what would you rip out entirely?",
        },
        new Example()
        {
            Title = "Summer trip: what are we actually doing?",
            Prompt = "Dates that work, dates that don't, and one thing you'd like to do while we're there.",
        },
        new Example()
        {
            Title = "How is the reading group going?",
            Prompt = "What is working, what isn't, and what should we read next?",
        },
        new Example()
        {
            Title = "Week 12",
            Prompt = "What happened, what I learned, and what is still bothering me.",
        },
    ];

    // Field initialiser rather than OnInitialized: nothing here depends on parameters, and the
    // page is static SSR, so one instance is one page load is one example.
    private readonly Example mExample = sExamples[Random.Shared.Next(sExamples.Length)];

    private CreatedSurvey? mCreated;

    private string? mShareLink;

    private string? mError;

    private readonly IReadOnlyList<Crumb> mCrumbs =
        [Breadcrumb.Home(), new Crumb { Text = "New survey" }];

    [Inject]
    private SurveyAdminService Admin { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromForm]
    public string? Title { get; set; }

    [SupplyParameterFromForm]
    public string? Prompt { get; set; }

    [SupplyParameterFromForm]
    public string? Password { get; set; }

    [SuppressMessage("Usage", "BL0008", Justification = "Absent form field must bind to default.")]
    [SupplyParameterFromForm]
    public ResponseIdentity Identity { get; set; } = ResponseIdentity.Required;

    [SuppressMessage("Usage", "BL0008", Justification = "Unchecked checkbox must bind to false.")]
    [SupplyParameterFromForm]
    public bool IsPubliclyListed { get; set; } = true;

    [SupplyParameterFromForm]
    public bool AreResponsesPublic { get; set; }

    private static string Explain(ResponseIdentity identity)
    {
        return identity switch
        {
            ResponseIdentity.Required => "everyone puts their name to what they write",
            ResponseIdentity.Optional => "a name is offered but can be left blank",
            _ => "no name and no timestamp is recorded at all",
        };
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(this.Title)
            || string.IsNullOrWhiteSpace(this.Prompt)
            || string.IsNullOrWhiteSpace(this.Password))
        {
            mError = "A title, a question and an admin password are all needed.";

            return;
        }

        mError = null;

        mCreated = await this.Admin.CreateAsync(
            this.Title,
            this.Prompt,
            this.Password,
            this.Identity,
            this.IsPubliclyListed,
            this.AreResponsesPublic);

        // Straight into admin without asking for the password just set.
        this.Session.Grant(mCreated.Survey.Id);

        mShareLink = this.Navigation
            .ToAbsoluteUri($"/surveys/{mCreated.Survey.Code}")
            .ToString();
    }
}

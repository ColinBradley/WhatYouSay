using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class CreateSurveyPage
{
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

    [SupplyParameterFromForm]
    public ResponseIdentity Identity { get; set; } = ResponseIdentity.Required;

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

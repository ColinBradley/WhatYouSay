using Microsoft.AspNetCore.Components;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Pages;

public partial class Home
{
    private IReadOnlyList<SurveyListing>? mListings;

    private string? mNotFound;

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromForm]
    public string? Code { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mListings = await this.Surveys.ListPubliclyListedAsync();
    }

    private async Task OpenByCodeAsync()
    {
        var code = this.Code?.Trim();

        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        if (await this.Surveys.FindByCodeAsync(code) is null)
        {
            mNotFound = code;

            return;
        }

        this.Navigation.NavigateTo($"/surveys/{code}");
    }
}

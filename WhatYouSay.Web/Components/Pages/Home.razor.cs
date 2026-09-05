using Microsoft.AspNetCore.Components;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Pages;

public partial class Home
{
    private IReadOnlyList<TopicListing>? mListings;

    private string? mNotFound;

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromForm]
    public string? Code { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mListings = await this.Topics.ListPubliclyListedAsync();
    }

    private async Task OpenByCodeAsync()
    {
        var code = this.Code?.Trim();

        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        if (await this.Topics.FindByCodeAsync(code) is null)
        {
            mNotFound = code;

            return;
        }

        this.Navigation.NavigateTo($"/topics/{code}");
    }
}

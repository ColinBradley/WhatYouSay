using Microsoft.AspNetCore.Components;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Pages;

public partial class Home
{
    private IReadOnlyList<TopicListing>? mListings;

    [Inject]
    private TopicService Topics { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        mListings = await this.Topics.ListPubliclyListedAsync();
    }

    /// <summary>Open first, and an empty half is dropped rather than headed.</summary>
    private IEnumerable<TopicGroup> Groups()
    {
        foreach (var accepting in (bool[])[true, false])
        {
            var listings = mListings!
                .Where(l => l.Topic.IsAcceptingResponses == accepting)
                .ToList();

            if (listings.Count > 0)
            {
                yield return new TopicGroup()
                {
                    Heading = accepting ? "Open" : "Closed",
                    Listings = listings,
                };
            }
        }
    }

    private sealed record TopicGroup
    {
        public required string Heading { get; init; }

        public required IReadOnlyList<TopicListing> Listings { get; init; }
    }
}

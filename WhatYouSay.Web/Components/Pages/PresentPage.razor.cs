using Microsoft.AspNetCore.Components;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Presentation;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Components.Pages;

/// <summary>
/// The static shell around <see cref="PresentReader"/>. Builds the join URL, mints the responder
/// token and writes its cookie. The cookie needs an <see cref="HttpContext"/>, which a circuit
/// does not have.
/// </summary>
public partial class PresentPage
{
    private Topic? mTopic;

    private string mReactorToken = string.Empty;

    private string mJoinUrl = string.Empty;

    private bool mShowsFallbackWarning;

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private PublicAddress Address { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        mTopic = await this.Topics.FindByCodeAsync(this.Code);

        if (mTopic is null)
        {
            return;
        }

        using var activity = WebTelemetry.Source.Start().SetTopic(mTopic);

        mJoinUrl = this.Address.Resolve($"topics/{mTopic.Code}/respond", this.Navigation.BaseUri).AbsoluteUri;
        mShowsFallbackWarning = !this.Address.IsConfigured && await this.Session.CanAdministerAsync(mTopic.Id);

        // On arrival, because response cards take reactions here as they do on the topic page.
        mReactorToken = ResponderCookie.Read(this.HttpContext, mTopic.Id) ?? Secrets.NewToken();
        ResponderCookie.Write(this.HttpContext, mTopic.Id, mReactorToken);
    }
}

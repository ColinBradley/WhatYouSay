using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class RespondPage
{
    private Topic? mTopic;

    private Response? mOwnResponse;

    private bool mFrozen;

    private string? mError;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private ResponseService Responses { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    public string? Body { get; set; }

    [SupplyParameterFromForm]
    public string? Author { get; set; }

    [SupplyParameterFromForm]
    public string? Intent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mTopic = await this.Topics.FindByCodeAsync(this.Code);

        if (mTopic is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Topic(mTopic.Code, mTopic.Title),
            new Crumb { Text = "Respond" },
        ];

        var token = ResponderCookie.Read(this.HttpContext, mTopic.Id);

        if (token is not null)
        {
            mOwnResponse = await this.Responses.FindOwnAsync(mTopic.Id, token);
        }

        // Your own answer stops being yours to change when it freezes, which is not the
        // same question as whether the topic is taking new ones.
        mFrozen = !mTopic.IsAcceptingResponses || mOwnResponse?.IsFrozen == true;

        // On a GET, prefill the editor with what is already stored.
        if (mOwnResponse is not null && this.Body is null)
        {
            this.Body = mOwnResponse.Body;
            this.Author = mOwnResponse.Author;
        }
    }

    private async Task SubmitAsync()
    {
        if (mTopic is null || mFrozen)
        {
            return;
        }

        if (this.Intent == "withdraw")
        {
            await this.WithdrawAsync();

            return;
        }

        if (!this.Validate(mTopic))
        {
            return;
        }

        if (mOwnResponse is null)
        {
            var token = await this.Responses.SubmitAsync(mTopic, this.Body!, this.Author);

            // Static SSR, so the response has not started and a cookie can still be written.
            ResponderCookie.Write(this.HttpContext, mTopic.Id, token);
        }
        else
        {
            await this.Responses.EditAsync(mTopic, mOwnResponse, this.Body!, this.Author);
        }

        this.Reload();
    }

    private async Task WithdrawAsync()
    {
        if (mTopic is null || mOwnResponse is null)
        {
            return;
        }

        await this.Responses.WithdrawAsync(mTopic, mOwnResponse);
        ResponderCookie.Clear(this.HttpContext, mTopic.Id);

        this.Reload();
    }

    private void Reload()
    {
        this.Navigation.NavigateTo($"/topics/{this.Code}/respond");
    }

    private bool Validate(Topic topic)
    {
        if (string.IsNullOrWhiteSpace(this.Body))
        {
            mError = "An answer is required.";

            return false;
        }

        if (topic.ResponseIdentity == ResponseIdentity.Required && string.IsNullOrWhiteSpace(this.Author))
        {
            mError = "This topic asks everyone to put their name to what they write.";

            return false;
        }

        mError = null;

        return true;
    }

    private string NameLabel()
    {
        return mTopic!.ResponseIdentity == ResponseIdentity.Optional ? "Your name (optional)" : "Your name";
    }

    private string? FrozenReason()
    {
        if (!mFrozen)
        {
            return null;
        }

        return mOwnResponse?.IsFrozen == true
            ? "This was frozen when the topic closed, so the quotes taken from it stay accurate."
            : "This topic is not taking new answers.";
    }

    private string StorageNotice()
    {
        return mTopic!.ResponseIdentity switch
        {
            ResponseIdentity.Anonymous =>
                "Anonymous: no name and no timestamp is recorded, not even hidden. A cookie in "
                + "this browser is the only link back to your response.",
            ResponseIdentity.Optional =>
                "Your answer, the name you give if any, and the time are stored.",
            _ => "Your answer, your name and the time are stored.",
        };
    }
}

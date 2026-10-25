using Microsoft.AspNetCore.Components;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class RespondPage
{
    private const string FrozenReason =
        "This was frozen when the topic closed, so the quotes taken from it stay accurate.";

    private const string NotYours =
        "That response is not one of yours on this topic, or it has been withdrawn.";

    private Topic? mTopic;

    private string? mToken;

    private IReadOnlyList<Response> mOwn = [];

    /// <summary>The response the form is editing. Null while the form adds a new one.</summary>
    private Response? mEditing;

    /// <summary>Why the form's fields and buttons are disabled, or null when they are not.</summary>
    private string? mLockedReason;

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

    /// <summary>The response a save or withdrawal targets. Absent when adding a new one.</summary>
    [SupplyParameterFromForm]
    public Guid? ResponseId { get; set; }

    [SupplyParameterFromQuery(Name = "saved")]
    public string? Saved { get; set; }

    /// <summary>
    /// The id of the response to open for editing. A string rather than a Guid, so a mangled
    /// link reaches the page and is reported there instead of failing to bind.
    /// </summary>
    [SupplyParameterFromQuery(Name = "edit")]
    public string? Edit { get; set; }

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

        mToken = ResponderCookie.Read(this.HttpContext, mTopic.Id);

        if (mToken is not null)
        {
            mOwn = await this.Responses.ListOwnAsync(mTopic, mToken);
        }

        // A post names its response in a form field, and a link to edit names it in the
        // query string.
        var selected = this.ResponseId ?? (Guid.TryParse(this.Edit, out var id) ? id : null);

        if (selected is { } responseId && mToken is not null)
        {
            mEditing = await this.Responses.FindOwnAsync(mTopic.Id, responseId, mToken);
        }

        mLockedReason = mEditing is not null
            ? (mEditing.IsFrozen ? FrozenReason : null)
            : (mTopic.IsAcceptingResponses ? null : "This topic is not taking new responses.");

        if (!HttpMethods.IsGet(this.HttpContext.Request.Method))
        {
            return;
        }

        if (this.Edit is not null && mEditing is null)
        {
            mError = NotYours;
        }

        // On a GET, prefill the editor with what is stored. A new response starts with the
        // name this browser gave last time, so a named topic does not ask for it again.
        if (mEditing is not null)
        {
            this.Body = mEditing.Current.Body;
            this.Author = mEditing.Author;
        }
        else
        {
            this.Author = mOwn.Select(r => r.Author).FirstOrDefault(author => author is not null);
        }
    }

    private async Task SubmitAsync()
    {
        if (mTopic is null)
        {
            return;
        }

        try
        {
            switch (this.Intent)
            {
                case "save":
                    await this.SaveAsync(mTopic);
                    break;

                case "withdraw":
                    await this.WithdrawAsync(mTopic);
                    break;

                default:
                    await this.AddAsync(mTopic);
                    break;
            }
        }
        catch (InvalidOperationException refused)
        {
            mError = refused.Message;
        }
    }

    private async Task AddAsync(Topic topic)
    {
        if (!this.Validate(topic))
        {
            return;
        }

        // Every response from this browser shares one token, so the cookie keeps ownership
        // of all of them.
        var token = mToken ?? Secrets.NewToken();

        await this.Responses.SubmitAsync(topic, token, this.Body!, this.Author);

        // Static SSR, so the response has not started and a cookie can still be written.
        ResponderCookie.Write(this.HttpContext, topic.Id, token);

        this.Reload("new");
    }

    private async Task SaveAsync(Topic topic)
    {
        if (mEditing is null)
        {
            mError = NotYours;

            return;
        }

        if (!this.Validate(topic))
        {
            return;
        }

        await this.Responses.EditAsync(topic, mEditing, this.Body!, this.Author);

        this.Reload("edited");
    }

    private async Task WithdrawAsync(Topic topic)
    {
        if (mEditing is null)
        {
            mError = NotYours;

            return;
        }

        await this.Responses.WithdrawAsync(topic, mEditing);

        this.Reload("withdrawn");
    }

    /// <summary>
    /// The outcome travels in the query string because the post redirects, which is what stops
    /// a refresh resubmitting it.
    /// </summary>
    private void Reload(string outcome)
    {
        this.Navigation.NavigateTo($"/topics/{this.Code}/respond?saved={outcome}");
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

    private string? SavedMessage()
    {
        return this.Saved switch
        {
            "new" => "Your answer is in. Add another whenever you like, or change one of yours below "
                + "while this topic is open.",
            "edited" => "Saved. This replaces what you wrote before.",
            "withdrawn" => "Withdrawn. That response and any quotes of it are gone from this topic, "
                + "though a summary written earlier may still reflect it. Your other responses are untouched.",
            _ => null,
        };
    }

    private string FormHeading()
    {
        if (mEditing is not null)
        {
            return "Edit your response";
        }

        return mOwn.Count == 0 ? "Your response" : "Add another response";
    }

    private string NameLabel()
    {
        return mTopic!.ResponseIdentity == ResponseIdentity.Optional ? "Your name (optional)" : "Your name";
    }

    /// <summary>
    /// Why one of the responder's own responses cannot be opened for editing, or null when it can.
    /// </summary>
    private string? EditBlockedReason(Response response)
    {
        if (response.Id == mEditing?.Id)
        {
            return "This is the response in the form above.";
        }

        return response.IsFrozen ? FrozenReason : null;
    }

    /// <summary>
    /// Whether to show the storage notice in a box. An anonymous topic that records timing gets
    /// one, because "anonymous" alone suggests the time is not kept either.
    /// </summary>
    private bool BoxesStorageNotice()
    {
        return mTopic!.IsAnonymous && mTopic.RecordsTiming;
    }

    private string StorageNotice()
    {
        var topic = mTopic!;

        var identity = topic.ResponseIdentity switch
        {
            ResponseIdentity.Anonymous => "Anonymous: no name is asked for or stored.",
            ResponseIdentity.Optional => "Your answer and the name you give, if any, are stored.",
            _ => "Your answer and your name are stored.",
        };

        var timing = topic.RecordsTiming
            ? "The time you post, and the time of each edit, is recorded. Wherever responses are "
                + "shown, they are listed newest first, with when each was posted."
            : "No time is recorded, not even hidden.";

        return $"{identity} {timing} A cookie in this browser is the only link back to your responses.";
    }
}

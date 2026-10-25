using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>A topic and the one-time secrets that only exist at creation.</summary>
public record CreatedTopic
{
    public required Topic Topic { get; init; }

    /// <summary>Shown once on the confirmation screen; only its hash is stored.</summary>
    public required string SummariserToken { get; init; }
}

public class TopicAdminService
{
    private readonly WhatYouSayContext mDb;

    public TopicAdminService(WhatYouSayContext db)
    {
        mDb = db;
    }

    public async Task<CreatedTopic> CreateAsync(
        string title,
        string prompt,
        string adminPassword,
        ResponseIdentity identity,
        bool isPubliclyListed,
        bool areResponsesPublic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var token = Secrets.NewToken();

        var topic = new Topic()
        {
            Id = Guid.CreateVersion7(),
            Code = await this.UniqueCodeAsync(cancellationToken),
            Title = title.Trim(),
            Description = prompt.Trim(),
            AdminPasswordHash = Secrets.HashPassword(adminPassword),
            SummariserTokenHash = Secrets.HashToken(token),
            ResponseIdentity = identity,
            IsPubliclyListed = isPubliclyListed,
            AreResponsesPublic = areResponsesPublic,
            IsAcceptingResponses = true,
            SummaryPolicy = SummaryPolicy.Live,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.TopicCreated(topic);

        return new CreatedTopic { Topic = topic, SummariserToken = token };
    }

    public bool CheckPassword(Topic topic, string password)
    {
        return Secrets.VerifyPassword(topic.AdminPasswordHash, password);
    }

    /// <summary>
    /// Opens or closes the topic to new responses.
    /// Closing freezes nothing, so responders can still edit and withdraw what they wrote.
    /// </summary>
    public async Task SetAcceptingResponsesAsync(
        Topic topic,
        bool accepting,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        topic.IsAcceptingResponses = accepting;

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Freezes every response the topic has now, or lets responders edit them all again.
    /// Responses that arrive after a freeze start editable.
    /// </summary>
    public async Task SetResponsesFrozenAsync(
        Topic topic,
        bool frozen,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        // Through the tracker rather than ExecuteUpdate: a caller holding a Response over
        // this call would otherwise still see its old state.
        var changing = await mDb.Responses
            .Where(r => r.TopicId == topic.Id && r.IsFrozen != frozen)
            .ToListAsync(cancellationToken);

        foreach (var response in changing)
        {
            response.IsFrozen = frozen;
        }

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Chooses whether the summariser's writes to the live summary are published straight away.
    /// </summary>
    /// <remarks>
    /// Switching to review ends the live summary.
    /// It stays published as an ordinary version, and it is closed to the summariser, so
    /// nothing the summariser writes reaches readers without the admin.
    /// </remarks>
    public async Task SetSummaryPolicyAsync(
        Topic topic,
        SummaryPolicy policy,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        topic.SummaryPolicy = policy;

        if (policy == SummaryPolicy.Reviewed
            && await this.FindLiveAsync(topic, cancellationToken) is { } live)
        {
            live.IsLive = false;
            live.IsAgentEditable = false;
        }

        await this.SaveSummaryChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Makes a summary the topic's live summary, which publishes it and opens it to the summariser.
    /// The previous live summary stays published as an ordinary version, closed to the summariser.
    /// </summary>
    /// <exception cref="InvalidOperationException">The topic has review switched on.</exception>
    public async Task MakeLiveAsync(
        Topic topic,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        if (topic.SummaryPolicy != SummaryPolicy.Live)
        {
            activity.RecordFailure("summaries_reviewed");

            throw new InvalidOperationException(
                "This topic has review switched on, so it has no live summary. Switch live "
                + "updates on first.");
        }

        var summary = await this.RequireSummaryAsync(topic, summaryId, cancellationToken);

        if (summary.IsLive)
        {
            return;
        }

        var previous = await this.FindLiveAsync(topic, cancellationToken);

        // The previous one is cleared and saved first, because the database allows only one
        // live summary per topic at any moment, even partway through a save.
        await using var transaction = await mDb.Database.BeginTransactionAsync(cancellationToken);

        if (previous is not null)
        {
            previous.IsLive = false;
            previous.IsAgentEditable = false;

            await this.SaveSummaryChangesAsync(cancellationToken);
        }

        summary.IsLive = true;
        summary.IsDraft = false;
        summary.IsPublic = true;
        summary.IsAgentEditable = true;
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await this.SaveSummaryChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            activity.RecordFailure("live_summary_exists");

            throw new InvalidOperationException(
                "The summariser made a live summary while this was being saved. Reload and "
                + "choose again.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Opens a summary to the summariser, or closes it.
    /// Closing the live summary pauses the summariser's updates without ending live mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Opening a published summary that is not the live summary, since readers would then see
    /// the summariser's writes without review.
    /// </exception>
    public async Task SetAgentEditableAsync(
        Topic topic,
        Guid summaryId,
        bool editable,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var summary = await this.RequireSummaryAsync(topic, summaryId, cancellationToken);

        if (editable && !summary.IsDraft && !summary.IsLive)
        {
            activity.RecordFailure("published_not_live");

            throw new InvalidOperationException(
                "A published summary is only open to the summariser while it is the live "
                + "summary. Unpublish it, or make it live, first.");
        }

        summary.IsAgentEditable = editable;

        await this.SaveSummaryChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the topic's identity and timing can still change. Both are fixed once anybody
    /// has responded, because what was collected cannot be unrecorded and the responders
    /// answered under the settings they were shown. A withdrawn response still counts.
    /// </summary>
    public async Task<bool> CanChangeIdentityAndTimingAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        return !await mDb.Responses.AnyAsync(r => r.TopicId == topic.Id, cancellationToken);
    }

    /// <exception cref="InvalidOperationException">
    /// The identity or timing differs from the topic's while <see cref="CanChangeIdentityAndTimingAsync"/>
    /// is false.
    /// </exception>
    public async Task UpdateSettingsAsync(
        Topic topic,
        bool isPubliclyListed,
        bool areResponsesPublic,
        bool anonymous,
        bool recordsTiming,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        topic.IsPubliclyListed = isPubliclyListed;
        topic.AreResponsesPublic = areResponsesPublic;

        var timing = recordsTiming ? ResponseTiming.Recorded : ResponseTiming.NotRecorded;

        if (!await this.CanChangeIdentityAndTimingAsync(topic, cancellationToken))
        {
            if (anonymous != topic.IsAnonymous || timing != topic.ResponseTiming)
            {
                activity.RecordFailure("responses_exist");

                throw new InvalidOperationException(
                    "Somebody has already answered under these settings, so identity and timing are fixed now.");
            }
        }
        else
        {
            topic.ResponseIdentity = anonymous
                ? ResponseIdentity.Anonymous
                : ResponseIdentity.Required;

            topic.ResponseTiming = timing;
        }

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the new token; the old one stops working immediately.</summary>
    public async Task<string> RegenerateSummariserTokenAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var token = Secrets.NewToken();
        topic.SummariserTokenHash = Secrets.HashToken(token);

        await mDb.SaveChangesAsync(cancellationToken);

        return token;
    }

    public async Task DeleteResponseAsync(
        Topic topic,
        Guid responseId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var response = await mDb.Responses.FirstOrDefaultAsync(
            r => r.Id == responseId && r.TopicId == topic.Id,
            cancellationToken);

        if (response is null)
        {
            return;
        }

        response.IsDeleted = true;

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Publishing blesses a summary and shows it in one move: there is no state where a
    /// summary is final but nobody can read it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Unpublishing the live summary, which stays published for as long as it is live.
    /// </exception>
    public async Task SetSummaryVisibilityAsync(
        Topic topic,
        Guid summaryId,
        bool published,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var summary = await this.RequireSummaryAsync(topic, summaryId, cancellationToken);

        if (!published && summary.IsLive)
        {
            activity.RecordFailure("summary_live");

            throw new InvalidOperationException(
                "This is the live summary, so it stays published. Switch on review, or make "
                + "another summary live, before unpublishing it.");
        }

        summary.IsDraft = !published;
        summary.IsPublic = published;

        // Publishing hands the version back to the humans. Re-opening it to the agent is
        // then a deliberate act rather than a bit somebody forgot to flip. The live summary
        // is the exception, because staying open to the agent is what makes it live.
        if (published && !summary.IsLive)
        {
            summary.IsAgentEditable = false;
        }

        summary.UpdatedAt = DateTimeOffset.UtcNow;

        await this.SaveSummaryChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a summary with nothing in it, for writing by hand. Takes no grounding check
    /// because an empty tree has no branches, and no response check because it cites
    /// nothing; the agent is kept out until an admin hands it over.
    /// </summary>
    public async Task<Summary> CreateEmptySummaryAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var now = DateTimeOffset.UtcNow;
        var summary = new Summary()
        {
            Id = Guid.CreateVersion7(),
            TopicId = topic.Id,
            Body = string.Empty,
            CreatedBy = "human",
            IsAgentEditable = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        mDb.Summaries.Add(summary);

        await mDb.SaveChangesAsync(cancellationToken);

        return summary;
    }

    public async Task DeleteSummaryAsync(
        Topic topic,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var summary = await this.RequireSummaryAsync(topic, summaryId, cancellationToken);

        mDb.Summaries.Remove(summary);

        await this.SaveSummaryChangesAsync(cancellationToken);
    }

    private async Task<Summary?> FindLiveAsync(Topic topic, CancellationToken cancellationToken)
    {
        return await mDb.Summaries.FirstOrDefaultAsync(
            s => s.TopicId == topic.Id && s.IsLive,
            cancellationToken);
    }

    /// <summary>
    /// Saves changes to summaries. A summary's revision is a concurrency token, so a save
    /// fails when the summariser or an editor wrote to the summary after it was loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">The summary changed after it was loaded.</exception>
    private async Task SaveSummaryChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await mDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Activity.Current.RecordFailure("stale_revision");

            throw new InvalidOperationException(
                "The summary changed while this was being saved, so nothing was saved. Try again.");
        }
    }

    private async Task<Summary> RequireSummaryAsync(
        Topic topic,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        return await mDb.Summaries.FirstOrDefaultAsync(
                s => s.Id == summaryId && s.TopicId == topic.Id,
                cancellationToken)
            ?? throw new InvalidOperationException($"No summary {summaryId} on this topic.");
    }

    private async Task<string> UniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Secrets.NewTopicCode();

            if (!await mDb.Topics.AnyAsync(s => s.Code == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not find a free topic code.");
    }
}

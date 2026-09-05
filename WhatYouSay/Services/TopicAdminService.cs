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

public class TopicAdminService(WhatYouSayContext db)
{
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
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Topics.Add(topic);
        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.TopicCreated(topic);

        return new CreatedTopic { Topic = topic, SummariserToken = token };
    }

    public bool CheckPassword(Topic topic, string password)
    {
        return Secrets.VerifyPassword(topic.AdminPasswordHash, password);
    }

    public async Task SetAcceptingResponsesAsync(
        Topic topic,
        bool accepting,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        topic.IsAcceptingResponses = accepting;

        // Closing is what freezes, and the freeze never lifts. Reopening therefore costs
        // nothing: what was collected stays frozen, new answers arrive editable, and the
        // next close freezes those.
        if (!accepting)
        {
            // Through the tracker rather than ExecuteUpdate: a caller holding a Response
            // over this call would otherwise still see it as editable.
            var thawed = await db.Responses
                .Where(r => r.TopicId == topic.Id && !r.IsFrozen)
                .ToListAsync(cancellationToken);

            foreach (var response in thawed)
            {
                response.IsFrozen = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSettingsAsync(
        Topic topic,
        bool isPubliclyListed,
        bool areResponsesPublic,
        bool anonymous,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        topic.IsPubliclyListed = isPubliclyListed;
        topic.AreResponsesPublic = areResponsesPublic;

        // Identity is only unchangeable once somebody has answered under it. At zero
        // responses there is nothing to unrecord and no deal to change.
        if (await db.Responses.AnyAsync(r => r.TopicId == topic.Id, cancellationToken))
        {
            if (anonymous != topic.IsAnonymous)
            {
                activity.RecordFailure("responses_exist");

                throw new InvalidOperationException(
                    "Somebody has already answered under this setting, so it is fixed now.");
            }
        }
        else
        {
            topic.ResponseIdentity = anonymous
                ? ResponseIdentity.Anonymous
                : ResponseIdentity.Required;
        }

        await db.SaveChangesAsync(cancellationToken);
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

        await db.SaveChangesAsync(cancellationToken);

        return token;
    }

    public async Task DeleteResponseAsync(
        Topic topic,
        Guid responseId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var response = await db.Responses.FirstOrDefaultAsync(
            r => r.Id == responseId && r.TopicId == topic.Id,
            cancellationToken);

        if (response is null)
        {
            return;
        }

        response.IsDeleted = true;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Publishing blesses a summary and shows it in one move: there is no state where a
    /// summary is final but nobody can read it.
    /// </summary>
    /// <exception cref="SummaryGroundingException">
    /// The tree has a branch ending in a claim nothing supports. The agent could not have
    /// submitted that, but a human editor can produce it a node at a time, and publishing is
    /// where it stops.
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


        summary.IsDraft = !published;
        summary.IsPublic = published;

        // Publishing hands the version back to the humans. Re-opening it to the agent is
        // then a deliberate act rather than a bit somebody forgot to flip.
        if (published)
        {
            summary.IsAgentEditable = false;
        }
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
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

        db.Summaries.Add(summary);

        await db.SaveChangesAsync(cancellationToken);

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

        db.Summaries.Remove(summary);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Summary> RequireSummaryAsync(
        Topic topic,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        return await db.Summaries.FirstOrDefaultAsync(
                s => s.Id == summaryId && s.TopicId == topic.Id,
                cancellationToken)
            ?? throw new InvalidOperationException($"No summary {summaryId} on this topic.");
    }

    private async Task<string> UniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Secrets.NewTopicCode();

            if (!await db.Topics.AnyAsync(s => s.Code == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not find a free topic code.");
    }
}

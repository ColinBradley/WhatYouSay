using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>A topic's timing setting, and the newest-first order it brings.</summary>
[TestClass]
public class ResponseTimingTests : DatabaseTest
{
    private static readonly DateTimeOffset sMorning = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task New_topics_record_timing_whatever_their_identity()
    {
        using var activity = TestTelemetry.Source.Start();

        var admin = new TopicAdminService(mDb);

        foreach (var identity in Enum.GetValues<ResponseIdentity>())
        {
            var created = await admin.CreateAsync(
                "Retro", "How did it go?", "pw", identity, false, false, this.Cancellation);

            Assert.AreEqual(ResponseTiming.Recorded, created.Topic.ResponseTiming);
        }
    }

    [TestMethod]
    public async Task A_new_anonymous_topic_records_the_time_but_no_author()
    {
        using var activity = TestTelemetry.Source.Start();

        var created = await new TopicAdminService(mDb).CreateAsync(
            "Retro", "How did it go?", "pw", ResponseIdentity.Anonymous, false, false, this.Cancellation);

        var response = await new ResponseService(mDb)
            .SubmitAsync(created.Topic, Secrets.NewToken(), "Meetings, mostly", "Colin", this.Cancellation);

        Assert.IsNull(response.Author);
        Assert.IsNotNull(response.SubmittedAt);
    }

    [TestMethod]
    public async Task Responses_are_listed_newest_first()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.OpenTopicAsync(ResponseTiming.Recorded);

        // Added out of order, so the listing cannot be following insertion.
        var added = await this.AddAsync(
            topic, ("second", sMorning.AddMinutes(1)), ("third", sMorning.AddMinutes(2)), ("first", sMorning));

        var (second, third, first) = (added[0], added[1], added[2]);
        var listed = await new ResponseService(mDb).ListAsync(topic, this.Cancellation);

        Assert.AreSequenceEqual(new[] { third.Id, second.Id, first.Id }, listed.Select(r => r.Id).ToArray());
    }

    [TestMethod]
    public async Task Editing_does_not_move_an_old_response_to_the_top()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.OpenTopicAsync(ResponseTiming.Recorded);
        var added = await this.AddAsync(topic, ("older", sMorning), ("newer", sMorning.AddMinutes(5)));
        var (older, newer) = (added[0], added[1]);

        await new ResponseService(mDb).EditAsync(topic, older, "older, reworded", null, this.Cancellation);

        await using var fresh = this.NewContext();

        var listed = await new ResponseService(fresh).ListAsync(topic, this.Cancellation);

        Assert.AreEqual(2, older.RevisionNumber);
        Assert.AreSequenceEqual(new[] { newer.Id, older.Id }, listed.Select(r => r.Id).ToArray());
    }

    [TestMethod]
    public async Task Responses_submitted_at_the_same_moment_fall_back_to_their_id()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.OpenTopicAsync(ResponseTiming.Recorded);

        await this.AddAsync(
            topic,
            [.. Enumerable.Range(0, 6).Select(i => (i.ToString(), sMorning))]);

        var listed = await new ResponseService(mDb).ListAsync(topic, this.Cancellation);
        var ids = listed.Select(r => r.Id).ToArray();

        // EF stores a Guid as upper-case text, so that is the order SQLite sorts them in.
        Assert.AreSequenceEqual(
            ids.OrderBy(id => id.ToString().ToUpperInvariant(), StringComparer.Ordinal).ToArray(),
            ids);
    }

    [TestMethod]
    public async Task An_untimed_topic_stores_no_time_anywhere()
    {
        using var activity = TestTelemetry.Source.Start();

        // Named, so the setting is shown to be independent of identity.
        var topic = NewTopic(ResponseIdentity.Required, ResponseTiming.NotRecorded);
        var summary = new Summary() { Body = "Overview", IsDraft = false, IsPublic = true };
        var node = new SummaryNode() { Text = "CI is slow" };

        summary.Nodes.Add(node);
        topic.Summaries.Add(summary);
        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var token = Secrets.NewToken();
        var responses = new ResponseService(mDb);
        var response = await responses.SubmitAsync(topic, token, "CI is slow", "Anna", this.Cancellation);

        await responses.EditAsync(topic, response, "CI is very slow", "Anna", this.Cancellation);
        await new CommentService(mDb).AddAsync(topic, node.Id, token, "Not quite.", "Anna", this.Cancellation);
        await new ReactionService(mDb).ToggleAsync(topic, node.Id, token, ReactionKind.Agree, this.Cancellation);

        Assert.AreEqual("Anna", response.Author);
        Assert.IsTrue(await mDb.ResponseRevisions.AllAsync(v => v.CreatedAt == null, this.Cancellation));
        Assert.IsNull((await mDb.NodeComments.SingleAsync(this.Cancellation)).CreatedAt);
        Assert.IsNull((await mDb.NodeReactions.SingleAsync(this.Cancellation)).CreatedAt);
    }

    [TestMethod]
    public async Task Timing_is_changeable_until_somebody_has_responded()
    {
        using var activity = TestTelemetry.Source.Start();

        var admin = new TopicAdminService(mDb);
        var created = await admin.CreateAsync(
            "Retro", "How did it go?", "pw", ResponseIdentity.Required, false, false, this.Cancellation);

        var topic = created.Topic;

        await admin.UpdateSettingsAsync(topic, false, false, false, false, this.Cancellation);
        Assert.AreEqual(ResponseTiming.NotRecorded, topic.ResponseTiming);

        await admin.UpdateSettingsAsync(topic, false, false, false, true, this.Cancellation);
        Assert.AreEqual(ResponseTiming.Recorded, topic.ResponseTiming);

        // A withdrawn response still fixes the setting, because it was given under it.
        var responses = new ResponseService(mDb);
        var response = await responses.SubmitAsync(topic, Secrets.NewToken(), "CI is slow", "Anna", this.Cancellation);

        await responses.WithdrawAsync(topic, response, this.Cancellation);

        Assert.IsFalse(await admin.CanChangeIdentityAndTimingAsync(topic, this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => admin.UpdateSettingsAsync(topic, false, false, false, false, this.Cancellation));

        Assert.AreEqual(ResponseTiming.Recorded, topic.ResponseTiming);

        // The other settings still save when identity and timing are passed unchanged.
        await admin.UpdateSettingsAsync(topic, true, false, false, true, this.Cancellation);

        Assert.IsTrue(topic.IsPubliclyListed);
    }

    [TestMethod]
    public async Task Response_timing_is_stored_as_a_string()
    {
        using var activity = TestTelemetry.Source.Start();

        mDb.Topics.Add(NewTopic(ResponseIdentity.Anonymous, ResponseTiming.NotRecorded));
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual("NotRecorded", await this.ScalarAsync("SELECT ResponseTiming FROM Topics"));
    }

    /// <summary>An existing database, written one migration back, upgraded in place.</summary>
    [TestMethod]
    public async Task The_migration_keeps_existing_anonymous_topics_untimed()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(this.Cancellation);

        await using var db = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(connection).Options);

        await db.Database.GetService<IMigrator>().MigrateAsync("ResponseRevisions", this.Cancellation);

        var anonymousId = Guid.CreateVersion7();
        var namedId = Guid.CreateVersion7();
        var anonymousResponseId = Guid.NewGuid();
        var namedResponseId = Guid.NewGuid();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                INSERT INTO "Topics" ("Id", "Code", "Title", "Description", "AdminPasswordHash", "SummariserTokenHash", "IsPubliclyListed", "IsAcceptingResponses", "AreResponsesPublic", "ResponseIdentity", "CreatedAt")
                VALUES ('{Stored(anonymousId)}', 'anon123', 'T', 'D', 'h', 'ta', 0, 1, 0, 'Anonymous', '2026-01-01T00:00:00.0000000+00:00'),
                       ('{Stored(namedId)}', 'named12', 'T', 'D', 'h', 'tn', 0, 1, 0, 'Optional', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO "Responses" ("Id", "TopicId", "Author", "AuthTokenHash", "IsFrozen", "IsDeleted", "RevisionNumber")
                VALUES ('{Stored(anonymousResponseId)}', '{Stored(anonymousId)}', NULL, 'a', 0, 0, 1),
                       ('{Stored(namedResponseId)}', '{Stored(namedId)}', 'Anna', 'n', 0, 0, 1);
                INSERT INTO "ResponseRevisions" ("ResponseId", "Number", "Body", "CreatedAt")
                VALUES ('{Stored(anonymousResponseId)}', 1, 'Meetings', NULL),
                       ('{Stored(namedResponseId)}', 1, 'CI is slow', '2026-01-01T09:00:00.0000000+00:00');
                """;

            await command.ExecuteNonQueryAsync(this.Cancellation);
        }

        await db.Database.MigrateAsync(this.Cancellation);

        var anonymous = await db.Topics.SingleAsync(t => t.Id == anonymousId, this.Cancellation);
        var named = await db.Topics.SingleAsync(t => t.Id == namedId, this.Cancellation);

        Assert.AreEqual(ResponseTiming.NotRecorded, anonymous.ResponseTiming);
        Assert.AreEqual(ResponseTiming.Recorded, named.ResponseTiming);

        // Nothing gains a time, including a response that arrives after the upgrade.
        var later = await new ResponseService(db)
            .SubmitAsync(anonymous, Secrets.NewToken(), "Standups", null, this.Cancellation);

        Assert.IsNull(later.SubmittedAt);
        Assert.IsTrue(await db.ResponseRevisions
            .Where(v => v.Response.TopicId == anonymousId)
            .AllAsync(v => v.CreatedAt == null, this.Cancellation));
    }

    /// <summary>EF's SQLite provider stores a Guid as upper-case text, and keyed lookups compare it as text.</summary>
    private static string Stored(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }

    private async Task<Topic> OpenTopicAsync(ResponseTiming timing)
    {
        var topic = NewTopic(ResponseIdentity.Anonymous, timing);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return topic;
    }

    /// <summary>Adds responses submitted at the given times, as submitting would have stored them.</summary>
    private async Task<IReadOnlyList<Response>> AddAsync(
        Topic topic,
        params (string Body, DateTimeOffset At)[] submissions
    )
    {
        var added = submissions
            .Select(submission => Revised(new Response()
            {
                Id = Guid.NewGuid(),
                TopicId = topic.Id,
                AuthTokenHash = Secrets.HashToken(Secrets.NewToken()),
            }, submission.Body, submission.At))
            .ToList();

        mDb.Responses.AddRange(added);
        await mDb.SaveChangesAsync(this.Cancellation);

        return added;
    }
}

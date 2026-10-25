using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class LiveSummaryTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    private const string TomSaid = "On-call was brutal, fourteen pages in a week.";

    [TestMethod]
    public async Task New_topics_start_live_and_other_topics_default_to_reviewed()
    {
        using var activity = TestTelemetry.Source.Start();

        var created = await this.Admin().CreateAsync(
            "Retro", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation);

        Assert.AreEqual(SummaryPolicy.Live, created.Topic.SummaryPolicy);
        Assert.AreEqual(SummaryPolicy.Reviewed, NewTopic(ResponseIdentity.Required).SummaryPolicy);
    }

    [TestMethod]
    public async Task The_first_summary_on_a_live_topic_is_its_live_summary_and_is_published()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();

        var summary = await this.Summaries().SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        Assert.IsTrue(summary.IsLive);
        Assert.IsTrue(summary.IsVisibleToPublic);
        Assert.IsTrue(summary.IsAgentEditable);
        Assert.AreEqual(1, summary.Revision);

        await using var fresh = this.NewContext();

        var visible = await new SummaryService(fresh).FindLatestVisibleAsync(topic.Id, this.Cancellation);

        Assert.AreEqual(summary.Id, visible!.Id);
    }

    /// <summary>One stable summary across updates, still published and open to the summariser.</summary>
    [TestMethod]
    public async Task A_live_write_stays_visible_and_open_to_the_summariser()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom) = await this.LiveTopicAsync();
        var service = this.Summaries();

        var first = await service.SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        var kept = first.Nodes.Single(n => n.Text == "CI is slow");

        var second = await service.SaveDraftAsync(
            topic,
            Draft(Carry(kept.Id), Cites("On-call hurts", tom, "fourteen pages")) with { BasedOnRevision = 1 },
            "agent",
            first.Id,
            this.Cancellation);

        await using var fresh = this.NewContext();

        var reloaded = (await new SummaryService(fresh).FindAsync(first.Id, this.Cancellation))!;

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, await fresh.Summaries.CountAsync(this.Cancellation));
        Assert.AreEqual(2, reloaded.Revision);
        Assert.IsTrue(reloaded.IsLive);
        Assert.IsTrue(reloaded.IsVisibleToPublic);
        Assert.IsTrue(reloaded.IsAgentEditable);
        Assert.IsTrue(reloaded.Nodes.Any(n => n.Id == kept.Id));
    }

    [TestMethod]
    public async Task A_reviewed_topic_keeps_summariser_work_private_until_published()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        topic.SummaryPolicy = SummaryPolicy.Reviewed;
        await mDb.SaveChangesAsync(this.Cancellation);

        var summary = await this.Summaries().SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        Assert.IsFalse(summary.IsLive);
        Assert.IsFalse(summary.IsVisibleToPublic);
        Assert.IsNull(await this.Summaries().FindLatestVisibleAsync(topic.Id, this.Cancellation));

        await this.Admin().SetSummaryVisibilityAsync(topic, summary.Id, true, this.Cancellation);

        Assert.IsTrue(summary.IsVisibleToPublic);
        Assert.IsFalse(summary.IsAgentEditable);
    }

    [TestMethod]
    public async Task A_new_summary_beside_an_existing_live_summary_is_a_private_draft()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var service = this.Summaries();

        var live = await service.SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        var other = await service.SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        Assert.IsTrue(live.IsLive);
        Assert.IsFalse(other.IsLive);
        Assert.IsTrue(other.IsDraft);
        Assert.IsFalse(other.IsPublic);
    }

    [TestMethod]
    public async Task The_live_summary_beats_a_newer_published_version_at_the_topic_url()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var service = this.Summaries();

        var live = await service.SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        var later = await service.SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        await this.Admin().SetSummaryVisibilityAsync(topic, later.Id, true, this.Cancellation);

        var visible = await service.FindLatestVisibleAsync(topic.Id, this.Cancellation);

        Assert.AreEqual(live.Id, visible!.Id);
    }

    [TestMethod]
    public async Task A_person_can_edit_the_live_summary_while_it_is_published()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();

        var live = await this.Summaries().SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        await new SummaryEditService(mDb).SetBodyAsync(topic, live.Id, "Edited by hand", this.Cancellation);

        Assert.AreEqual("Edited by hand", live.Body);
        Assert.IsTrue(live.IsVisibleToPublic);
    }

    [TestMethod]
    public async Task The_live_summary_cannot_be_unpublished()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();

        var live = await this.Summaries().SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna, "22 minutes")), "agent", null, this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Admin().SetSummaryVisibilityAsync(topic, live.Id, false, this.Cancellation));

        Assert.IsTrue(live.IsVisibleToPublic);
    }

    [TestMethod]
    public async Task Pausing_the_summariser_refuses_its_writes_until_resumed()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var service = this.Summaries();
        var draft = Draft(Cites("CI is slow", anna, "22 minutes"));

        var live = await service.SaveDraftAsync(topic, draft, "agent", null, this.Cancellation);

        await this.Admin().SetAgentEditableAsync(topic, live.Id, false, this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => service.SaveDraftAsync(
                topic, draft with { BasedOnRevision = 1 }, "agent", live.Id, this.Cancellation));

        Assert.AreEqual("summary_not_agent_editable", rejection.Reason);
        Assert.IsTrue(live.IsLive, "Pausing should not end live mode.");

        await this.Admin().SetAgentEditableAsync(topic, live.Id, true, this.Cancellation);
        await service.SaveDraftAsync(topic, draft with { BasedOnRevision = 1 }, "agent", live.Id, this.Cancellation);

        Assert.AreEqual(2, live.Revision);
    }

    [TestMethod]
    public async Task Requiring_review_ends_the_live_summary_and_closes_it_to_the_summariser()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, _) = await this.LiveTopicAsync();
        var service = this.Summaries();
        var draft = Draft(Cites("CI is slow", anna, "22 minutes"));

        var live = await service.SaveDraftAsync(topic, draft, "agent", null, this.Cancellation);

        await this.Admin().SetSummaryPolicyAsync(topic, SummaryPolicy.Reviewed, this.Cancellation);

        Assert.IsFalse(live.IsLive);
        Assert.IsTrue(live.IsVisibleToPublic);
        Assert.IsFalse(live.IsAgentEditable);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => service.SaveDraftAsync(
                topic, draft with { BasedOnRevision = 1 }, "agent", live.Id, this.Cancellation));

        Assert.AreEqual("summary_not_agent_editable", rejection.Reason);

        var next = await service.SaveDraftAsync(topic, draft, "agent", null, this.Cancellation);

        Assert.IsTrue(next.IsDraft);
        Assert.IsFalse(next.IsLive);
    }

    [TestMethod]
    public async Task Making_a_summary_live_moves_the_selection_and_publishes_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, _) = await this.LiveTopicAsync();
        var admin = this.Admin();

        var first = await admin.CreateEmptySummaryAsync(topic, this.Cancellation);
        var second = await admin.CreateEmptySummaryAsync(topic, this.Cancellation);

        await admin.MakeLiveAsync(topic, first.Id, this.Cancellation);
        await admin.MakeLiveAsync(topic, second.Id, this.Cancellation);

        await using var fresh = this.NewContext();

        var live = await fresh.Summaries.SingleAsync(s => s.IsLive, this.Cancellation);
        var former = await fresh.Summaries.SingleAsync(s => s.Id == first.Id, this.Cancellation);

        Assert.AreEqual(second.Id, live.Id);
        Assert.IsTrue(live.IsVisibleToPublic);
        Assert.IsTrue(live.IsAgentEditable);
        Assert.IsTrue(former.IsVisibleToPublic);
        Assert.IsFalse(former.IsAgentEditable);
    }

    [TestMethod]
    public async Task Only_a_live_topic_can_have_a_live_summary()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var summary = await this.Admin().CreateEmptySummaryAsync(topic, this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Admin().MakeLiveAsync(topic, summary.Id, this.Cancellation));

        Assert.IsFalse(summary.IsLive);
    }

    /// <summary>The database refuses a second live summary, so racing creators cannot both win.</summary>
    [TestMethod]
    public async Task A_topic_holds_at_most_one_live_summary()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, _) = await this.LiveTopicAsync();

        topic.Summaries.Add(new Summary() { Body = "One", IsLive = true, IsDraft = false, IsPublic = true });
        topic.Summaries.Add(new Summary() { Body = "Two", IsLive = true, IsDraft = false, IsPublic = true });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => mDb.SaveChangesAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_published_summary_opens_to_the_summariser_only_while_live()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, _) = await this.LiveTopicAsync();
        var admin = this.Admin();

        var summary = await admin.CreateEmptySummaryAsync(topic, this.Cancellation);

        await admin.SetAgentEditableAsync(topic, summary.Id, true, this.Cancellation);
        Assert.IsTrue(summary.IsAgentEditable, "A draft may be handed to the summariser.");

        await admin.SetSummaryVisibilityAsync(topic, summary.Id, true, this.Cancellation);
        Assert.IsFalse(summary.IsAgentEditable);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => admin.SetAgentEditableAsync(topic, summary.Id, true, this.Cancellation));
    }

    /// <summary>A database written before live summaries existed, upgraded in place.</summary>
    [TestMethod]
    public async Task The_migration_keeps_existing_topics_reviewed_and_their_feedback_in_place()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(this.Cancellation);

        await using var db = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(connection).Options);

        await db.Database.GetService<IMigrator>().MigrateAsync("ResponseRevisions", this.Cancellation);

        const string Quote = "22 minutes";
        const string At = "2026-01-01T09:00:00.0000000+00:00";

        var topicId = Guid.CreateVersion7();
        var responseId = Guid.NewGuid();
        var summaryId = Guid.CreateVersion7();
        var start = AnnaSaid.IndexOf(Quote, StringComparison.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                INSERT INTO "Topics" ("Id", "Code", "Title", "Description", "AdminPasswordHash", "SummariserTokenHash", "IsPubliclyListed", "IsAcceptingResponses", "AreResponsesPublic", "ResponseIdentity", "CreatedAt")
                VALUES ('{Stored(topicId)}', 'code123', 'T', 'D', 'h', 'th', 0, 0, 0, 'Required', '{At}');
                INSERT INTO "Responses" ("Id", "TopicId", "Author", "AuthTokenHash", "IsFrozen", "IsDeleted", "RevisionNumber")
                VALUES ('{Stored(responseId)}', '{Stored(topicId)}', 'Anna', 'a', 1, 0, 1);
                INSERT INTO "ResponseRevisions" ("ResponseId", "Number", "Body", "CreatedAt")
                VALUES ('{Stored(responseId)}', 1, '{AnnaSaid}', '{At}');
                INSERT INTO "Summaries" ("Id", "TopicId", "Body", "IsDraft", "IsPublic", "IsAgentEditable", "ResponseCountAtWrite", "CreatedAt", "UpdatedAt")
                VALUES ('{Stored(summaryId)}', '{Stored(topicId)}', 'o', 0, 1, 0, 1, '{At}', '{At}');
                INSERT INTO "SummaryNodes" ("Id", "SummaryId", "ParentId", "Ordinal", "Text")
                VALUES (7, '{Stored(summaryId)}', NULL, 0, 'Tooling'),
                       (8, '{Stored(summaryId)}', 7, 0, 'CI is slow');
                INSERT INTO "References" ("Id", "NodeId", "ResponseId", "RevisionNumber", "Quote", "StartIndex", "EndIndex")
                VALUES (1, 8, '{Stored(responseId)}', 1, '{Quote}', {start}, {start + Quote.Length});
                INSERT INTO "NodeReactions" ("Id", "NodeId", "ReactorTokenHash", "Kind", "CreatedAt")
                VALUES (1, 8, 'a', 'Agree', '{At}');
                INSERT INTO "NodeComments" ("Id", "NodeId", "AuthorTokenHash", "Author", "Body", "IsHidden", "CreatedAt")
                VALUES (1, 8, 'a', 'Anna', 'Not quite what I meant.', 0, '{At}');
                """;

            await command.ExecuteNonQueryAsync(this.Cancellation);
        }

        await db.Database.MigrateAsync(this.Cancellation);

        var topic = await db.Topics.SingleAsync(this.Cancellation);
        var summary = (await new SummaryService(db).FindAsync(summaryId, this.Cancellation))!;
        var leaf = summary.Nodes.Single(n => n.Id == 8);

        Assert.AreEqual(SummaryPolicy.Reviewed, topic.SummaryPolicy);
        Assert.AreEqual(1, summary.Revision);
        Assert.IsFalse(summary.IsLive);
        Assert.IsTrue(summary.IsVisibleToPublic);
        Assert.IsFalse(summary.IsAgentEditable);
        Assert.AreEqual(7, summary.Roots.Single().Id);
        Assert.AreEqual(leaf, summary.Roots.Single().Children.Single());
        Assert.IsNull(leaf.ArchivedAt);
        Assert.AreEqual(Quote, leaf.References.Single().Quote);
        Assert.IsTrue((await db.Responses.SingleAsync(this.Cancellation)).IsFrozen);
        Assert.AreEqual(1, await db.NodeReactions.CountAsync(r => r.NodeId == 8, this.Cancellation));
        Assert.AreEqual(1, await db.NodeComments.CountAsync(c => c.NodeId == 8, this.Cancellation));
        Assert.AreEqual(
            DateTimeOffset.Parse(At, CultureInfo.InvariantCulture),
            (await db.NodeComments.SingleAsync(this.Cancellation)).CreatedAt);
    }

    /// <summary>EF's SQLite provider stores a Guid as upper-case text, and keyed lookups compare it as text.</summary>
    private static string Stored(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }

    private static SummaryDraft Draft(params NodeDraft[] nodes)
    {
        return new SummaryDraft() { Body = "Overview", Nodes = nodes };
    }

    private static NodeDraft Carry(int id)
    {
        return new NodeDraft() { Id = id };
    }

    private static NodeDraft Cites(string text, Guid response, string quote)
    {
        return new NodeDraft()
        {
            Text = text,
            References = [new ReferenceDraft { ResponseId = response, Quote = quote }],
        };
    }

    private SummaryService Summaries()
    {
        return new SummaryService(mDb);
    }

    private TopicAdminService Admin()
    {
        return new TopicAdminService(mDb);
    }

    /// <summary>An open, live topic with two editable responses.</summary>
    private async Task<(Topic Topic, Guid Anna, Guid Tom)> LiveTopicAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.SummaryPolicy = SummaryPolicy.Live;

        var anna = Revised(new Response() { Id = Guid.NewGuid(), Author = "Anna", AuthTokenHash = "a" }, AnnaSaid);
        var tom = Revised(new Response() { Id = Guid.NewGuid(), Author = "Tom", AuthTokenHash = "t" }, TomSaid);

        topic.Responses.Add(anna);
        topic.Responses.Add(tom);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, anna.Id, tom.Id);
    }
}

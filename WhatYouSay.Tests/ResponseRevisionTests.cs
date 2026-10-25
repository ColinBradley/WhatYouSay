using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ResponseRevisionTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow and the build takes twenty minutes.";

    private const string AnnaLater = "Reviews are the real bottleneck, not CI.";

    [TestMethod]
    public async Task Submitting_records_the_first_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (_, response) = await this.SubmittedAsync();

        var revision = await mDb.ResponseRevisions.SingleAsync(this.Cancellation);

        Assert.AreEqual(ResponseRevision.First, response.RevisionNumber);
        Assert.AreEqual(ResponseRevision.First, revision.Number);
        Assert.AreEqual(AnnaSaid, revision.Body);
        Assert.IsNotNull(revision.CreatedAt);
    }

    [TestMethod]
    public async Task Editing_adds_a_normalised_revision_and_keeps_the_earlier_one()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();

        await new ResponseService(mDb).EditAsync(topic, response, "one\r\ntwo", "Anna", this.Cancellation);

        var revisions = await mDb.ResponseRevisions
            .OrderBy(v => v.Number)
            .ToListAsync(this.Cancellation);

        Assert.HasCount(2, revisions);
        Assert.AreEqual(AnnaSaid, revisions[0].Body);
        Assert.AreEqual("one\ntwo", revisions[1].Body);
        Assert.AreEqual(2, response.RevisionNumber);
        Assert.AreEqual(revisions[0].CreatedAt, response.SubmittedAt);
    }

    [TestMethod]
    public async Task Changing_only_the_author_records_no_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();

        await new ResponseService(mDb).EditAsync(topic, response, AnnaSaid, "Anna B", this.Cancellation);

        Assert.AreEqual(1, await mDb.ResponseRevisions.CountAsync(this.Cancellation));
        Assert.AreEqual("Anna B", response.Author);
    }

    [TestMethod]
    public async Task An_edit_that_changes_nothing_records_no_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();

        // Trailing whitespace normalises away, so this is the same wording.
        await new ResponseService(mDb).EditAsync(topic, response, AnnaSaid + "\r\n", "Anna", this.Cancellation);

        Assert.AreEqual(1, await mDb.ResponseRevisions.CountAsync(this.Cancellation));
        Assert.AreEqual(ResponseRevision.First, response.RevisionNumber);
    }

    [TestMethod]
    public async Task A_human_citation_keeps_its_words_after_an_edit()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();
        var (summaryId, nodeId) = await this.HandWrittenSummaryAsync(topic);

        await this.Edits().AddReferenceAsync(topic, summaryId, nodeId, response.Id, "twenty minutes", this.Cancellation);
        await new ResponseService(mDb).EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);

        var reference = await this.OnlyReferenceAsync(summaryId);

        Assert.AreEqual(ResponseRevision.First, reference.RevisionNumber);
        Assert.IsFalse(reference.IsOnCurrentRevision);
        Assert.AreEqual(AnnaSaid, reference.Revision.Body);
        Assert.IsTrue(QuoteLocator.Matches(reference.Revision.Body, reference.Quote, reference.StartIndex, reference.EndIndex));
    }

    [TestMethod]
    public async Task An_agent_citation_keeps_its_words_after_an_edit()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();

        var summary = await new SummaryService(mDb).SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes =
                [
                    new NodeDraft()
                    {
                        Text = "CI is slow",
                        References = [new ReferenceDraft { ResponseId = response.Id, Quote = "twenty minutes" }],
                    },
                ],
            },
            "agent",
            null,
            this.Cancellation);

        await new ResponseService(mDb).EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);

        var reference = await this.OnlyReferenceAsync(summary.Id);

        Assert.IsFalse(reference.IsOnCurrentRevision);
        Assert.AreEqual("twenty minutes", reference.Quote);
        Assert.IsTrue(QuoteLocator.Matches(reference.Revision.Body, reference.Quote, reference.StartIndex, reference.EndIndex));
    }

    [TestMethod]
    public async Task A_new_citation_is_taken_from_the_current_wording()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();
        var (summaryId, nodeId) = await this.HandWrittenSummaryAsync(topic);

        await new ResponseService(mDb).EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);
        await this.Edits().AddReferenceAsync(topic, summaryId, nodeId, response.Id, "real bottleneck", this.Cancellation);

        var reference = await this.OnlyReferenceAsync(summaryId);

        Assert.AreEqual(2, reference.RevisionNumber);
        Assert.IsTrue(reference.IsOnCurrentRevision);
    }

    [TestMethod]
    public async Task The_earlier_wording_cannot_be_newly_quoted()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();
        var (summaryId, nodeId) = await this.HandWrittenSummaryAsync(topic);

        await new ResponseService(mDb).EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Edits().AddReferenceAsync(topic, summaryId, nodeId, response.Id, "twenty minutes", this.Cancellation));

        Assert.AreEqual("quote_not_found", rejection.Reason);
    }

    [TestMethod]
    public async Task A_reference_cannot_name_another_responses_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, edited) = await this.SubmittedAsync();
        var (_, nodeId) = await this.HandWrittenSummaryAsync(topic);
        var service = new ResponseService(mDb);

        await service.SubmitAsync(topic, Secrets.NewToken(), "Tom's answer", "Tom", this.Cancellation);
        await service.EditAsync(topic, edited, AnnaLater, "Anna", this.Cancellation);

        var untouched = await mDb.Responses.SingleAsync(r => r.Id != edited.Id, this.Cancellation);

        // Revision 2 exists, but only for the other response.
        mDb.References.Add(new SummaryNodeReference()
        {
            NodeId = nodeId,
            ResponseId = untouched.Id,
            RevisionNumber = 2,
            Quote = "Tom",
            StartIndex = 0,
            EndIndex = 3,
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => mDb.SaveChangesAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Withdrawing_hides_every_quoted_revision()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.SubmittedAsync();
        var (summaryId, nodeId) = await this.HandWrittenSummaryAsync(topic);
        var responses = new ResponseService(mDb);

        await this.Edits().AddReferenceAsync(topic, summaryId, nodeId, response.Id, "twenty minutes", this.Cancellation);
        await responses.EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);
        await this.Edits().AddReferenceAsync(topic, summaryId, nodeId, response.Id, "real bottleneck", this.Cancellation);
        await responses.WithdrawAsync(topic, response, this.Cancellation);

        await using var fresh = this.NewContext();

        var read = await new SummaryService(fresh).FindAsync(summaryId, this.Cancellation);
        var edited = await new SummaryEditService(fresh).LoadAsync(topic, summaryId, this.Cancellation);

        Assert.IsEmpty(read!.Nodes.SelectMany(n => n.References));
        Assert.IsEmpty(edited!.Nodes.SelectMany(n => n.References));
    }

    /// <summary>An existing database, written one migration back, upgraded in place.</summary>
    [TestMethod]
    public async Task The_migration_makes_each_existing_response_its_own_first_revision()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(this.Cancellation);

        await using var db = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(connection).Options);

        await db.Database.GetService<IMigrator>()
            .MigrateAsync("SummaryResponseCountAtWrite", this.Cancellation);

        const string Quote = "twenty minutes";
        const string Submitted = "2026-01-01T09:00:00.0000000+00:00";
        const string Edited = "2026-01-02T09:00:00.0000000+00:00";

        var topicId = Guid.CreateVersion7();
        var responseId = Guid.NewGuid();
        var withdrawnId = Guid.NewGuid();
        var summaryId = Guid.CreateVersion7();
        var start = AnnaSaid.IndexOf(Quote, StringComparison.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                INSERT INTO "Topics" ("Id", "Code", "Title", "Description", "AdminPasswordHash", "SummariserTokenHash", "IsPubliclyListed", "IsAcceptingResponses", "AreResponsesPublic", "ResponseIdentity", "CreatedAt")
                VALUES ('{Stored(topicId)}', 'code123', 'T', 'D', 'h', 'th', 0, 0, 0, 'Required', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO "Responses" ("Id", "TopicId", "Body", "Author", "AuthTokenHash", "IsFrozen", "IsDeleted", "CreatedAt", "UpdatedAt")
                VALUES ('{Stored(responseId)}', '{Stored(topicId)}', '{AnnaSaid}', 'Anna', 'a', 1, 0, '{Submitted}', '{Edited}'),
                       ('{Stored(withdrawnId)}', '{Stored(topicId)}', 'Gone', 'Tom', 't', 1, 1, NULL, NULL);
                INSERT INTO "Summaries" ("Id", "TopicId", "Body", "IsDraft", "IsPublic", "IsAgentEditable", "ResponseCountAtWrite", "CreatedAt", "UpdatedAt")
                VALUES ('{Stored(summaryId)}', '{Stored(topicId)}', 'o', 0, 1, 0, 1, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO "SummaryNodes" ("Id", "SummaryId", "ParentId", "Ordinal", "Text") VALUES (1, '{Stored(summaryId)}', NULL, 0, 'Tooling');
                INSERT INTO "References" ("Id", "NodeId", "ResponseId", "Quote", "StartIndex", "EndIndex")
                VALUES (1, 1, '{Stored(responseId)}', '{Quote}', {start}, {start + Quote.Length}),
                       (2, 1, '{Stored(withdrawnId)}', 'Gone', 0, 4);
                """;

            await command.ExecuteNonQueryAsync(this.Cancellation);
        }

        await db.Database.MigrateAsync(this.Cancellation);

        var revisions = await db.ResponseRevisions.ToListAsync(this.Cancellation);
        var revision = revisions.Single(v => v.ResponseId == responseId);
        var summary = await new SummaryService(db).FindAsync(summaryId, this.Cancellation);
        var reference = summary!.Nodes.SelectMany(n => n.References).Single();

        Assert.HasCount(2, revisions);
        Assert.AreEqual(ResponseRevision.First, revision.Number);
        Assert.AreEqual(AnnaSaid, revision.Body);
        Assert.AreEqual(DateTimeOffset.Parse(Submitted, CultureInfo.InvariantCulture), revision.CreatedAt);
        Assert.AreEqual(responseId, reference.ResponseId);
        Assert.AreEqual(ResponseRevision.First, reference.RevisionNumber);
        Assert.IsTrue(reference.IsOnCurrentRevision);
        Assert.IsTrue(QuoteLocator.Matches(reference.Revision.Body, reference.Quote, reference.StartIndex, reference.EndIndex));

        // The next edit becomes revision 2 instead of colliding with the backfilled revision 1.
        var topic = await db.Topics.SingleAsync(this.Cancellation);
        var response = await db.Responses
            .Include(r => r.Revisions)
            .SingleAsync(r => r.Id == responseId, this.Cancellation);

        Assert.AreEqual("Anna", response.Author);

        response.IsFrozen = false;
        await new ResponseService(db).EditAsync(topic, response, AnnaLater, "Anna", this.Cancellation);

        Assert.AreEqual(2, response.RevisionNumber);
    }

    /// <summary>EF's SQLite provider stores a Guid as upper-case text, and keyed lookups compare it as text.</summary>
    private static string Stored(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }

    private async Task<(Topic Topic, Response Response)> SubmittedAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        await new ResponseService(mDb).SubmitAsync(topic, Secrets.NewToken(), AnnaSaid, "Anna", this.Cancellation);

        return (topic, await mDb.Responses.SingleAsync(this.Cancellation));
    }

    private async Task<(Guid SummaryId, int NodeId)> HandWrittenSummaryAsync(Topic topic)
    {
        var summary = await new TopicAdminService(mDb).CreateEmptySummaryAsync(topic, this.Cancellation);
        var nodeId = await this.Edits().AddNodeAsync(topic, summary.Id, null, "CI is slow", this.Cancellation);

        return (summary.Id, nodeId);
    }

    /// <summary>Reads the summary's only reference through a fresh context, as the next request would.</summary>
    private async Task<SummaryNodeReference> OnlyReferenceAsync(Guid summaryId)
    {
        await using var fresh = this.NewContext();

        var summary = await new SummaryService(fresh).FindAsync(summaryId, this.Cancellation);

        return summary!.Nodes.SelectMany(n => n.References).Single();
    }

    private SummaryEditService Edits()
    {
        return new SummaryEditService(mDb);
    }
}

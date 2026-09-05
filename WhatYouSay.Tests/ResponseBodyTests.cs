using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ResponseBodyTests : DatabaseTest
{
    [TestMethod]
    public void Carriage_returns_never_survive_normalisation()
    {
        Assert.AreEqual("one\ntwo", ResponseBody.Normalise("one\r\ntwo"));
        Assert.AreEqual("one\ntwo", ResponseBody.Normalise("one\rtwo"));
        Assert.AreEqual("one\n\ntwo", ResponseBody.Normalise("one\r\n\r\ntwo"));
        Assert.AreEqual("one\ntwo", ResponseBody.Normalise("  \r\none\r\ntwo\r\n  "));
    }

    [TestMethod]
    public async Task A_submitted_body_is_stored_with_line_feeds()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.OpenTopicAsync();

        await new ResponseService(mDb)
            .SubmitAsync(topic, "CI is slow.\r\nReviews are slower.", "Anna", this.Cancellation);

        var stored = await mDb.Responses.SingleAsync(this.Cancellation);

        Assert.AreEqual("CI is slow.\nReviews are slower.", stored.Body);
    }

    [TestMethod]
    public async Task An_edited_body_is_stored_with_line_feeds()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = await this.OpenTopicAsync();
        var service = new ResponseService(mDb);

        var token = await service.SubmitAsync(topic, "first", "Anna", this.Cancellation);
        var response = await service.FindOwnAsync(topic.Id, token, this.Cancellation);

        await service.EditAsync(topic, response!, "one\r\ntwo\r\nthree", "Anna", this.Cancellation);

        Assert.AreEqual("one\ntwo\nthree", (await mDb.Responses.SingleAsync(this.Cancellation)).Body);
    }

    /// <summary>
    /// The migration repairs offsets against the body it is about to change, so this walks
    /// the real upgrade: schema one migration back, CRLF rows written by hand, then up.
    /// </summary>
    [TestMethod]
    public async Task The_migration_shifts_stored_offsets_by_the_returns_it_removes()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(this.Cancellation);

        await using var db = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(connection).Options);

        await db.Database.GetService<IMigrator>()
            .MigrateAsync("RenameSurveyToTopic", this.Cancellation);

        // "CI is slow.\r\nReviews are slower." — the quote starts after one carriage
        // return, so both of its offsets should come back one lower.
        const string Body = "CI is slow.\r\nReviews are slower.";
        const string Quote = "Reviews are slower.";

        var topicId = Guid.CreateVersion7();
        var responseId = Guid.NewGuid();
        var summaryId = Guid.CreateVersion7();

        await ExecuteAsync(connection, $"""
            INSERT INTO "Topics" VALUES ('{topicId}', 'code123', 'T', 'D', 'h', 'th', 0, 1, 0, 'Required', '2026-01-01T00:00:00.0000000+00:00');
            INSERT INTO "Responses" VALUES ('{responseId}', '{topicId}', '{Body.Replace("\r", "' || char(13) || '").Replace("\n", "' || char(10) || '")}', NULL, 'a', 0, NULL, NULL);
            INSERT INTO "Summaries" VALUES ('{summaryId}', '{topicId}', 'o', 1, 0, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00', 'human');
            INSERT INTO "SummaryNodes" ("Id", "SummaryId", "ParentId", "Ordinal", "Text") VALUES (1, '{summaryId}', NULL, 0, 'Tooling');
            INSERT INTO "References" ("Id", "NodeId", "ResponseId", "Quote", "StartIndex", "EndIndex") VALUES (1, 1, '{responseId}', '{Quote}', {Body.IndexOf(Quote, StringComparison.Ordinal)}, {Body.IndexOf(Quote, StringComparison.Ordinal) + Quote.Length});
            """);

        await db.Database.MigrateAsync(this.Cancellation);

        var normalised = ResponseBody.Normalise(Body);
        var reference = await db.Set<SummaryNodeReference>().SingleAsync(this.Cancellation);
        var response = await db.Responses.SingleAsync(this.Cancellation);

        Assert.AreEqual(normalised, response.Body);
        Assert.AreEqual(normalised.IndexOf(Quote, StringComparison.Ordinal), reference.StartIndex);
        Assert.IsTrue(
            QuoteLocator.Matches(response.Body, reference.Quote, reference.StartIndex, reference.EndIndex),
            "The repaired offsets should still select the quote.");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private async Task<Topic> OpenTopicAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return topic;
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

/// <summary>
/// Real SQLite, real migrations, nothing mocked. Each test instance gets its own private
/// in-memory database, so no state is shared and the suite runs under ParallelMode.All.
/// </summary>
public abstract class DatabaseTest : IDisposable
{
    private readonly SqliteConnection mConnection;

    protected readonly WhatYouSayContext mDb;

    protected DatabaseTest()
    {
        // "Filename=:memory:" without a shared cache is private to this connection, and
        // the schema lives exactly as long as the connection does.
        mConnection = new SqliteConnection("Filename=:memory:");
        mConnection.Open();

        mDb = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(mConnection).Options);

        mDb.Database.Migrate();
    }

    /// <summary>Lets the runner abort a test promptly, which matters once tests interleave.</summary>
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected static Survey NewSurvey(ResponseIdentity identity) => new()
    {
        Id = Guid.CreateVersion7(),
        Code = Guid.NewGuid().ToString("n")[..7],
        Title = "Test survey",
        Description = "A prompt",
        AdminPasswordHash = "hash",
        SummariserTokenHash = Guid.NewGuid().ToString("n"),
        ResponseIdentity = identity,
        CreatedAt = DateTimeOffset.UtcNow
    };

    protected async Task<SummaryTopicPoint> SeededPointAsync()
    {
        var survey = NewSurvey(ResponseIdentity.Required);
        var summary = new Summary { Body = "overview" };
        var topic = new SummaryTopic { Name = "Tooling" };
        var point = new SummaryTopicPoint { Description = "CI is slow" };

        topic.Points.Add(point);
        summary.Topics.Add(topic);
        survey.Summaries.Add(summary);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(Cancellation);

        return point;
    }

    /// <summary>Raw SQL, to assert what actually landed in the file rather than what EF hands back.</summary>
    protected async Task<string?> ScalarAsync(string sql)
    {
        await using var command = mConnection.CreateCommand();
        command.CommandText = sql;

        return (await command.ExecuteScalarAsync(Cancellation))?.ToString();
    }

    public void Dispose()
    {
        mDb.Dispose();
        mConnection.Dispose();
        GC.SuppressFinalize(this);
    }
}

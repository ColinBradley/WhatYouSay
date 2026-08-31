using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

/// <summary>Gives each test its own private in-memory database.</summary>
public abstract class DatabaseTest : IDisposable
{
    private readonly SqliteConnection mConnection;

    protected readonly WhatYouSayContext mDb;

    protected DatabaseTest()
    {
        // Without a shared cache this is private to the connection, and the schema lives
        // exactly as long as the connection does.
        mConnection = new SqliteConnection("Filename=:memory:");
        mConnection.Open();

        mDb = new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>().UseSqlite(mConnection).Options);

        mDb.Database.Migrate();
    }

    /// <summary>Set by MSTest on each test instance.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Lets the runner abort a test promptly.</summary>
    protected CancellationToken Cancellation => this.TestContext.CancellationToken;

    protected static Survey NewSurvey(ResponseIdentity identity)
    {
        return new Survey()
        {
            Id = Guid.CreateVersion7(),
            Code = Guid.NewGuid().ToString("n")[..7],
            Title = "Test survey",
            Description = "A prompt",
            AdminPasswordHash = "hash",
            SummariserTokenHash = Guid.NewGuid().ToString("n"),
            ResponseIdentity = identity,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>A survey with a heading over one grounded node, the smallest usable tree.</summary>
    protected async Task<SummaryNode> SeededLeafAsync()
    {
        var survey = NewSurvey(ResponseIdentity.Required);
        var summary = new Summary { Body = "overview" };
        var heading = new SummaryNode { Text = "Tooling" };
        var leaf = new SummaryNode { Text = "CI is slow", Parent = heading };

        summary.Nodes.Add(heading);
        summary.Nodes.Add(leaf);
        survey.Summaries.Add(summary);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        return leaf;
    }

    /// <summary>Raw SQL, to assert what actually landed in the file rather than what EF hands back.</summary>
    protected async Task<string?> ScalarAsync(string sql)
    {
        await using var command = mConnection.CreateCommand();
        command.CommandText = sql;

        return (await command.ExecuteScalarAsync(this.Cancellation))?.ToString();
    }

    public void Dispose()
    {
        mDb.Dispose();
        mConnection.Dispose();
        GC.SuppressFinalize(this);
    }
}

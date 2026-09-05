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
            new DbContextOptionsBuilder<WhatYouSayContext>()
                .UseSqlite(mConnection)
                .Options
        );

        mDb.Database.Migrate();
    }

    /// <summary>Set by MSTest on each test instance.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A second context over the same database, for asserting what a later request would
    /// see. Filtered includes cannot evict an entity <see cref="mDb"/> is already tracking,
    /// so a test about withdrawn responses has to read through fresh eyes.
    /// </summary>
    protected WhatYouSayContext NewContext()
    {
        return new WhatYouSayContext(
            new DbContextOptionsBuilder<WhatYouSayContext>()
                .UseSqlite(mConnection)
                .Options
        );
    }

    /// <summary>Lets the runner abort a test promptly.</summary>
    protected CancellationToken Cancellation => this.TestContext.CancellationToken;

    protected static Topic NewTopic(ResponseIdentity identity)
    {
        return new Topic()
        {
            Id = Guid.CreateVersion7(),
            Code = Guid.NewGuid().ToString("n")[..7],
            Title = "Test topic",
            Description = "A prompt",
            AdminPasswordHash = "hash",
            SummariserTokenHash = Guid.NewGuid().ToString("n"),
            ResponseIdentity = identity,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>A topic with a heading over one grounded node, the smallest usable tree.</summary>
    protected async Task<SummaryNode> SeededLeafAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        var summary = new Summary { Body = "overview" };
        var heading = new SummaryNode { Text = "Tooling" };
        var leaf = new SummaryNode { Text = "CI is slow", Parent = heading };

        summary.Nodes.Add(heading);
        summary.Nodes.Add(leaf);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
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

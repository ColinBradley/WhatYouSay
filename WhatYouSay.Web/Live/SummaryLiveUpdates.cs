namespace WhatYouSay.Web.Live;

/// <summary>
/// Tells everyone reading a summary that somebody changed it. A reaction or a comment is a
/// thing the group did together, so a viewer holding the page open should see it arrive
/// rather than find out on their next reload.
/// </summary>
/// <remarks>
/// Singleton, and deliberately in-memory: it carries no state worth keeping, only the set of
/// circuits currently looking at something. A second instance of the app would not see these
/// notifications, which is the same trade the rest of a single-process self-hosted tool makes.
/// </remarks>
public sealed class SummaryLiveUpdates
{
    private readonly Lock mGate = new();

    private readonly Dictionary<Guid, List<Subscription>> mBySummary = [];

    /// <summary>
    /// Registers <paramref name="onChanged"/> until the returned handle is disposed. A
    /// component that forgets to dispose keeps its circuit reachable forever, so this is
    /// called from <c>OnInitialized</c> and disposed from <c>IDisposable</c>, never inline.
    /// </summary>
    public IDisposable Subscribe(Guid summaryId, Func<Task> onChanged)
    {
        var subscription = new Subscription(this, summaryId, onChanged);

        lock (mGate)
        {
            if (!mBySummary.TryGetValue(summaryId, out var subscribers))
            {
                subscribers = [];
                mBySummary[summaryId] = subscribers;
            }

            subscribers.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Runs every subscriber for this summary. Each reloads its own view rather than being
    /// handed one: a tally says which reactions are <em>yours</em>, so there is no shared
    /// payload to send.
    /// </summary>
    public async Task PublishAsync(Guid summaryId)
    {
        Subscription[] subscribers;

        lock (mGate)
        {
            if (!mBySummary.TryGetValue(summaryId, out var found))
            {
                return;
            }

            // Copied out of the lock: a subscriber may unsubscribe while being notified, and
            // notifying is an await that must not hold the gate.
            subscribers = [.. found];
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber.OnChanged();
            }
            catch (Exception)
            {
                // A circuit that has gone away must not stop the others being told. It
                // unsubscribes on dispose; this covers the window before that happens.
            }
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (mGate)
        {
            if (!mBySummary.TryGetValue(subscription.SummaryId, out var subscribers))
            {
                return;
            }

            subscribers.Remove(subscription);

            if (subscribers.Count == 0)
            {
                mBySummary.Remove(subscription.SummaryId);
            }
        }
    }

    private sealed class Subscription(SummaryLiveUpdates owner, Guid summaryId, Func<Task> onChanged)
        : IDisposable
    {
        public Guid SummaryId { get; } = summaryId;

        public Func<Task> OnChanged { get; } = onChanged;

        public void Dispose()
        {
            owner.Remove(this);
        }
    }
}

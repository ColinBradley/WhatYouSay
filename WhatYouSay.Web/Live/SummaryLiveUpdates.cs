namespace WhatYouSay.Web.Live;

/// <summary>
/// Notifies every circuit reading a summary that it changed. Registered as a singleton; the
/// subscriptions are in-process, so a second instance of the app would not see them.
/// </summary>
public sealed class SummaryLiveUpdates
{
    private readonly Lock mGate = new();

    private readonly Dictionary<Guid, List<Subscription>> mBySummary = [];

    /// <summary>
    /// Registers <paramref name="onChanged"/> until the returned handle is disposed. Failing to
    /// dispose keeps the subscriber's circuit reachable for the life of the process.
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
    /// Runs every subscriber for this summary. Each reloads its own view, since a tally is per
    /// viewer and there is no shared payload to hand out.
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

            // A subscriber may unsubscribe while being notified, and the await must not hold
            // the gate.
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
                // A circuit torn down before it unsubscribed must not stop the others.
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

    private sealed class Subscription : IDisposable
    {
        private readonly SummaryLiveUpdates mOwner;

        public Subscription(SummaryLiveUpdates owner, Guid summaryId, Func<Task> onChanged)
        {
            mOwner = owner;
            this.SummaryId = summaryId;
            this.OnChanged = onChanged;
        }

        public Guid SummaryId { get; }

        public Func<Task> OnChanged { get; }

        public void Dispose()
        {
            mOwner.Remove(this);
        }
    }
}

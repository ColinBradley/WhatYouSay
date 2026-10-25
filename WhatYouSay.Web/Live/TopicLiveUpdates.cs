namespace WhatYouSay.Web.Live;

/// <summary>
/// Tells every circuit showing a topic that something on it changed, so each one reloads what
/// its viewer may see. Registered as a singleton. The subscriptions are in-process, so a second
/// instance of the app would not see them.
/// </summary>
public sealed partial class TopicLiveUpdates
{
    private readonly Lock mGate = new();

    private readonly Dictionary<Guid, List<Subscription>> mByTopic = [];

    /// <summary>
    /// How many reloads may run at the same time, across every topic. Without a limit, one write
    /// on a busy topic starts a database read for every viewer at the same moment.
    /// A reload waiting for a slot holds no thread.
    /// </summary>
    private readonly SemaphoreSlim mReloadSlots = new(Environment.ProcessorCount);

    private readonly ILogger<TopicLiveUpdates> mLogger;

    public TopicLiveUpdates(ILogger<TopicLiveUpdates> logger)
    {
        mLogger = logger;
    }

    /// <summary>
    /// Calls <paramref name="onChanged"/> after changes to the topic until the returned handle is
    /// disposed. Failing to dispose keeps the subscriber's circuit reachable for the life of the
    /// process.
    /// </summary>
    /// <remarks>
    /// Calls to one subscriber never overlap. Changes that arrive while one call runs lead to
    /// one more call between them, not one each.
    /// </remarks>
    public IDisposable Subscribe(Guid topicId, Func<Task> onChanged)
    {
        var subscription = new Subscription(this, topicId, onChanged);

        lock (mGate)
        {
            if (!mByTopic.TryGetValue(topicId, out var subscribers))
            {
                subscribers = [];
                mByTopic[topicId] = subscribers;
            }

            subscribers.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Schedules a call to every subscriber to the topic, and returns without waiting for any of
    /// them. Call it only once the change is committed, because subscribers read the database.
    /// </summary>
    public void Publish(Guid topicId)
    {
        Subscription[] subscribers;

        lock (mGate)
        {
            if (!mByTopic.TryGetValue(topicId, out var found))
            {
                return;
            }

            subscribers = [.. found];
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Schedule();
        }
    }

    private async Task CallAsync(Func<Task> onChanged)
    {
        await mReloadSlots.WaitAsync();

        try
        {
            await onChanged();
        }
        catch (Exception failure)
        {
            // A circuit torn down mid-reload must not stop its own later calls or anyone else's.
            LogSubscriberFailed(mLogger, failure);
        }
        finally
        {
            mReloadSlots.Release();
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (mGate)
        {
            if (!mByTopic.TryGetValue(subscription.TopicId, out var subscribers))
            {
                return;
            }

            subscribers.Remove(subscription);

            if (subscribers.Count == 0)
            {
                mByTopic.Remove(subscription.TopicId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A live update subscriber failed.")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception failure);

    private sealed class Subscription : IDisposable
    {
        private readonly TopicLiveUpdates mOwner;

        private readonly Func<Task> mOnChanged;

        private readonly Lock mGate = new();

        private bool mIsRunning;

        private bool mIsPending;

        private bool mIsDisposed;

        public Subscription(TopicLiveUpdates owner, Guid topicId, Func<Task> onChanged)
        {
            mOwner = owner;
            mOnChanged = onChanged;
            this.TopicId = topicId;
        }

        public Guid TopicId { get; }

        /// <summary>
        /// Starts a call on the thread pool, so the publisher never waits for it. While a call is
        /// running, this only marks another one as due.
        /// </summary>
        public void Schedule()
        {
            lock (mGate)
            {
                if (mIsDisposed)
                {
                    return;
                }

                if (mIsRunning)
                {
                    mIsPending = true;

                    return;
                }

                mIsRunning = true;
            }

            _ = Task.Run(this.RunAsync);
        }

        public void Dispose()
        {
            lock (mGate)
            {
                mIsDisposed = true;
            }

            mOwner.Remove(this);
        }

        private async Task RunAsync()
        {
            do
            {
                await mOwner.CallAsync(mOnChanged);
            }
            while (this.TakePending());
        }

        /// <summary>Returns true when another call is due, and clears the mark.</summary>
        private bool TakePending()
        {
            lock (mGate)
            {
                if (mIsPending && !mIsDisposed)
                {
                    mIsPending = false;

                    return true;
                }

                mIsRunning = false;

                return false;
            }
        }
    }
}

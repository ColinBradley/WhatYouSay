using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WhatYouSay.Tests;

/// <summary>
/// Runs another operation just before the context's first save, as a request racing it
/// would.
/// </summary>
internal sealed class BeforeFirstSave : SaveChangesInterceptor
{
    private readonly Func<Task> mRacer;

    public BeforeFirstSave(Func<Task> racer)
    {
        mRacer = racer;
    }

    public bool HasRun { get; private set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (!this.HasRun)
        {
            this.HasRun = true;

            await mRacer();
        }

        return result;
    }
}

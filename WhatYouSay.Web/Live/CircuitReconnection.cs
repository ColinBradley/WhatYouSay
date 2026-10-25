using Microsoft.AspNetCore.Components.Server.Circuits;

namespace WhatYouSay.Web.Live;

/// <summary>
/// Raises <see cref="Reconnected"/> when a circuit's connection comes back after dropping.
/// Registered scoped, so each circuit has its own.
/// </summary>
/// <remarks>
/// A live view reloads on it, rather than trusting that every render made while the connection
/// was down reached the browser.
/// </remarks>
public sealed class CircuitReconnection : CircuitHandler
{
    private int mConnections;

    public event Action? Reconnected;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // The first connection is the circuit starting, not coming back.
        if (Interlocked.Increment(ref mConnections) > 1)
        {
            this.Reconnected?.Invoke();
        }

        return Task.CompletedTask;
    }
}

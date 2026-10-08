using System.Collections.Generic;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;

namespace Kloarte.Application;

/// <summary>The console binding's DMX input (MagicQ: Art-Net) as the views' live input.</summary>
public sealed class LiveInput : ILiveInput
{
    /// <summary>The console binding's input.</summary>
    private readonly IDmxInput _input;

    /// <summary>Wraps a DMX input.</summary>
    public LiveInput(IDmxInput input) => _input = input;

    /// <inheritdoc />
    public bool Running => _input.Running;

    /// <inheritdoc />
    public long Packets => _input.Packets;

    /// <inheritdoc />
    public int UniverseOffset
    {
        get => _input.UniverseOffset;
        set => _input.UniverseOffset = value;
    }

    /// <inheritdoc />
    public void Start() => _input.Start();

    /// <inheritdoc />
    public void Stop() => _input.Stop();

    /// <inheritdoc />
    public Dictionary<int, int> Snapshot() => _input.Snapshot();

    /// <inheritdoc />
    public bool Receive(byte[] packet) => _input.Handle(packet);
}

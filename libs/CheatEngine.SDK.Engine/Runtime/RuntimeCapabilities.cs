using System;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>An immutable set of optional capability observations for one Cheat Engine runtime snapshot.</summary>
public sealed class RuntimeCapabilities : IEquatable<RuntimeCapabilities>
{
    private readonly RuntimeCapabilityAvailability[] _entries;

    private RuntimeCapabilities(RuntimeCapabilityAvailability[] entries)
    {
        _entries = entries;
    }

    /// <summary>Gets an empty capability set.</summary>
    public static RuntimeCapabilities Empty { get; } = new(Array.Empty<RuntimeCapabilityAvailability>());

    /// <summary>Gets the number of explicit capability observations.</summary>
    public int Count => _entries.Length;

    /// <summary>Gets the ordered observations as a read-only span.</summary>
    public ReadOnlySpan<RuntimeCapabilityAvailability> Entries => _entries;

    /// <inheritdoc />
    public bool Equals(RuntimeCapabilities? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _entries.Length != other._entries.Length) return false;
        for (var i = 0; i < _entries.Length; i++)
            if (_entries[i] != other._entries[i])
                return false;
        return true;
    }

    /// <summary>Creates an immutable capability set by copying the supplied observations.</summary>
    /// <param name="entries">The observations to copy. Each capability identifier must be non-empty and unique.</param>
    /// <returns>The independent, immutable capability set.</returns>
    /// <exception cref="System.ArgumentException">An identifier is empty or occurs more than once.</exception>
    public static RuntimeCapabilities Create(ReadOnlySpan<RuntimeCapabilityAvailability> entries)
    {
        if (entries.IsEmpty) return Empty;

        var copy = entries.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i].Capability.IsEmpty)
                throw new ArgumentException("A runtime capability identifier cannot be empty.", nameof(entries));

            for (var previous = 0; previous < i; previous++)
                if (copy[previous].Capability == copy[i].Capability)
                    throw new ArgumentException("A runtime capability identifier occurs more than once.",
                        nameof(entries));
        }

        return new RuntimeCapabilities(copy);
    }

    /// <summary>Looks up one explicit capability observation.</summary>
    /// <param name="capability">The capability to look up.</param>
    /// <param name="availability">The matching observation, or the default value when absent.</param>
    /// <returns><see langword="true" /> when the set contains <paramref name="capability" />.</returns>
    public bool TryGet(RuntimeCapabilityId capability, out RuntimeCapabilityAvailability availability)
    {
        for (var i = 0; i < _entries.Length; i++)
            if (_entries[i].Capability == capability)
            {
                availability = _entries[i];
                return true;
            }

        availability = default;
        return false;
    }

    /// <summary>Gets an observed availability state, or unknown when absent.</summary>
    /// <param name="capability">The capability to look up.</param>
    /// <returns>The explicit state, or <see cref="RuntimeCapabilityAvailabilityState.Unknown" /> when absent.</returns>
    public RuntimeCapabilityAvailabilityState GetState(RuntimeCapabilityId capability)
    {
        return TryGet(capability, out var availability)
            ? availability.State
            : RuntimeCapabilityAvailabilityState.Unknown;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RuntimeCapabilities other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < _entries.Length; i++) hash.Add(_entries[i]);
        return hash.ToHashCode();
    }
}

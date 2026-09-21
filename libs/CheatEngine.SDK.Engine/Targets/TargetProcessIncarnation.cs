using System;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>A copied local-process incarnation identified by its PID and observed UTC creation time.</summary>
/// <remarks>
///     A PID alone can be reused. This value is created only by the SDK after it has combined Cheat Engine's selected
///     PID with a local process creation-time observation. It is not a process handle and never grants a caller a
///     right to select, open, or release a target process.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct TargetProcessIncarnation : IEquatable<TargetProcessIncarnation>
{
    internal TargetProcessIncarnation(int processId, long startedAtUtcTicks)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId,
                "A target process incarnation requires a positive process identifier.");
        if (startedAtUtcTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(startedAtUtcTicks), startedAtUtcTicks,
                "A target process incarnation requires a positive UTC creation time.");

        ProcessId = processId;
        StartedAtUtcTicks = startedAtUtcTicks;
    }

    /// <summary>Gets the positive Windows process identifier observed by Cheat Engine.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the UTC ticks of the local process creation-time observation.</summary>
    public long StartedAtUtcTicks { get; }

    /// <inheritdoc />
    public bool Equals(TargetProcessIncarnation other)
    {
        return ProcessId == other.ProcessId && StartedAtUtcTicks == other.StartedAtUtcTicks;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TargetProcessIncarnation other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ProcessId, StartedAtUtcTicks);
    }

    /// <summary>Compares two target-process incarnations.</summary>
    public static bool operator ==(TargetProcessIncarnation left, TargetProcessIncarnation right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two target-process incarnations.</summary>
    public static bool operator !=(TargetProcessIncarnation left, TargetProcessIncarnation right)
    {
        return !left.Equals(right);
    }
}

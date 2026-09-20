namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>A stable, SDK-owned identifier for one optional Engine capability.</summary>
/// <remarks>
///     The identifier intentionally does not expose a Lua global name. Public callers reason about a capability while
///     bindings retain Cheat Engine's exact spelling and call shape internally.
/// </remarks>
public readonly struct RuntimeCapabilityId : System.IEquatable<RuntimeCapabilityId>
{
    private readonly string? _value;

    /// <summary>Initializes a non-empty SDK capability identifier.</summary>
    /// <param name="value">The stable, non-white-space identifier.</param>
    /// <exception cref="System.ArgumentException"><paramref name="value" /> is null, empty, or white-space only.</exception>
    public RuntimeCapabilityId(string value)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    /// <summary>Gets the capability identifier, or an empty string for the default value.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Gets a value indicating whether this is the default, unusable identifier.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Gets the SDK capability for the CE file-version query.</summary>
    public static RuntimeCapabilityId CheatEngineVersion => new("Runtime.CheatEngineVersion");

    /// <summary>Gets the SDK capability for the CE host architecture query.</summary>
    public static RuntimeCapabilityId SystemArchitecture => new("Runtime.SystemArchitecture");

    /// <summary>Gets the SDK capability for the target architecture probes.</summary>
    public static RuntimeCapabilityId TargetArchitecture => new("Runtime.TargetArchitecture");

    /// <summary>Gets the SDK capability for the target ABI query.</summary>
    public static RuntimeCapabilityId TargetAbi => new("Runtime.TargetAbi");

    /// <inheritdoc />
    public bool Equals(RuntimeCapabilityId other) => string.Equals(_value, other._value, System.StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RuntimeCapabilityId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value is null ? 0 : System.StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Tests two capability identifiers for ordinal equality.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when the identifiers have the same ordinal value.</returns>
    public static bool operator ==(RuntimeCapabilityId left, RuntimeCapabilityId right) => left.Equals(right);

    /// <summary>Tests two capability identifiers for ordinal inequality.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when the identifiers differ.</returns>
    public static bool operator !=(RuntimeCapabilityId left, RuntimeCapabilityId right) => !left.Equals(right);
}

using System;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>A stable, SDK-owned identifier for one optional Engine capability.</summary>
/// <remarks>
///     The identifier intentionally does not expose a Lua global name. Public callers reason about a capability while
///     bindings retain Cheat Engine's exact spelling and call shape internally.
/// </remarks>
public readonly struct RuntimeCapabilityId : IEquatable<RuntimeCapabilityId>
{
	private readonly string? _value;

	/// <summary>Initializes a non-empty SDK capability identifier.</summary>
	/// <param name="value">The stable, non-white-space identifier.</param>
	/// <exception cref="System.ArgumentException"><paramref name="value" /> is null, empty, or white-space only.</exception>
	public RuntimeCapabilityId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
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

	/// <summary>Gets the SDK capability for observing Cheat Engine's currently selected target process.</summary>
	public static RuntimeCapabilityId CurrentProcess => new("Process.Current");

	/// <summary>Gets the SDK capability for explicit process selection and immediate verification.</summary>
	public static RuntimeCapabilityId ProcessSelection => new("Process.Selection");

	/// <summary>Gets the SDK capability for the target ABI query.</summary>
	public static RuntimeCapabilityId TargetAbi => new("Runtime.TargetAbi");

	/// <summary>Gets the SDK capability for Cheat Engine's configured pointer size, which is not the target bitness.</summary>
	public static RuntimeCapabilityId ConfiguredPointerSize => new("Runtime.ConfiguredPointerSize");

	/// <summary>Gets the SDK capability for whether the Cheat Engine host process is 64-bit.</summary>
	public static RuntimeCapabilityId CheatEngineBitness => new("Runtime.CheatEngineBitness");

	/// <summary>Gets the SDK capability for the operating system Cheat Engine reports.</summary>
	public static RuntimeCapabilityId OperatingSystem => new("Runtime.OperatingSystem");

	/// <summary>Gets the SDK capability for whether Cheat Engine reports an Android target.</summary>
	public static RuntimeCapabilityId TargetAndroid => new("Runtime.TargetAndroid");

	/// <summary>Gets the SDK capability for establishing the target backend (local process or CEServer).</summary>
	public static RuntimeCapabilityId TargetBackend => new("Runtime.TargetBackend");

	/// <inheritdoc />
	public bool Equals(RuntimeCapabilityId other)
	{
		return string.Equals(_value, other._value, StringComparison.Ordinal);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is RuntimeCapabilityId other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Value;
	}

	/// <summary>Tests two capability identifiers for ordinal equality.</summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true" /> when the identifiers have the same ordinal value.</returns>
	public static bool operator ==(RuntimeCapabilityId left, RuntimeCapabilityId right)
	{
		return left.Equals(right);
	}

	/// <summary>Tests two capability identifiers for ordinal inequality.</summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true" /> when the identifiers differ.</returns>
	public static bool operator !=(RuntimeCapabilityId left, RuntimeCapabilityId right)
	{
		return !left.Equals(right);
	}
}

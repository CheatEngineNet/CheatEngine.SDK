using System;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A borrowed handle to Cheat Engine's <c>MemScan</c> Lua class. It owns nothing; an instance that the plugin may
///     destroy is carried by <see cref="Owned{T}" />.
/// </summary>
/// <remarks>
///     <para>
///         CE 7.7.0.10621 documents <c>MemScan</c> in <c>celua.txt</c> lines 2506-2658. It distinguishes the GUI's
///         <c>getCurrentMemscan()</c> object from an object returned by <c>createMemScan(progressbar OPTIONAL)</c>.
///         This handle therefore never infers ownership: <see cref="MemoryScanSessions.TryCreate" /> is the specific
///         factory that establishes ownership for its created parent/child pair, whereas a GUI-supplied scan stays
///         borrowed. The Client capability remains live-gated independently of this low-level contract.
///     </para>
///     <para>
///         The safe, stateful path is <see cref="MemoryScanSession" />. This type remains a copyable borrowed value so
///         that it can model CE's object references without allocation or a disposal method.
///     </para>
///     <para>
///         CE's catalog does not establish a main-thread rule for this raw object operation. It therefore deliberately
///         has no <c>MainThreadOnly</c> metadata; <see cref="MemoryScanSession" /> imposes its own conservative runtime
///         guard where its owned child cleanup needs it.
///     </para>
/// </remarks>
[LuaClass("MemScan")]
public readonly struct MemScan : ICEObject<MemScan>, IEquatable<MemScan>
{
	private readonly CEObject _handle;

	/// <summary>Wraps a borrowed <c>MemScan</c> handle without validating its native class.</summary>
	/// <param name="handle">The native handle; a null handle gives the default value.</param>
	public MemScan(CEObject handle)
	{
		_handle = handle;
	}

	/// <inheritdoc />
	public CEObject Handle => _handle;

	/// <inheritdoc />
	public static MemScan FromHandle(CEObject handle)
	{
		return new MemScan(handle);
	}

	/// <summary>Gets a value indicating whether this is the null handle.</summary>
	public bool IsNull => _handle.IsNull;

	/// <summary>Compares two borrowed handles by native pointer value.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	/// <returns><see langword="true" /> when both handles carry the same pointer.</returns>
	public static bool operator ==(MemScan left, MemScan right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares two borrowed handles by native pointer value.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	/// <returns><see langword="true" /> when the pointers differ.</returns>
	public static bool operator !=(MemScan left, MemScan right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public bool Equals(MemScan other)
	{
		return _handle.Equals(other._handle);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is MemScan other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _handle.GetHashCode();
	}

	/// <summary>Formats the underlying native handle.</summary>
	/// <returns>The underlying <see cref="CEObject" /> representation.</returns>
	public override string ToString()
	{
		return _handle.ToString();
	}
}

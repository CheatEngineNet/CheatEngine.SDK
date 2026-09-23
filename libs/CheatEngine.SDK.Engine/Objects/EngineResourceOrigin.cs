using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     The context in which a durable SDK resource was created: the Lua runtime identity (attach epoch and state
///     generation) and, for a target-bound resource, the qualified target process incarnation.
/// </summary>
/// <remarks>
///     <para>
///         Every durable resource the SDK owns or leases (<see cref="Owned{T}" />, an allocated region, an Auto
///         Assembler patch, a symbol registration lease, a symbol-list registration lease) captures its origin inside
///         the admitted Lua operation that performed the effect. Cleanup compares the origin with the current context
///         before any Cheat Engine call: a resource created in another Lua universe (after a re-enable or a controlled
///         state replacement) is refused and reported, and a target-bound resource is never released against another
///         target incarnation.
///     </para>
///     <para>
///         Both identity components are compared: an equal attach epoch with a different state generation is another
///         Lua universe (audit A08-02). The value is a copied observation, not a capability. Consumers cannot
///         manufacture one: the only constructor is internal to the SDK. <see langword="default" /> means that no origin
///         was captured.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct EngineResourceOrigin
{
	internal EngineResourceOrigin(LuaStateIdentity runtime, TargetProcessIncarnation? target)
	{
		Runtime = runtime;
		Target = target;
	}

	/// <summary>Gets the Lua runtime identity (attach epoch and state generation) that created the resource.</summary>
	/// <remarks>The attach epoch is zero only for <see langword="default" />: no runtime identity was captured.</remarks>
	public LuaStateIdentity Runtime
	{
		get;
	}

	/// <summary>Gets the qualified target process incarnation the resource is bound to, or <see langword="null" />.</summary>
	/// <remarks>
	///     A resource that does not act on the ambient target (a plugin-owned Cheat Engine object, a symbol registration)
	///     has no target component.
	/// </remarks>
	public TargetProcessIncarnation? Target
	{
		get;
	}

	/// <summary>Gets whether the resource is bound to a target process incarnation.</summary>
	public bool IsTargetBound => Target.HasValue;

	/// <summary>
	///     Gets whether a runtime is attached and its current identity equals <see cref="Runtime" />.
	/// </summary>
	/// <remarks>
	///     A lock-free diagnostic observation made at the time of the read: an attach, detach or state replacement on
	///     another thread can change the answer immediately afterwards. It is not a lock and never authorizes an
	///     operation; the SDK's cleanup paths repeat the comparison inside their admitted Lua operation. A
	///     <see langword="default" /> origin is never current.
	/// </remarks>
	public bool IsCurrentRuntime => Runtime.AttachEpoch != 0 && LuaRuntime.IsAttached &&
	                                Runtime == LuaRuntime.CurrentStateIdentity;

	/// <summary>Captures the current runtime identity, without a target component.</summary>
	internal static EngineResourceOrigin CaptureRuntime()
	{
		return new EngineResourceOrigin(LuaRuntime.CurrentStateIdentity, null);
	}

	/// <summary>Whether <paramref name="runtime" /> is still the current Lua universe (attachment state is not checked).</summary>
	internal static bool IsCurrent(LuaStateIdentity runtime)
	{
		return runtime == LuaRuntime.CurrentStateIdentity;
	}
}

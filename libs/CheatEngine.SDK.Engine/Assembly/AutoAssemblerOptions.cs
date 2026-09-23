using System;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     Bounds and opt-ins for <see cref="AutoAssemblerPatcher.TryApplyWithOutcome(string, AutoAssemblerOptions, out AutoAssemblerPatch?)" />
///     and <see cref="AutoAssemblerPatcher.TryCheck(string, bool, AutoAssemblerOptions)" />.
/// </summary>
/// <remarks>
///     <para>
///         Host text (Cheat Engine's rejection detail and its compilation warnings) is copied only when
///         <see cref="CaptureHostText" /> is <see langword="true" />, because it can contain script source and file paths.
///         It is copied bounded (at most <see cref="MaxHostTextBytes" /> UTF-8 bytes, cut at a scalar boundary) and is
///         never parsed: an outcome category never depends on it.
///     </para>
///     <para>
///         The disable-info snapshot copies at most <see cref="MaxDisableInfoEntries" /> entries per section and skips
///         names longer than <see cref="MaxDisableInfoNameBytes" /> UTF-8 bytes; either limit marks the snapshot
///         <see cref="AutoAssemblerDisableInfoSnapshotStatus.Truncated" />. The rooted disable-info table itself is never
///         truncated: it stays the disable authority.
///     </para>
///     <para>
///         Values are validated when the options are used: an out-of-range value makes the call throw
///         <see cref="ArgumentOutOfRangeException" /> before any Lua work. Instances are immutable after initialization and
///         can be shared between threads.
///     </para>
/// </remarks>
public sealed class AutoAssemblerOptions
{
	/// <summary>The smallest accepted <see cref="MaxHostTextBytes" />.</summary>
	public const int MinHostTextBytes = 1;

	/// <summary>The largest accepted <see cref="MaxHostTextBytes" />.</summary>
	public const int MaxHostTextBytesLimit = 8192;

	/// <summary>The smallest accepted <see cref="MaxDisableInfoEntries" />.</summary>
	public const int MinDisableInfoEntries = 1;

	/// <summary>The largest accepted <see cref="MaxDisableInfoEntries" />.</summary>
	public const int MaxDisableInfoEntriesLimit = 65536;

	/// <summary>The smallest accepted <see cref="MaxDisableInfoNameBytes" />.</summary>
	public const int MinDisableInfoNameBytes = 1;

	/// <summary>The largest accepted <see cref="MaxDisableInfoNameBytes" />.</summary>
	public const int MaxDisableInfoNameBytesLimit = 4096;

	/// <summary>Gets the defaults: no host text, 512-byte text bound, 1024 entries per section, 256-byte names.</summary>
	public static AutoAssemblerOptions Default
	{
		get;
	} = new();

	/// <summary>
	///     Gets a value indicating whether Cheat Engine's rejection detail and compilation warnings are copied, bounded,
	///     into the outcome. <see langword="false" /> by default.
	/// </summary>
	public bool CaptureHostText
	{
		get;
		init;
	}

	/// <summary>
	///     Gets the maximum number of UTF-8 bytes copied from one host text value (1 to 8192; 512 by default). A longer
	///     text is cut at a scalar boundary and flagged as truncated.
	/// </summary>
	public int MaxHostTextBytes
	{
		get;
		init;
	} = 512;

	/// <summary>
	///     Gets the maximum number of entries visited per disable-info section (1 to 65536; 1024 by default).
	/// </summary>
	public int MaxDisableInfoEntries
	{
		get;
		init;
	} = 1024;

	/// <summary>
	///     Gets the maximum UTF-8 byte length of one copied disable-info name (1 to 4096; 256 by default). A longer name
	///     is skipped, never cut, so a copied name is always exact.
	/// </summary>
	public int MaxDisableInfoNameBytes
	{
		get;
		init;
	} = 256;

	internal void Validate(string parameterName)
	{
		if (MaxHostTextBytes is < MinHostTextBytes or > MaxHostTextBytesLimit)
		{
			throw new ArgumentOutOfRangeException(parameterName, MaxHostTextBytes,
				"MaxHostTextBytes must be between 1 and 8192.");
		}

		if (MaxDisableInfoEntries is < MinDisableInfoEntries or > MaxDisableInfoEntriesLimit)
		{
			throw new ArgumentOutOfRangeException(parameterName, MaxDisableInfoEntries,
				"MaxDisableInfoEntries must be between 1 and 65536.");
		}

		if (MaxDisableInfoNameBytes is < MinDisableInfoNameBytes or > MaxDisableInfoNameBytesLimit)
		{
			throw new ArgumentOutOfRangeException(parameterName, MaxDisableInfoNameBytes,
				"MaxDisableInfoNameBytes must be between 1 and 4096.");
		}
	}
}

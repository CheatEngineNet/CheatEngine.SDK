using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>A copied entry of the <c>allocs</c> section of Cheat Engine's Auto Assembler disable information.</summary>
/// <remarks>
///     A diagnostic copy only: the SDK never frees an allocation from this value. Cheat Engine's <c>[DISABLE]</c>
///     section, run through the patch's rooted disable-info table, owns the release of script allocations.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AutoAssemblerAllocationInfo
{
	internal AutoAssemblerAllocationInfo(string name, Address address, long size, Address? preferredAddress)
	{
		Name = name;
		Address = address;
		Size = size;
		PreferredAddress = preferredAddress;
	}

	/// <summary>Gets the allocation name used by the script (the key of the <c>allocs</c> entry).</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the target address of the allocation (the entry's <c>address</c> field).</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the allocation size in bytes (the entry's <c>size</c> field).</summary>
	public long Size
	{
		get;
	}

	/// <summary>
	///     Gets the preferred address Cheat Engine recorded (the entry's <c>prefered</c> field, spelled as Cheat Engine
	///     spells it), or <see langword="null" /> when the field is absent.
	/// </summary>
	public Address? PreferredAddress
	{
		get;
	}
}

using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>A copied entry of the <c>symbols</c> section of Cheat Engine's Auto Assembler disable information.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AutoAssemblerSymbolInfo
{
	internal AutoAssemblerSymbolInfo(string name, Address address)
	{
		Name = name;
		Address = address;
	}

	/// <summary>Gets the symbol or label name of the script.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the target address Cheat Engine assigned to the symbol.</summary>
	public Address Address
	{
		get;
	}
}

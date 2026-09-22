using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>The managed-only value and selector invariants of the inspection surface.</summary>
public sealed class InspectionValueTests
{
	[Fact]
	public void TargetProcessId_rejects_nonpositive_values()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TargetProcessId(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new TargetProcessId(-1));
		Assert.Equal(42, new TargetProcessId(42).Value);
	}

	[Fact]
	public void Explicit_process_enumeration_rejects_the_default_process_identifier_before_lua_is_acquired()
	{
		ModuleInfo[] destination = new ModuleInfo[1];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			EngineInspection.EnumerateModules(default, destination, out _));
	}

	[Fact]
	public void Module_and_symbol_selectors_reject_empty_values_and_compare_ordinally()
	{
		Assert.Throws<ArgumentException>(() => new ModuleName(""));
		Assert.Throws<ArgumentException>(() => new ModuleName(" \t"));
		Assert.Throws<ArgumentException>(() => new SymbolExpression(""));

		Assert.Equal(new ModuleName("GAME.EXE"), new ModuleName("GAME.EXE"));
		Assert.NotEqual(new ModuleName("GAME.EXE"), new ModuleName("game.exe"));
		Assert.NotEqual(new SymbolExpression("Game.Update"), new SymbolExpression("game.update"));
	}

	[Fact]
	public void MemorySize_and_file_offset_are_distinct_value_categories()
	{
		Assert.Equal("8192", new MemorySize(8192).ToString());
		Assert.Equal("2000", new ModuleFileOffset(0x2000).ToString());
		Assert.True(new MemorySize(8) < new MemorySize(16));
	}
}

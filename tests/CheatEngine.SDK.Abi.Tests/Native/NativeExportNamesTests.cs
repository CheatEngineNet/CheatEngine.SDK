using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Abi.Tests.Native;

public sealed class NativeExportNamesTests
{
	[Fact]
	public void Names_match_the_exports_cheat_engine_resolves()
	{
		Assert.Equal("CEPlugin_GetVersion", NativeExportNames.GetVersion);
		Assert.Equal("CEPlugin_InitializePlugin", NativeExportNames.InitializePlugin);
		Assert.Equal("CEPlugin_DisablePlugin", NativeExportNames.DisablePlugin);
	}
}

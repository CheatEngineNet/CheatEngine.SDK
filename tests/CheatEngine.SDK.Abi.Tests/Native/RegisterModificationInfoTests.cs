using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Structural regression tests for the AMD64 branch of <c>REGISTERMODIFICATIONINFO</c> in the installed CE
///     7.7.0.10621 C SDK (SHA-256 <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>).
/// </summary>
public sealed unsafe class RegisterModificationInfoTests
{
	[Fact]
	public void RegisterModificationInfo_change_flags_on_64_bit_match_the_installed_C_header_layout()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		RegisterModificationInfo info = default;
		void* origin = &info;

		Assert.Equal(264, Layout.SizeOf<RegisterModificationInfo>());
		Assert.Equal(0, Layout.OffsetOf(origin, &info.Address));

		Assert.Equal(8, Layout.OffsetOf(origin, &info.ChangeEax));
		Assert.Equal(12, Layout.OffsetOf(origin, &info.ChangeEbx));
		Assert.Equal(16, Layout.OffsetOf(origin, &info.ChangeEcx));
		Assert.Equal(20, Layout.OffsetOf(origin, &info.ChangeEdx));
		Assert.Equal(24, Layout.OffsetOf(origin, &info.ChangeEsi));
		Assert.Equal(28, Layout.OffsetOf(origin, &info.ChangeEdi));
		Assert.Equal(32, Layout.OffsetOf(origin, &info.ChangeEbp));
		Assert.Equal(36, Layout.OffsetOf(origin, &info.ChangeEsp));
		Assert.Equal(40, Layout.OffsetOf(origin, &info.ChangeEip));
		Assert.Equal(44, Layout.OffsetOf(origin, &info.ChangeR8));
		Assert.Equal(48, Layout.OffsetOf(origin, &info.ChangeR9));
		Assert.Equal(52, Layout.OffsetOf(origin, &info.ChangeR10));
		Assert.Equal(56, Layout.OffsetOf(origin, &info.ChangeR11));
		Assert.Equal(60, Layout.OffsetOf(origin, &info.ChangeR12));
		Assert.Equal(64, Layout.OffsetOf(origin, &info.ChangeR13));
		Assert.Equal(68, Layout.OffsetOf(origin, &info.ChangeR14));
		Assert.Equal(72, Layout.OffsetOf(origin, &info.ChangeR15));
		Assert.Equal(76, Layout.OffsetOf(origin, &info.ChangeCf));
		Assert.Equal(80, Layout.OffsetOf(origin, &info.ChangePf));
		Assert.Equal(84, Layout.OffsetOf(origin, &info.ChangeAf));
		Assert.Equal(88, Layout.OffsetOf(origin, &info.ChangeZf));
		Assert.Equal(92, Layout.OffsetOf(origin, &info.ChangeSf));
		Assert.Equal(96, Layout.OffsetOf(origin, &info.ChangeOf));
	}

	[Fact]
	public void RegisterModificationInfo_replacement_values_on_64_bit_match_the_installed_C_header_layout()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		RegisterModificationInfo info = default;
		void* origin = &info;

		Assert.Equal(104, Layout.OffsetOf(origin, &info.NewEax));
		Assert.Equal(112, Layout.OffsetOf(origin, &info.NewEbx));
		Assert.Equal(120, Layout.OffsetOf(origin, &info.NewEcx));
		Assert.Equal(128, Layout.OffsetOf(origin, &info.NewEdx));
		Assert.Equal(136, Layout.OffsetOf(origin, &info.NewEsi));
		Assert.Equal(144, Layout.OffsetOf(origin, &info.NewEdi));
		Assert.Equal(152, Layout.OffsetOf(origin, &info.NewEbp));
		Assert.Equal(160, Layout.OffsetOf(origin, &info.NewEsp));
		Assert.Equal(168, Layout.OffsetOf(origin, &info.NewEip));
		Assert.Equal(176, Layout.OffsetOf(origin, &info.NewR8));
		Assert.Equal(184, Layout.OffsetOf(origin, &info.NewR9));
		Assert.Equal(192, Layout.OffsetOf(origin, &info.NewR10));
		Assert.Equal(200, Layout.OffsetOf(origin, &info.NewR11));
		Assert.Equal(208, Layout.OffsetOf(origin, &info.NewR12));
		Assert.Equal(216, Layout.OffsetOf(origin, &info.NewR13));
		Assert.Equal(224, Layout.OffsetOf(origin, &info.NewR14));
		Assert.Equal(232, Layout.OffsetOf(origin, &info.NewR15));

		Assert.Equal(240, Layout.OffsetOf(origin, &info.NewCf));
		Assert.Equal(244, Layout.OffsetOf(origin, &info.NewPf));
		Assert.Equal(248, Layout.OffsetOf(origin, &info.NewAf));
		Assert.Equal(252, Layout.OffsetOf(origin, &info.NewZf));
		Assert.Equal(256, Layout.OffsetOf(origin, &info.NewSf));
		Assert.Equal(260, Layout.OffsetOf(origin, &info.NewOf));
	}
}

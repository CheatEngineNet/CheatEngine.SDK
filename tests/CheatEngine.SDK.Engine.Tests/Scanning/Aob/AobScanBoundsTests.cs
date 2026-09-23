using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>
///     The CE work limit of the bounded AOB route: a non-empty half-open range, refused before any CE call when empty
///     or inverted (spike D4.3), built from a module as <c>[base, base + size)</c> (CE's own module convention, D4.1).
/// </summary>
public sealed class AobScanBoundsTests
{
	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_default_is_invalid()
	{
		AobScanBounds bounds = default;

		Assert.False(bounds.IsValid);
		Assert.Equal(0UL, bounds.Length);
		Assert.False(bounds.Contains(Address.Zero));
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(0x1000UL, 0x1000UL)]
	[InlineData(0x2000UL, 0x1000UL)]
	[InlineData(0UL, 0UL)]
	[InlineData(ulong.MaxValue, 0UL)]
	public void AobScanBounds_TryCreate_refuses_empty_and_inverted_ranges(ulong start, ulong stop)
	{
		Assert.False(AobScanBounds.TryCreate(new Address(start), new Address(stop), out AobScanBounds bounds));

		Assert.Equal(default, bounds);
		Assert.False(bounds.IsValid);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_TryCreate_keeps_the_bounds_verbatim_and_reports_the_length()
	{
		Assert.True(AobScanBounds.TryCreate(new Address(0x1000), new Address(0x1004), out AobScanBounds bounds));

		Assert.True(bounds.IsValid);
		Assert.Equal(new Address(0x1000), bounds.Start);
		Assert.Equal(new Address(0x1004), bounds.Stop);
		Assert.Equal(4UL, bounds.Length);
		Assert.True(AobScanBounds.TryCreate(Address.Zero, new Address(ulong.MaxValue), out AobScanBounds whole));
		Assert.Equal(ulong.MaxValue, whole.Length);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_TryFromModule_uses_base_plus_image_size_as_the_exclusive_stop()
	{
		ModuleInfo module = new("Tutorial-x86_64.exe", new Address(0x1_0000_0000), new MemorySize(0x36_7000), true,
			"Tutorial-x86_64.exe");

		Assert.True(AobScanBounds.TryFromModule(in module, out AobScanBounds bounds));

		Assert.Equal(new Address(0x1_0000_0000), bounds.Start);
		Assert.Equal(new Address(0x1_0036_7000), bounds.Stop);
		Assert.Equal(0x36_7000UL, bounds.Length);
		Assert.True(bounds.Contains(new Address(0x1_0036_6FFF)));
		Assert.False(bounds.Contains(new Address(0x1_0036_7000)));
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_TryFromModule_refuses_a_module_without_image_size()
	{
		ModuleInfo module = new("unknown.dll", new Address(0x40_0000), null, false, "unknown.dll");

		Assert.False(AobScanBounds.TryFromModule(in module, out AobScanBounds bounds));

		Assert.False(bounds.IsValid);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_TryFromModule_refuses_a_zero_image_size()
	{
		ModuleInfo module = new("empty.dll", new Address(0x40_0000), new MemorySize(0), false, "empty.dll");

		Assert.False(AobScanBounds.TryFromModule(in module, out AobScanBounds bounds));

		Assert.False(bounds.IsValid);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(0xFFFF_FFFF_FFFF_F000UL, 0x1000UL)]
	[InlineData(0xFFFF_FFFF_FFFF_F000UL, 0x2000UL)]
	[InlineData(ulong.MaxValue, 1UL)]
	public void AobScanBounds_TryFromModule_refuses_an_end_beyond_the_address_space(ulong baseAddress, ulong size)
	{
		ModuleInfo module = new("high.dll", new Address(baseAddress), new MemorySize(size), true, "high.dll");

		Assert.False(AobScanBounds.TryFromModule(in module, out AobScanBounds bounds));

		Assert.False(bounds.IsValid);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AobScanBounds_Contains_is_start_inclusive_and_stop_exclusive()
	{
		Assert.True(AobScanBounds.TryCreate(new Address(0x1000), new Address(0x2000), out AobScanBounds bounds));

		Assert.False(bounds.Contains(new Address(0x0FFF)));
		Assert.True(bounds.Contains(new Address(0x1000)));
		Assert.True(bounds.Contains(new Address(0x1FFF)));
		Assert.False(bounds.Contains(new Address(0x2000)));
		Assert.False(bounds.Contains(new Address(ulong.MaxValue)));
	}
}

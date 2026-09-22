using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>Pure contract tests for the optional CE AOBScan argument model.</summary>
public sealed class AobScanOptionsTests
{
	[Fact]
	public void Default_has_no_protection_or_alignment_arguments()
	{
		AobScanOptions options = AobScanOptions.Default;

		Assert.Null(options.ProtectionFlags);
		Assert.Equal(FastScanMethod.NotAligned, options.AlignmentMethod);
		Assert.Null(options.AlignmentParameter);
		Assert.Equal(options, new AobScanOptions());
	}

	[Fact]
	public void Constructor_preserves_the_exact_CE_argument_values()
	{
		AobScanOptions options = new("+X-C-W", FastScanMethod.LastDigits, "F0");

		Assert.Equal("+X-C-W", options.ProtectionFlags);
		Assert.Equal(FastScanMethod.LastDigits, options.AlignmentMethod);
		Assert.Equal("F0", options.AlignmentParameter);
		Assert.Equal(options, new AobScanOptions("+X-C-W", FastScanMethod.LastDigits, "F0"));
		Assert.NotEqual(options, new AobScanOptions("+X-C-W", FastScanMethod.Aligned, "F0"));
	}

	[Fact]
	public void Constructor_rejects_an_unknown_alignment_method_before_a_scan()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new AobScanOptions(null, (FastScanMethod) 3, null));
	}

	[Fact]
	public void Constructor_requires_a_nonempty_parameter_for_nondefault_alignment()
	{
		Assert.Throws<ArgumentException>(() => new AobScanOptions(null, FastScanMethod.Aligned, null));
		Assert.Throws<ArgumentException>(() => new AobScanOptions(null, FastScanMethod.LastDigits, string.Empty));
		Assert.Throws<ArgumentException>(() => new AobScanOptions(null, FastScanMethod.NotAligned, "10"));
	}
}

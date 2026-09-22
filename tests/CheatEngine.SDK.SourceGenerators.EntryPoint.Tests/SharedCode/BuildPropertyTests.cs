using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.Shared;

using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.SharedCode;

public sealed class BuildPropertyTests
{
	private const string Key = BuildProperty.KeyPrefix + "CheatEngineSdkSwitch";

	[Fact]
	public void KeyPrefix_matches_the_compiler_visible_property_convention()
	{
		Assert.Equal("build_property.", BuildProperty.KeyPrefix);
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("TRUE", true)]
	[InlineData(" True ", true)]
	[InlineData("false", false)]
	[InlineData("False", false)]
	[InlineData("\tfalse ", false)]
	public void ReadBoolean_boolean_text_is_parsed_whatever_the_default(string raw, bool expected)
	{
		AnalyzerConfigOptions options = Options(Key, raw);

		Assert.Equal(expected, BuildProperty.ReadBoolean(options, Key, true));
		Assert.Equal(expected, BuildProperty.ReadBoolean(options, Key, false));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("0")]
	[InlineData("1")]
	[InlineData("no")]
	[InlineData("disable")]
	public void ReadBoolean_non_boolean_text_yields_the_default(string raw)
	{
		AnalyzerConfigOptions options = Options(Key, raw);

		Assert.True(BuildProperty.ReadBoolean(options, Key, true));
		Assert.False(BuildProperty.ReadBoolean(options, Key, false));
	}

	[Fact]
	public void ReadBoolean_missing_key_yields_the_default()
	{
		Assert.True(BuildProperty.ReadBoolean(TestAnalyzerConfigOptions.Empty, Key, true));
		Assert.False(BuildProperty.ReadBoolean(TestAnalyzerConfigOptions.Empty, Key, false));
	}

	[Fact]
	public void ReadBoolean_null_options_throws()
	{
		Assert.Throws<ArgumentNullException>(() => BuildProperty.ReadBoolean(null!, Key, true));
	}

	private static TestAnalyzerConfigOptions Options(string key, string value)
	{
		return new TestAnalyzerConfigOptions(ImmutableDictionary
			.Create<string, string>(AnalyzerConfigOptions.KeyComparer).Add(key, value));
	}
}

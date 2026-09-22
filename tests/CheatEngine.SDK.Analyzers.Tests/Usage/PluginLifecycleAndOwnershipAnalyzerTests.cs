using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Analyzers.Usage;

namespace CheatEngine.SDK.Analyzers.Tests.Usage;

/// <summary>Direct semantic tests for the lifecycle and borrowed-ownership rules CESDK1001, CESDK1003 and CESDK1005.</summary>
public sealed class PluginLifecycleAndOwnershipAnalyzerTests
{
	[Fact]
	public async Task Enabled_only_calls_from_a_plugin_constructor_and_field_initializer_report_CESDK1001()
	{
		await AnalyzerVerifier<PluginLifecycleAndOwnershipAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lifetime;
			using CheatEngine.SDK.Annotations.Plugin;
			using CheatEngine.SDK.Hosting.Plugin;
			using System.Threading.Tasks;

			namespace Demo;

			[RequiresPluginEnabled]
			internal static class EnabledApi
			{
			    public static int Value => 1;
			    public static void Connect() { }
			}

			internal sealed class EnabledClient
			{
			    [RequiresPluginEnabled]
			    public EnabledClient() { }
			}

			[CheatEnginePlugin("Demo")]
			public sealed class DemoPlugin : CheatEnginePlugin
			{
			    private readonly int _value = {|CESDK1001:EnabledApi.Value|};

			    public int Initializer { get; } = {|CESDK1001:EnabledApi.Value|};

			    static DemoPlugin()
			    {
			        var value = {|CESDK1001:EnabledApi.Value|};
			    }

			    public DemoPlugin()
			    {
			        {|CESDK1001:EnabledApi.Connect()|};
			        {|CESDK1001:new EnabledClient()|};
			    }

			    protected override async void {|CESDK1005:OnEnable|}()
			    {
			        await Task.Yield();
			        EnabledApi.Connect();
			    }

			    protected override async void {|CESDK1005:OnDisable|}()
			    {
			        await Task.Yield();
			    }
			}
			""");
	}

	[Fact]
	public async Task Directly_borrowed_parameters_properties_and_returns_cannot_be_disposed()
	{
		await AnalyzerVerifier<PluginLifecycleAndOwnershipAnalyzer>.VerifyAsync(
			"""
			using System;
			using System.Threading.Tasks;
			using CheatEngine.SDK.Annotations.Lifetime;

			namespace Demo;

			internal sealed class Borrowed : IDisposable, IAsyncDisposable
			{
			    public void Dispose() { }
			    public ValueTask DisposeAsync() => default;
			}

			internal static class BorrowedSource
			{
			    [return: CEOwned]
			    public static Borrowed Create() => new();

			    [CEOwned]
			    public static Borrowed Current => new();

			    public static void Consume([CEOwned] Borrowed value)
			    {
			        {|CESDK1003:Create().Dispose()|};
			        {|CESDK1003:Current.Dispose()|};
			        {|CESDK1003:value.DisposeAsync()|};
			    }
			}
			""");
	}

	[Fact]
	public async Task Borrowed_value_stored_in_a_local_is_not_guessed_to_be_owned_or_borrowed()
	{
		await AnalyzerVerifier<PluginLifecycleAndOwnershipAnalyzer>.VerifyAsync(
			"""
			using System;
			using CheatEngine.SDK.Annotations.Lifetime;

			namespace Demo;

			internal sealed class Borrowed : IDisposable
			{
			    public void Dispose() { }
			}

			internal static class BorrowedSource
			{
			    [return: CEOwned]
			    public static Borrowed Create() => new();

			    public static void DisposeAfterExplicitTransferBoundary()
			    {
			        var local = Create();
			        local.Dispose();
			    }
			}
			""");
	}
}

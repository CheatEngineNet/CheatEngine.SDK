using System.Reflection;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>The origin context of durable resources: a copied, SDK-made value that compares both identity components.</summary>
public sealed class EngineResourceOriginTests
{
	[Fact]
	public void Default_origin_names_no_runtime_and_no_target()
	{
		EngineResourceOrigin origin = default;

		Assert.Equal(0, origin.Runtime.AttachEpoch);
		Assert.Equal(0, origin.Runtime.StateGeneration);
		Assert.Null(origin.Target);
		Assert.False(origin.IsTargetBound);
		Assert.False(origin.IsCurrentRuntime);
	}

	[Fact]
	public void Consumers_cannot_construct_an_origin()
	{
		ConstructorInfo[] constructors = typeof(EngineResourceOrigin).GetConstructors(
			BindingFlags.Instance | BindingFlags.Public);

		Assert.All(constructors, static constructor => Assert.Empty(constructor.GetParameters()));
		Assert.All(typeof(EngineResourceOrigin).GetProperties(BindingFlags.Instance | BindingFlags.Public),
			static property => Assert.False(property.CanWrite, property.Name + " is writable."));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Current_runtime_compares_both_the_attach_epoch_and_the_state_generation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineResourceOrigin first = EngineResourceOrigin.CaptureRuntime();

		FakeHost.ReplaceStateGeneration();
		EngineResourceOrigin replaced = EngineResourceOrigin.CaptureRuntime();

		Assert.Equal(first.Runtime.AttachEpoch, replaced.Runtime.AttachEpoch);
		Assert.NotEqual(first, replaced);
		Assert.False(first.IsCurrentRuntime);
		Assert.True(replaced.IsCurrentRuntime);

		LuaRuntime.Detach();
		Assert.False(replaced.IsCurrentRuntime);

		LuaRuntime.Attach(scope.Binding);
		EngineResourceOrigin reattached = EngineResourceOrigin.CaptureRuntime();
		Assert.Equal(replaced.Runtime.AttachEpoch + 1, reattached.Runtime.AttachEpoch);
		Assert.False(replaced.IsCurrentRuntime);
		Assert.True(reattached.IsCurrentRuntime);
	}
}

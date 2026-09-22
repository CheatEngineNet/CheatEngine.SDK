using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>The handle as a value, without any Lua.</summary>
public sealed class CEObjectValueTests
{
	[Fact]
	public void A_handle_is_its_pointer()
	{
		CEObject a = new(0x1234);
		CEObject b = new(0x1234);
		CEObject c = new(0x5678);

		Assert.Equal(0x1234, a.Value);
		Assert.False(a.IsNull);
		Assert.True(a == b);
		Assert.False(a != b);
		Assert.True(a != c);
		Assert.True(a.Equals(b));
		Assert.True(a.Equals((object) b));
		Assert.False(a.Equals(null));
		Assert.False(a.Equals(c));
		Assert.Equal(a.GetHashCode(), b.GetHashCode());
		Assert.Equal("CEObject@0x1234", a.ToString());
	}

	[Fact]
	public void The_default_handle_is_null()
	{
		Assert.True(default(CEObject).IsNull);
		Assert.True(CEObject.Null.IsNull);
		Assert.Equal(0, CEObject.Null.Value);
		Assert.Equal(default, CEObject.Null);
		Assert.Equal("CEObject(null)", CEObject.Null.ToString());
		Assert.Equal(CEObject.Null, new CEObject(0));
	}

	[Fact]
	public void The_handle_is_its_own_typed_handle()
	{
		CEObject handle = new(0x42);
		Assert.Equal(handle, handle.Handle);
		Assert.Equal(handle, CEObject.FromHandle(handle));
		Assert.Equal(handle, Wrap<CEObject>(handle));
		Assert.True(Wrap<CEObject>(CEObject.Null).IsNull);
	}

	[Fact]
	public void Members_that_reach_the_host_fail_cleanly_while_detached()
	{
		LuaRuntime.Detach();
		CEObject handle = new(0x42);

		Assert.Throws<InvalidOperationException>(() => handle.TryGetProperty<Int32Marshaller, int>("Count"u8, out _));
		Assert.Throws<InvalidOperationException>(() => handle.TrySetProperty<Int32Marshaller, int>("Count"u8, 1));
		Assert.Throws<InvalidOperationException>(() => handle.TryCallMethod("destroy"u8));
		Assert.Throws<InvalidOperationException>(() => handle.TryCallMethod<Int32Marshaller, int>("getCount"u8, out _));
	}

	private static T Wrap<T>(CEObject handle)
		where T : struct, ICEObject<T>
	{
		return T.FromHandle(handle);
	}
}

using System.Globalization;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>
///     The handle primitives against the fake host: userdata decoding, property and indexed access, bound-method calls,
///     and every failure path as a status rather than a crash.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class CEObjectTests
{
	[Fact]
	public void TryRead_decodes_a_full_userdata_whose_first_field_is_the_object_pointer()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);

		CEObject probe = FakeHost.CreateObject(L, "Probe");
		probe.Push(L);
		Assert.True(L.IsUserdata(-1));
		Assert.True(CEObject.TryRead(L, -1, out CEObject fromPusher));
		Assert.Equal(probe, fromPusher);

		// A userdata made by hand with the same layout, larger than a pointer.
		IntPtr block = L.NewUserdata(32);
		*(nint*) block = 0x7777_0000;
		Assert.True(CEObject.TryRead(L, -1, out CEObject fromBlock));
		Assert.Equal(new CEObject(0x7777_0000), fromBlock);
		Assert.Equal(frame.Top + 2, L.Top);
	}

	[Fact]
	public void TryRead_refuses_values_that_are_not_host_objects()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = EngineTest.View(state);
		using LuaFrame frame = new(L);

		L.PushLightUserdata(0x1234); // 1: a bare pointer, no block to read
		_ = L.NewUserdata(1); // 2: a block smaller than a pointer
		IntPtr zeroed = L.NewUserdata((nuint) sizeof(nint));
		*(nint*) zeroed = 0; // 3: a null first field
		L.CreateTable(); // 4
		L.PushInteger(0x1234); // 5
		L.PushString("0x1234"u8); // 6
		L.PushNil(); // 7

		for (int index = 1; index <= 8; index++)
		{
			Assert.False(CEObject.TryRead(L, index, out CEObject value),
				"index " + index.ToString(CultureInfo.InvariantCulture) + " was read as an object");
			Assert.True(value.IsNull);
		}

		Assert.Equal(7, L.Top);
	}

	[Fact]
	public void Push_goes_through_the_host_pusher_and_a_null_handle_pushes_nil()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);

		CEObject probe = FakeHost.CreateObject(L, "Probe");
		int pushes = FakeHost.PusherCalls;
		probe.Push(L);
		CEObject.Push(L, probe);
		CEObject.Null.Push(L);

		Assert.Equal(pushes + 2, FakeHost.PusherCalls);
		Assert.Equal(LuaType.Userdata, L.TypeOf(-3));
		Assert.Equal(LuaType.Userdata, L.TypeOf(-2));
		Assert.True(L.IsNil(-1));
		Assert.True(CEObject.TryRead(L, -2, out CEObject back));
		Assert.Equal(probe, back);
	}

	[Fact]
	public void Push_without_a_pusher_in_the_binding_throws()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state, false);
		LuaState L = scope.State;

		CEObject probe = FakeHost.CreateObject(L, "Probe");
		Assert.Throws<InvalidOperationException>(() => probe.Push(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryGetProperty_leaves_only_the_value_on_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 3; o.props.Name = 'probe'");

		Assert.True(probe.TryGetProperty(L, "Count"u8).IsOk);
		Assert.Equal(3, EngineTest.ReadInteger(L, -1));
		Assert.Equal(frame.Top + 1, L.Top);

		Assert.True(probe.TryGetProperty(L, "Name"u8).IsOk);
		Assert.Equal("probe", EngineTest.ReadString(L, -1));

		Assert.True(probe.TryGetProperty(L, "Missing"u8).IsOk);
		Assert.True(L.IsNil(-1));
		Assert.Equal(frame.Top + 3, L.Top);
	}

	[Fact]
	public void TrySetProperty_consumes_the_value_and_the_object_sees_it()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 3");

		L.PushInteger(7);
		Assert.True(probe.TrySetProperty(L, "Count"u8).IsOk);
		Assert.Equal(frame.Top, L.Top);

		Assert.True(probe.TryGetProperty(L, "Count"u8).IsOk);
		Assert.Equal(7, EngineTest.ReadInteger(L, -1));
	}

	[Fact]
	public void A_raising_getter_or_setter_is_a_status_with_one_error_value()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(
			L,
			"Probe",
			"o.getters.Bad = function() error('the getter raised') end; o.setters.Locked = function() error('the setter raised') end");

		LuaStatus status = probe.TryGetProperty(L, "Bad"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("the getter raised", EngineTest.ErrorMessage(L, status), StringComparison.Ordinal);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);

		L.PushInteger(1);
		status = probe.TrySetProperty(L, "Locked"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("the setter raised", EngineTest.ErrorMessage(L, status), StringComparison.Ordinal);
		Assert.Equal(frame.Top + 1, L.Top);
	}

	[Fact]
	public void Indexed_access_passes_the_zero_based_index_through_unchanged()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.items = { 'first', 'second' }");

		Assert.True(probe.TryGetIndex(L, 0).IsOk);
		Assert.Equal("first", EngineTest.ReadString(L, -1));
		Assert.True(probe.TryGetIndex(L, 1).IsOk);
		Assert.Equal("second", EngineTest.ReadString(L, -1));
		Assert.True(probe.TryGetIndex(L, 2).IsOk);
		Assert.True(L.IsNil(-1));
		L.SetTop(frame.Top);

		L.PushString("third"u8);
		Assert.True(probe.TrySetIndex(L, 2).IsOk);
		Assert.Equal(frame.Top, L.Top);
		Assert.True(probe.TryGetIndex(L, 2).IsOk);
		Assert.Equal("third", EngineTest.ReadString(L, -1));
	}

	[Fact]
	public void TryPushMethod_pushes_an_instance_bound_function_that_is_called_without_self()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 5");

		Assert.True(probe.TryPushMethod(L, "add"u8).IsOk);
		Assert.True(L.IsFunction(-1));
		L.PushInteger(2);
		L.PushInteger(3);
		Assert.True(L.TryCall(2, 1).IsOk);
		Assert.Equal(5, EngineTest.ReadInteger(L, -1));
		Assert.Equal(frame.Top + 1, L.Top);

		Assert.True(probe.TryPushMethod(L, "getClassName"u8).IsOk);
		Assert.True(L.TryCall(0, 1).IsOk);
		Assert.Equal("Probe", EngineTest.ReadString(L, -1));
	}

	[Fact]
	public void TryPushMethod_reports_a_member_that_is_not_a_function_by_name_and_type()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe");

		LuaStatus status = probe.TryPushMethod(L, "notAMethod"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal("'notAMethod' is a number, not a method", EngineTest.ErrorMessage(L, status));
		Assert.Equal(frame.Top + 1, L.Top);

		status = probe.TryPushMethod(L, "missing"u8);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal("'missing' is a nil, not a method", EngineTest.ErrorMessage(L, status));
		Assert.Equal(frame.Top + 2, L.Top);
	}

	[Fact]
	public void TryCallMethod_replaces_the_arguments_by_the_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 9");

		L.PushInteger(40);
		L.PushInteger(2);
		Assert.True(probe.TryCallMethod(L, "add"u8, 2, 1).IsOk);
		Assert.Equal(42, EngineTest.ReadInteger(L, -1));
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);

		L.PushInteger(1);
		L.PushString("two"u8);
		L.PushBoolean(true);
		Assert.True(probe.TryCallMethod(L, "echo"u8, 3, LuaState.MultipleResults).IsOk);
		Assert.Equal(frame.Top + 3, L.Top);
		Assert.Equal(1, EngineTest.ReadInteger(L, -3));
		Assert.Equal("two", EngineTest.ReadString(L, -2));
		Assert.True(L.ToBoolean(-1));
		L.SetTop(frame.Top);

		Assert.True(probe.TryCallMethod(L, "getCount"u8, 0, 1).IsOk);
		Assert.Equal(9, EngineTest.ReadInteger(L, -1));
		Assert.True(probe.TryCallMethod(L, "getCount"u8, 0, 0).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
	}

	[Fact]
	public void TryCallMethod_replaces_the_arguments_by_one_error_value_on_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe");

		L.PushInteger(1);
		L.PushInteger(2);
		LuaStatus status = probe.TryCallMethod(L, "raise"u8, 2, 1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Contains("raised by the host object", EngineTest.ErrorMessage(L, status), StringComparison.Ordinal);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);

		L.PushInteger(1);
		status = probe.TryCallMethod(L, "missing"u8, 1, 1);
		Assert.Equal(LuaStatus.RuntimeError, status);
		Assert.Equal("'missing' is a nil, not a method", EngineTest.ErrorMessage(L, status));
		Assert.Equal(frame.Top + 1, L.Top);
	}

	[Fact]
	public void TryCallMethod_refuses_a_negative_argument_count_before_touching_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject probe = FakeHost.CreateObject(L, "Probe");

		Assert.Throws<ArgumentOutOfRangeException>(() => probe.TryCallMethod(L, "add"u8, -1, 0));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void A_null_handle_fails_every_stack_member_with_a_status()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);

		Assert.False(CEObject.Null.TryGetProperty(L, "Count"u8).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);
		L.PushInteger(1);
		Assert.False(CEObject.Null.TrySetProperty(L, "Count"u8).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);
		Assert.False(CEObject.Null.TryGetIndex(L, 0).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);
		Assert.False(CEObject.Null.TryPushMethod(L, "destroy"u8).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
		L.Pop(1);
		L.PushInteger(1);
		Assert.False(CEObject.Null.TryCallMethod(L, "add"u8, 1, 1).IsOk);
		Assert.Equal(frame.Top + 1, L.Top);
	}

	[Fact]
	public void Typed_members_acquire_the_state_read_through_the_marshaller_and_restore_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject probe = FakeHost.CreateObject(
			L,
			"Probe",
			"o.props.Count = 3; o.props.Name = 'probe'; o.props.Result = 0x400000; o.props.Text = '00401000'; o.getters.Bad = function() error('x') end");

		Assert.True(probe.TryGetProperty<Int32Marshaller, int>("Count"u8, out int count));
		Assert.Equal(3, count);
		Assert.True(probe.TryGetProperty<StringMarshaller, string>("Name"u8, out string? name));
		Assert.Equal("probe", name);
		Assert.True(probe.TryGetProperty<Address, Address>("Result"u8, out Address number));
		Assert.Equal(0x400000UL, number.Value);
		Assert.True(probe.TryGetProperty<Address, Address>("Text"u8, out Address text));
		Assert.Equal(0x401000UL, text.Value);
		Assert.True(
			probe.TryGetProperty<EnumMarshaller<VariableType>, VariableType>("Count"u8, out VariableType asEnum));
		Assert.Equal(VariableType.Qword, asEnum);

		Assert.False(probe.TryGetProperty<Int32Marshaller, int>("Name"u8, out int notAnInteger));
		Assert.Equal(0, notAnInteger);
		Assert.False(probe.TryGetProperty<Int32Marshaller, int>("Missing"u8, out _));
		Assert.False(probe.TryGetProperty<Int32Marshaller, int>("Bad"u8, out _));
		Assert.False(probe.TryGetProperty<StringMarshaller, string>("Missing"u8, out string? missing));
		Assert.Null(missing);

		Assert.True(probe.TrySetProperty<Int32Marshaller, int>("Count"u8, 11));
		Assert.True(probe.TrySetProperty<Utf8Marshaller, ReadOnlySpan<byte>>("Name"u8, "renamed"u8));
		Assert.True(probe.TrySetProperty<Address, Address>("Result"u8, 0x500000));
		Assert.True(probe.TryGetProperty<Int32Marshaller, int>("Count"u8, out count));
		Assert.Equal(11, count);
		Assert.True(probe.TryGetProperty<StringMarshaller, string>("Name"u8, out name));
		Assert.Equal("renamed", name);
		Assert.True(probe.TryGetProperty<Address, Address>("Result"u8, out number));
		Assert.Equal(0x500000UL, number.Value);

		Assert.True(probe.TryCallMethod<Int32Marshaller, int>("getCount"u8, out int viaMethod));
		Assert.Equal(11, viaMethod);
		Assert.True(probe.TryCallMethod("getCount"u8));
		Assert.False(probe.TryCallMethod("raise"u8));
		Assert.False(probe.TryCallMethod("notAMethod"u8));
		Assert.False(probe.TryCallMethod<Int32Marshaller, int>("getClassName"u8, out _));
		Assert.False(probe.TryCallMethod<Int32Marshaller, int>("raise"u8, out _));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Typed_members_restore_the_exact_stack_when_a_consumer_marshaller_throws()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject probe = FakeHost.CreateObject(L, "Probe", "o.props.Count = 3");
		L.PushInteger(0x1234);
		int top = L.Top;

		Assert.Throws<InvalidOperationException>(() => probe.TrySetProperty<ThrowingPushMarshaller, int>("Count"u8, 4));
		Assert.Equal(top, L.Top);
		Assert.Equal(0x1234, EngineTest.ReadInteger(L, -1));

		Assert.Throws<InvalidOperationException>(() =>
			probe.TryGetProperty<ThrowingReadMarshaller, int>("Count"u8, out _));
		Assert.Equal(top, L.Top);
		Assert.Equal(0x1234, EngineTest.ReadInteger(L, -1));

		Assert.Throws<InvalidOperationException>(() =>
			probe.TryCallMethod<ThrowingReadMarshaller, int>("getCount"u8, out _));
		Assert.Equal(top, L.Top);
		Assert.Equal(0x1234, EngineTest.ReadInteger(L, -1));
	}

	[Fact]
	public void An_object_travels_as_an_argument_and_comes_back_as_the_same_handle()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject probe = FakeHost.CreateObject(L, "Probe");
		CEObject other = FakeHost.CreateObject(L, "Probe");

		using (LuaFrame frame = new(L))
		{
			other.Push(L);
			Assert.True(probe.TryCallMethod(L, "setOther"u8, 1, 0).IsOk);
			Assert.Equal(frame.Top, L.Top);
		}

		Assert.True(probe.TryGetProperty<CEObject, CEObject>("Other"u8, out CEObject back));
		Assert.Equal(other, back);
		Assert.NotEqual(probe, back);
		Assert.False(probe.TryGetProperty<CEObject, CEObject>("Missing"u8, out CEObject none));
		Assert.True(none.IsNull);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void A_method_returning_hexadecimal_text_is_read_by_the_address_reader_with_a_zero_based_index()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		CEObject list = FakeHost.CreateObject(L, "Probe", "o.addresses = { 0x400000, 0x7FF6A1B2C3D4 }");

		L.PushInteger(0);
		Assert.True(list.TryCallMethod(L, "getAddress"u8, 1, 1).IsOk);
		Assert.Equal("00400000", EngineTest.ReadString(L, -1));
		Assert.True(Address.TryRead(L, -1, out Address first));
		Assert.Equal(0x400000UL, first.Value);

		L.PushInteger(1);
		Assert.True(list.TryCallMethod(L, "getAddressNumber"u8, 1, 1).IsOk);
		Assert.True(L.IsInteger(-1));
		Assert.True(Address.TryRead(L, -1, out Address second));
		Assert.Equal(0x7FF6A1B2C3D4UL, second.Value);

		L.PushInteger(2);
		Assert.False(list.TryCallMethod(L, "getAddress"u8, 1, 1).IsOk);
		Assert.Equal(frame.Top + 3, L.Top);
	}

	private readonly struct ThrowingPushMarshaller : ILuaMarshaller<int>
	{
		public static void Push(LuaState state, int value)
		{
			state.PushInteger(value);
			throw new InvalidOperationException("The test marshaller failed after pushing a partial value.");
		}

		public static bool TryRead(LuaState state, int index, out int value)
		{
			value = default;
			return false;
		}
	}

	private readonly struct ThrowingReadMarshaller : ILuaMarshaller<int>
	{
		public static void Push(LuaState state, int value)
		{
			state.PushInteger(value);
		}

		public static bool TryRead(LuaState state, int index, out int value)
		{
			state.PushInteger(0x5678);
			value = default;
			throw new InvalidOperationException("The test marshaller failed after creating a partial stack value.");
		}
	}
}

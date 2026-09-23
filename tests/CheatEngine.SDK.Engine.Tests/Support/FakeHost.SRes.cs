using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Support;

/// <summary>
///     The S-RES part of the fake host: a controlled Lua state replacement, a Cheat Engine <c>SymbolList</c> class
///     model, a qualified local target, host-object globals, and a Lua-callable hook that re-enters managed code from
///     inside a host call (for the table-load guard).
/// </summary>
internal static unsafe partial class FakeHost
{
	private static readonly Lazy<Action> SReplaceStateGeneration = new(CreateStateReplacement);
	private static Action? s_reentrantHook;
	private static Exception? s_reentrantHookFailure;

	/// <summary>
	///     The Lua side of the <c>SymbolList</c> class model. A list keeps its symbols by search key, counts register,
	///     unregister and destroy calls in globals, and raises on every member once it was destroyed, as Cheat Engine does
	///     for a detached userdata.
	/// </summary>
	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification = "Lua model data kept in one literal, like the base model of FakeHost.")]
	private static ReadOnlySpan<byte> SymbolListModel => """
	                                                     local host = ...
	                                                     symbol_list_register_calls = 0
	                                                     symbol_list_unregister_calls = 0
	                                                     symbol_list_destroyed_while_registered = 0
	                                                     local function guard(o)
	                                                       if o.destroyed then error("attempt to index a userdata value") end
	                                                     end
	                                                     local function copy(s)
	                                                       return { modulename = s.modulename, searchkey = s.searchkey, address = s.address, symbolsize = s.symbolsize }
	                                                     end
	                                                     host.classes.SymbolList = setmetatable({
	                                                       clear = function(o) guard(o) o.symbols = {} end,
	                                                       addSymbol = function(o, ...)
	                                                         guard(o)
	                                                         o.last_args = table.pack(...)
	                                                         local m, k, a, s = ...
	                                                         o.symbols[k] = { modulename = m, searchkey = k, address = a, symbolsize = s }
	                                                       end,
	                                                       deleteSymbol = function(o, ...)
	                                                         guard(o)
	                                                         o.last_args = table.pack(...)
	                                                         local key = ...
	                                                         if type(key) == "number" then
	                                                           for name, s in pairs(o.symbols) do
	                                                             if s.address == key then o.symbols[name] = nil end
	                                                           end
	                                                         else
	                                                           o.symbols[key] = nil
	                                                         end
	                                                       end,
	                                                       getSymbolFromAddress = function(o, ...)
	                                                         guard(o)
	                                                         o.last_args = table.pack(...)
	                                                         if o.malformed then return { modulename = 1 } end
	                                                         local address = ...
	                                                         for _, s in pairs(o.symbols) do
	                                                           if address >= s.address and address < s.address + s.symbolsize then return copy(s) end
	                                                         end
	                                                         return nil
	                                                       end,
	                                                       getSymbolFromString = function(o, ...)
	                                                         guard(o)
	                                                         o.last_args = table.pack(...)
	                                                         if o.malformed then return { modulename = "m", searchkey = "k", address = "not an address", symbolsize = 1 } end
	                                                         local s = o.symbols[(...)]
	                                                         if s == nil then return nil end
	                                                         return copy(s)
	                                                       end,
	                                                       register = function(o, ...)
	                                                         guard(o)
	                                                         o.register_args = select("#", ...)
	                                                         symbol_list_register_calls = symbol_list_register_calls + 1
	                                                         if o.register_raises then o.registered = true error("register failed after it began") end
	                                                         o.registered = true
	                                                       end,
	                                                       unregister = function(o, ...)
	                                                         guard(o)
	                                                         o.unregister_args = select("#", ...)
	                                                         symbol_list_unregister_calls = symbol_list_unregister_calls + 1
	                                                         if o.unregister_raises then error("unregister failed after it began") end
	                                                         o.registered = false
	                                                       end,
	                                                       destroy = function(o)
	                                                         if o.destroyed then error("object already destroyed") end
	                                                         if o.registered then symbol_list_destroyed_while_registered = symbol_list_destroyed_while_registered + 1 end
	                                                         o.destroyed = true
	                                                         host.destroyed = host.destroyed + 1
	                                                       end,
	                                                     }, { __index = host.classes.Object })
	                                                     """u8;

	/// <summary>
	///     Runs the SDK-controlled Lua state replacement without replacing the fixture state: calls the internal
	///     <c>LuaRuntime.BeginStateReset()</c> and disposes the returned transition at once, which advances
	///     <see cref="LuaRuntime.StateGeneration" /> and neutralizes callbacks exactly like a supported reset.
	/// </summary>
	/// <remarks>
	///     Depends on the internal member <c>CheatEngine.SDK.Lua.Runtime.LuaRuntime.BeginStateReset()</c> returning the
	///     stack-only <c>LuaStateResetTransition</c> with a public <c>Dispose()</c>. Reflection cannot invoke a method that
	///     returns a by-ref-like type, so a dynamic method calls it and disposes the transition immediately; nothing runs in
	///     between, so the transition is never left open. A change of that internal protocol (S-HOST) must update this
	///     helper.
	/// </remarks>
	public static void ReplaceStateGeneration()
	{
		SReplaceStateGeneration.Value();
	}

	/// <summary>Adds the <c>SymbolList</c> class to the installed model, so <see cref="CreateObject" /> accepts it.</summary>
	public static void InstallSymbolListClass(LuaState L)
	{
		using LuaFrame frame = new(L);
		LuaStatus status = L.TryLoad(SymbolListModel, "=fakehost-symbollist"u8);
		FailIfNotOk(L, status, "loading the SymbolList model");
		Assert.Equal(LuaType.Table, L.RawGetPointer(LuaState.RegistryIndex, s_keys + HostKey));
		FailIfNotOk(L, L.TryCall(1, 0), "installing the SymbolList model");
	}

	/// <summary>Creates a fake <c>SymbolList</c> object with an empty symbol table.</summary>
	public static CEObject CreateSymbolList(LuaState L, string initializer = "")
	{
		return CreateObject(L, "SymbolList", "o.symbols = {}\n" + initializer);
	}

	/// <summary>Assigns a host object to a Lua global (the userdata the host pusher creates).</summary>
	public static void SetGlobalObject(LuaState L, string name, CEObject value)
	{
		using LuaFrame frame = new(L);
		value.Push(L);
		FailIfNotOk(L, L.TrySetGlobal(Encoding.UTF8.GetBytes(name)), "setting the global " + name);
	}

	/// <summary>
	///     Runs Lua against the fake object table of <paramref name="value" /> (bound to <c>o</c>): for example
	///     <c>o.destroyed = true</c> to make the host destroy the object behind the plugin's back.
	/// </summary>
	public static void RunOnObject(LuaState L, CEObject value, string source)
	{
		using LuaFrame frame = new(L);
		byte[] chunk = Encoding.UTF8.GetBytes("local o = ...\n" + source);
		FailIfNotOk(L, L.TryLoad(chunk, "=fakehost-object"u8), "loading an object chunk");
		Assert.Equal(LuaType.Table, L.RawGetPointer(LuaState.RegistryIndex, s_keys + ObjectsKey));
		Assert.Equal(LuaType.Table, L.RawGetPointer(-1, value.Value));
		L.Remove(-2);
		FailIfNotOk(L, L.TryCall(1, 0), "running an object chunk");
	}

	/// <summary>
	///     Defines <c>getOpenedProcessID</c> returning this test process (so the SDK can qualify a local incarnation from
	///     its creation time) and <c>isConnectedToCEServer</c> returning <see langword="false" /> (a qualified local target,
	///     shared-contracts section 6).
	/// </summary>
	public static void InstallQualifiedLocalTarget(LuaState L)
	{
		EngineTest.Run(L, Encoding.UTF8.GetBytes(
			"fake_target_pid = " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n" +
			"function getOpenedProcessID() return fake_target_pid end\n" +
			"function isConnectedToCEServer() return false end"));
	}

	/// <summary>
	///     Installs a Lua global C function that calls <paramref name="hook" /> synchronously on the calling thread, from
	///     inside whatever Lua host call invokes it. A managed exception is caught (it must never cross the C boundary) and
	///     reported by <see cref="TakeReentrantHookFailure" />.
	/// </summary>
	public static IDisposable InstallReentrantHook(LuaState L, string globalName, Action hook)
	{
		s_reentrantHook = hook;
		s_reentrantHookFailure = null;
		using LuaFrame frame = new(L);
		L.PushUncheckedFunction(
			new LuaNativeFunction((nint) (delegate* unmanaged[Cdecl]<lua_State*, int>) &InvokeReentrantHook));
		FailIfNotOk(L, L.TrySetGlobal(Encoding.UTF8.GetBytes(globalName)), "installing the re-entrant hook");
		return new ReentrantHookScope();
	}

	/// <summary>Returns and clears the exception the re-entrant hook threw, if any.</summary>
	public static Exception? TakeReentrantHookFailure()
	{
		Exception? failure = s_reentrantHookFailure;
		s_reentrantHookFailure = null;
		return failure;
	}

	private static Action CreateStateReplacement()
	{
		MethodInfo begin = typeof(LuaRuntime).GetMethod("BeginStateReset", BindingFlags.Static | BindingFlags.NonPublic,
			                   Type.EmptyTypes)
		                   ?? throw new InvalidOperationException("LuaRuntime.BeginStateReset() was not found.");
		Type transition = begin.ReturnType;
		MethodInfo dispose = transition.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public,
			                     Type.EmptyTypes)
		                     ?? throw new InvalidOperationException("LuaStateResetTransition.Dispose() was not found.");
		DynamicMethod method = new("ReplaceStateGeneration", null, Type.EmptyTypes, typeof(FakeHost).Module, true);
		ILGenerator il = method.GetILGenerator();
		LocalBuilder local = il.DeclareLocal(transition);
		il.Emit(OpCodes.Call, begin);
		il.Emit(OpCodes.Stloc, local);
		il.Emit(OpCodes.Ldloca, local);
		il.Emit(OpCodes.Call, dispose);
		il.Emit(OpCodes.Ret);
		return method.CreateDelegate<Action>();
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int InvokeReentrantHook(lua_State* state)
	{
		try
		{
			s_reentrantHook?.Invoke();
		}
		catch (Exception exception)
		{
			s_reentrantHookFailure = exception;
		}

		return 0;
	}

	private sealed class ReentrantHookScope : IDisposable
	{
		public void Dispose()
		{
			s_reentrantHook = null;
		}
	}
}

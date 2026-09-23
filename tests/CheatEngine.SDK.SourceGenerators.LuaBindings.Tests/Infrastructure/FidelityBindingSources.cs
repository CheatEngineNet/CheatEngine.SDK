namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>
///     The declarations and stand-in globals of the qualification suites Q20 (string and byte fidelity), Q21 (integer
///     boundaries) and Q22 (<c>nil</c>, <c>false</c>, zero, zero results and Lua errors): compiled warning-free against
///     the real SDK assemblies and executed on the bundled Lua fixture. Kept apart from <see cref="BindingSources" /> so
///     that the pinned outputs of the existing shapes stay untouched.
/// </summary>
internal static class FidelityBindingSources
{
	/// <summary>Every Q22 form: Try, Outcome and Throwing over integer, boolean, string, optional and custom results.</summary>
	public const string Q22Suite = """
	                               using System;
	                               using System.Diagnostics.CodeAnalysis;
	                               using CheatEngine.SDK.Annotations.Lua;
	                               using CheatEngine.SDK.Lua.Calls;
	                               using CheatEngine.SDK.Lua.Marshalling;
	                               using CheatEngine.SDK.Lua.State;

	                               namespace Demo;

	                               public readonly struct EvenMarshaller : ILuaMarshaller<long>
	                               {
	                                   public static void Push(LuaState state, long value) => state.PushInteger(value);

	                                   public static bool TryRead(LuaState state, int index, out long value)
	                                   {
	                                       if (!state.TryReadInteger(index, out value))
	                                       {
	                                           return false;
	                                       }

	                                       if (value < 0)
	                                       {
	                                           throw new InvalidOperationException("negative token");
	                                       }

	                                       if (value % 2 != 0)
	                                       {
	                                           value = 0;
	                                           return false;
	                                       }

	                                       return true;
	                                   }
	                               }

	                               public static partial class Q22
	                               {
	                                   [LuaGlobal("q22")]
	                                   public static partial bool TryInt32(string kind, out int value);

	                                   [LuaGlobal("q22")]
	                                   public static partial bool TryBoolean(string kind, out bool value);

	                                   [LuaGlobal("q22")]
	                                   public static partial bool TryText(string kind, [MaybeNullWhen(false)] out string value);

	                                   [LuaGlobal("q22")]
	                                   public static partial bool TryTextCopy(string kind, Span<byte> destination, out int written);

	                                   [LuaGlobal("q22")]
	                                   public static partial LuaOperationStatus OutcomeInt32(string kind, out int value);

	                                   [LuaGlobal("q22")]
	                                   public static partial LuaOperationStatus OutcomeOptional(string kind, out LuaOptional<int> value);

	                                   [LuaGlobal("q22")]
	                                   public static partial int Int32(string kind);

	                                   [LuaGlobal("q22")]
	                                   public static partial bool Boolean(string kind);

	                                   [LuaGlobal("q22")]
	                                   public static partial string Text(string kind);

	                                   [LuaGlobal("q22")]
	                                   public static partial bool TryEven(string kind, [LuaMarshaller(typeof(EvenMarshaller))] out long value);

	                                   [LuaGlobal("q22")]
	                                   public static partial LuaOperationStatus OutcomeEven(string kind, [LuaMarshaller(typeof(EvenMarshaller))] out long value);

	                                   [LuaGlobal("q22")]
	                                   [return: LuaMarshaller(typeof(EvenMarshaller))]
	                                   public static partial long Even(string kind);
	                               }
	                               """;

	/// <summary>Integer, 64-bit and address results in every form, and the same kinds as exported function arguments.</summary>
	public const string NumericSuite = """
	                                   using CheatEngine.SDK.Annotations.Lua;
	                                   using CheatEngine.SDK.Lua.Calls;

	                                   namespace Demo;

	                                   public static partial class Numbers
	                                   {
	                                       [LuaGlobal("boundary")]
	                                       public static partial bool TryInt32(string expression, out int value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial bool TryInt64(string expression, out long value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial bool TryAddress(string expression, out nuint value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial LuaOperationStatus OutcomeInt32(string expression, out int value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial LuaOperationStatus OutcomeInt64(string expression, out long value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial LuaOperationStatus OutcomeAddress(string expression, out nuint value);

	                                       [LuaGlobal("boundary")]
	                                       public static partial int Int32(string expression);

	                                       [LuaGlobal("boundary")]
	                                       public static partial long Int64(string expression);

	                                       [LuaGlobal("boundary")]
	                                       public static partial nuint Address(string expression);

	                                       [LuaFunction("n_int")]
	                                       public static int EchoInt32(int value) => value;

	                                       [LuaFunction("n_long")]
	                                       public static long EchoInt64(long value) => value;

	                                       [LuaFunction("n_addr")]
	                                       public static nuint EchoAddress(nuint value) => value;
	                                   }
	                                   """;

	/// <summary>String and UTF-8 arguments and results in every shape, and a nullable string function argument.</summary>
	public const string StringSuite = """
	                                  using System;
	                                  using System.Diagnostics.CodeAnalysis;
	                                  using CheatEngine.SDK.Annotations.Lua;

	                                  namespace Demo;

	                                  public static partial class Strings
	                                  {
	                                      [LuaGlobal("s_value")]
	                                      public static partial bool TryText(string key, [MaybeNullWhen(false)] out string value);

	                                      [LuaGlobal("s_value")]
	                                      public static partial bool TryTextCopy(string key, Span<byte> destination, out int written);

	                                      [LuaGlobal("s_value")]
	                                      public static partial string Text(string key);

	                                      [LuaGlobal("s_len")]
	                                      public static partial long Utf8Length(ReadOnlySpan<byte> text);

	                                      [LuaGlobal("s_len")]
	                                      public static partial long TextLength(string text);

	                                      [LuaGlobal("s_echo")]
	                                      public static partial string Echo(string text);

	                                      [LuaGlobal("s_echo")]
	                                      public static partial bool TryEchoCopy(ReadOnlySpan<byte> text, Span<byte> destination, out int written);

	                                      [LuaGlobal("s_type")]
	                                      public static partial string TypeOf(string? text);

	                                      [LuaFunction("f_describe")]
	                                      public static string Describe(string? text) => text is null ? "null" : "text:" + text.Length;
	                                  }
	                                  """;

	/// <summary>
	///     <c>q22(kind)</c> returns one shape per kind: <c>nil</c>, <c>false</c>, <c>true</c>, 0, 42, 7 (odd), -2
	///     (negative), <c>''</c>, <c>{}</c>, no value at all, a string error and a table error whose <c>__tostring</c>
	///     raises.
	/// </summary>
	public static ReadOnlySpan<byte> Q22StandIns => """
	                                                local raising = setmetatable({}, { __tostring = function() error('tostring boom') end })
	                                                local cases = {
	                                                  ['nil'] = function() return nil end,
	                                                  ['false'] = function() return false end,
	                                                  ['true'] = function() return true end,
	                                                  zero = function() return 0 end,
	                                                  value = function() return 42 end,
	                                                  odd = function() return 7 end,
	                                                  negative = function() return -2 end,
	                                                  empty = function() return '' end,
	                                                  table = function() return {} end,
	                                                  none = function() end,
	                                                  raise = function() error('boom') end,
	                                                  raise_table = function() error(raising) end,
	                                                }
	                                                function q22(kind) return cases[kind]() end
	                                                """u8;

	/// <summary><c>boundary(expression)</c> evaluates a Lua expression, so each row is a value Lua itself produced.</summary>
	public static ReadOnlySpan<byte> NumericStandIns => """
	                                                    function boundary(expression) return assert(load('return ' .. expression))() end
	                                                    """u8;

	/// <summary>
	///     <c>s_value(key)</c> returns a string with embedded NULs, multibyte UTF-8, invalid UTF-8 or the empty string;
	///     <c>s_len</c> returns the byte length Lua received, <c>s_echo</c> the string itself, <c>s_type</c> its type.
	/// </summary>
	public static ReadOnlySpan<byte> StringStandIns => """
	                                                   local values = { nul = 'a\0b\0', multibyte = '\u{E9}\u{6F22}\u{1F600}', invalid = '\255\254A', empty = '' }
	                                                   function s_value(key) return values[key] end
	                                                   function s_len(s) return #s end
	                                                   function s_echo(s) return s end
	                                                   function s_type(s) return type(s) end
	                                                   """u8;
}

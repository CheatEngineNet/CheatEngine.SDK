namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>
///     Declarations with <c>LuaOptional&lt;T&gt;</c> arguments, optional and variadic results (audit A19-07, A19-08,
///     F10): compiled warning-free against the real SDK assemblies and executed by the EndToEnd suites. Kept apart from
///     <see cref="BindingSources" /> so that the pinned outputs of the existing shapes stay untouched.
/// </summary>
internal static class OptionalBindingSources
{
	/// <summary>The stand-in globals every optional suite runs against.</summary>
	public static ReadOnlySpan<byte> StandIns => """
	                                             calls = 0
	                                             function arity(...) calls = calls + 1 return select('#', ...) end
	                                             function kinds(...)
	                                               local t = table.pack(...)
	                                               local out = {}
	                                               for i = 1, t.n do out[#out + 1] = type(t[i]) end
	                                               return table.concat(out, ',')
	                                             end
	                                             function readBytesLike(...)
	                                               local n = select('#', ...)
	                                               local address, count, asTable = ...
	                                               if n == 1 then return 'one byte' end
	                                               if n == 2 then return 'values' end
	                                               if asTable == nil then return 'nil flag' end
	                                               if asTable then return 'table' end
	                                               return 'values'
	                                             end
	                                             function shape(mode)
	                                               if mode == 0 then return end
	                                               if mode == 1 then return nil end
	                                               if mode == 2 then return 7 end
	                                               if mode == 3 then return 7, 8 end
	                                               if mode == 4 then return true, 'warn' end
	                                               if mode == 5 then return 1970354901756285, {} end
	                                               if mode == 6 then return 'text' end
	                                               if mode == 7 then return 7, 'x' end
	                                               if mode == 8 then return 7, nil end
	                                               error('boom')
	                                             end
	                                             function seq(n, bad)
	                                               if bad == 1 then return 1, nil, 3 end
	                                               if bad == 2 then return 1, 'x', 3 end
	                                               if n < 0 then error('negative') end
	                                               local t = {}
	                                               for i = 1, n do t[i] = i * 10 end
	                                               return table.unpack(t, 1, n)
	                                             end
	                                             """u8;

	/// <summary>Every supported optional argument, optional result and variadic shape of a bound global.</summary>
	public const string GlobalSuite = """
	                                  using System;
	                                  using System.Diagnostics.CodeAnalysis;
	                                  using CheatEngine.SDK.Annotations.Lua;
	                                  using CheatEngine.SDK.Lua.Calls;
	                                  using CheatEngine.SDK.Lua.Marshalling;
	                                  using CheatEngine.SDK.Lua.State;

	                                  namespace Demo;

	                                  public static partial class Optionals
	                                  {
	                                      [LuaGlobal("arity")]
	                                      public static partial long Arity(long first, LuaOptional<long> second, LuaOptional<string> third);

	                                      [LuaGlobal("kinds")]
	                                      public static partial string Kinds(long first, LuaOptional<long> second);

	                                      [LuaGlobal("arity")]
	                                      public static partial bool TryArity(long first, LuaOptional<long> second, out long count);

	                                      [LuaGlobal("arity")]
	                                      public static partial LuaOperationStatus ArityDetailed(long first, LuaOptional<double> second, LuaOptional<bool> third, out long count);

	                                      [LuaGlobal("arity")]
	                                      public static partial long ArityOn(LuaState state, LuaOptional<nuint> first);

	                                      [LuaGlobal("readBytesLike")]
	                                      public static partial string ReadBytesLike(nuint address, LuaOptional<int> count, LuaOptional<bool> asTable);

	                                      [LuaGlobal("shape")]
	                                      public static partial LuaOperationStatus ShapeOptional(int mode, out LuaOptional<long> first);

	                                      [LuaGlobal("shape")]
	                                      public static partial LuaOperationStatus ShapeRequiredThenOptional(int mode, out long first, out LuaOptional<long> second);

	                                      [LuaGlobal("shape")]
	                                      public static partial bool TryShapeRequiredThenOptional(int mode, out long first, out LuaOptional<long> second);

	                                      [LuaGlobal("shape")]
	                                      public static partial LuaOperationStatus ShapeFlagAndWarning(int mode, out bool ok, out LuaOptional<string> warning);

	                                      [LuaGlobal("shape")]
	                                      public static partial bool TryShapeScalar(int mode, out long value);

	                                      [LuaGlobal("seq")]
	                                      public static partial LuaOperationStatus Sequence(int count, LuaOptional<int> bad, Span<long> values, out int valueCount);

	                                      [LuaGlobal("seq")]
	                                      public static partial LuaOperationStatus SequenceAfterOne(int count, out long first, out LuaOptional<long> second, Span<long> rest, out int restCount);
	                                  }
	                                  """;

	/// <summary>Exported functions with optional trailing parameters.</summary>
	public const string FunctionSuite = """
	                                    using CheatEngine.SDK.Annotations.Lua;
	                                    using CheatEngine.SDK.Lua.Marshalling;

	                                    namespace Demo;

	                                    public static partial class OptionalFunctions
	                                    {
	                                        [LuaFunction("optdescribe")]
	                                        public static string Describe(long first, LuaOptional<long> second, LuaOptional<string> third) =>
	                                            first + "|" + State(second) + "|" + State(third);

	                                        [LuaFunction("optflag")]
	                                        public static string Flag(LuaOptional<bool> value) => value.IsOmitted ? "omitted" : value.IsNil ? "nil" : value.Value ? "yes" : "no";

	                                        private static string State<T>(LuaOptional<T> value)
	                                            where T : notnull =>
	                                            value.IsOmitted ? "omitted" : value.IsNil ? "nil" : value.Value.ToString()!;
	                                    }
	                                    """;
}

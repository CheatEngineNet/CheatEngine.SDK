namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>Binding declarations that compile warning-free against the real SDK assemblies (CS1591 aside).</summary>
internal static class BindingSources
{
    /// <summary>Two exported functions: the nominal case whose exact output <see cref="ExpectedFiles.Functions" /> pins.</summary>
    public const string Functions = """
                                    using CheatEngine.SDK.Annotations.Lua;

                                    namespace Demo;

                                    public static partial class Functions
                                    {
                                        [LuaFunction("add")]
                                        public static long Add(long a, long b) => a + b;

                                        [LuaFunction("greet")]
                                        public static string Greet(string name) => "hello, " + name;
                                    }
                                    """;

    /// <summary>The worked example: a Try form and a throwing form of the same global, sharing one cache.</summary>
    public const string Globals = """
                                  using CheatEngine.SDK.Annotations.Lua;

                                  namespace Demo;

                                  public static partial class Memory
                                  {
                                      [LuaGlobal("readInteger")]
                                      public static partial bool TryReadInt32(nuint address, out int value);

                                      [LuaGlobal("readInteger")]
                                      public static partial int ReadInt32(nuint address);
                                  }
                                  """;

    /// <summary>Every supported shape of an exported function, for execution against a real Lua.</summary>
    public const string FunctionSuite = """
                                        using System;
                                        using CheatEngine.SDK.Annotations.Lua;
                                        using CheatEngine.SDK.Lua.State;

                                        namespace Demo;

                                        public static partial class Suite
                                        {
                                            [LuaFunction("add")]
                                            public static long Add(long a, long b) => a + b;

                                            [LuaFunction("greet")]
                                            public static string Greet(string name) => "hello, " + name;

                                            [LuaFunction("ping")]
                                            public static void Ping() => Pings++;

                                            public static int Pings;

                                            [LuaFunction("isint")]
                                            public static bool IsInteger(LuaState L, double value) => L.IsInteger(1);

                                            [LuaFunction("boom")]
                                            public static int Boom() => throw new InvalidOperationException("managed boom");

                                            [LuaFunction("echo")]
                                            public static ReadOnlySpan<byte> Echo(ReadOnlySpan<byte> text) => text;

                                            [LuaFunction("half")]
                                            public static double Half(double value) => value / 2;

                                            [LuaFunction("negate")]
                                            public static bool Negate(bool value) => !value;

                                            [LuaFunction("step")]
                                            public static nuint Step(nuint address) => address + 4;

                                            [LuaFunction("small")]
                                            public static int Small(int value) => value;

                                            [LuaFunction("scale")]
                                            public static float Scale(float value) => value * 2;

                                            [LuaFunction("maybe")]
                                            public static string? Maybe(bool give) => give ? "yes" : null;
                                        }
                                        """;

    /// <summary>Every supported shape of a bound global, for execution against stand-in Lua globals.</summary>
    public const string GlobalSuite = """
                                      using System;
                                      using System.Diagnostics.CodeAnalysis;
                                      using CheatEngine.SDK.Annotations.Lua;
                                      using CheatEngine.SDK.Lua.State;

                                      namespace Demo;

                                      public static partial class Bindings
                                      {
                                          [LuaGlobal("readInteger")]
                                          public static partial bool TryReadInt32(nuint address, out int value);

                                          [LuaGlobal("readInteger")]
                                          public static partial int ReadInt32(nuint address);

                                          [LuaGlobal("readString")]
                                          public static partial bool TryReadString(nuint address, int maxLength, Span<byte> destination, out int written);

                                          [LuaGlobal("readString")]
                                          public static partial bool TryReadString(nuint address, int maxLength, [MaybeNullWhen(false)] out string value);

                                          [LuaGlobal("readString")]
                                          public static partial string ReadString(nuint address, int maxLength);

                                          [LuaGlobal("beep")]
                                          public static partial void Beep();

                                          [LuaGlobal("isKeyPressed")]
                                          public static partial bool IsKeyPressed(int key);

                                          [LuaGlobal("divide")]
                                          public static partial bool TryDivide(long dividend, long divisor, out long quotient, out long remainder);

                                          [LuaGlobal("describe")]
                                          public static partial bool TryDescribe(double value, bool flag, [MaybeNullWhen(false)] out string text, out double doubled);

                                          [LuaGlobal("add")]
                                          public static partial long AddOn(LuaState state, long a, long b);

                                          [LuaGlobal("add")]
                                          public static partial bool TryAddOn(LuaState state, long a, long b, out long sum);

                                          [LuaGlobal("upper")]
                                          public static partial string Upper(ReadOnlySpan<byte> text);

                                          [LuaGlobal("upper")]
                                          public static partial string? UpperOrNull(string? text);
                                      }
                                      """;

    /// <summary>A function with more arguments than the guaranteed free slots: the body checks the stack first.</summary>
    public const string ManyArguments = """
                                        using CheatEngine.SDK.Annotations.Lua;

                                        namespace Demo;

                                        public static partial class Wide
                                        {
                                            [LuaGlobal("sum16")]
                                            public static partial bool TrySum16(
                                                int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7,
                                                int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15,
                                                out long sum);

                                            [LuaGlobal("sum16")]
                                            public static partial long Sum16(
                                                int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7,
                                                int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15);
                                        }
                                        """;

    /// <summary>Both binding kinds in one type: two files.</summary>
    public const string Mixed = """
                                using CheatEngine.SDK.Annotations.Lua;

                                namespace Demo;

                                public static partial class Mixed
                                {
                                    [LuaFunction("twice")]
                                    public static int Twice(int value) => value * 2;

                                    [LuaGlobal("readInteger")]
                                    public static partial bool TryReadInt32(nuint address, out int value);
                                }
                                """;
}

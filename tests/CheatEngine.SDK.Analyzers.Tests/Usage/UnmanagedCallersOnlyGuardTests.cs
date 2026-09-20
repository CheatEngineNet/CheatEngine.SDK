using CheatEngine.SDK.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
    CheatEngine.SDK.Analyzers.Usage.UnmanagedCallersOnlyGuardAnalyzer>;

namespace CheatEngine.SDK.Analyzers.Tests.Usage;

/// <summary>
///     CESDK1004: the structural definition of "the body is entirely guarded by a catch-all", case by case. Guarded
///     shapes first, then each way of leaving a hole.
/// </summary>
public sealed class UnmanagedCallersOnlyGuardTests
{
    [Theory]
    [InlineData("catch (Exception) { return 0; }")]
    [InlineData("catch (System.Exception) { return 0; }")]
    [InlineData("catch (Exception exception) { Log(exception); return 0; }")]
    [InlineData("catch { return 0; }")]
    [InlineData("catch (InvalidOperationException) { return -1; } catch (Exception) { return 0; }")]
    [InlineData("catch (Exception) { return 0; } finally { Cleanup(); }")]
    [InlineData("catch (Exception exception) { Environment.FailFast(exception.Message); return 0; }")]
    [InlineData("catch (Exception) { Environment.Exit(1); return 0; }")]
    public async Task Body_made_of_one_try_with_a_catch_all_reports_nothing(string handlers)
    {
        await Verifier.VerifyAsync(Callbacks($$"""
                                               [UnmanagedCallersOnly]
                                               private static int OnCall(nint state)
                                               {
                                                   try
                                                   {
                                                       return Work(state);
                                                   }
                                                   {{handlers}}
                                               }
                                               """));
    }

    [Fact]
    public async Task Trivial_declarations_before_and_trivial_return_after_the_try_report_nothing()
    {
        await Verifier.VerifyAsync(Callbacks("""
                                             private const int Failure = -1;

                                             [UnmanagedCallersOnly]
                                             private static int OnCall(nint state)
                                             {
                                                 int result = Failure, other;
                                                 nint copy = state;
                                                 long wide = 0;
                                                 object? nothing = null;
                                                 Guid id = default;
                                                 ;
                                                 try
                                                 {
                                                     other = Work(copy);
                                                     result = other + (int)wide + id.GetHashCode() + (nothing?.GetHashCode() ?? 0);
                                                 }
                                                 catch (Exception)
                                                 {
                                                     result = Failure;
                                                 }

                                                 return result;
                                             }
                                             """));
    }

    [Theory]
    [InlineData("private static int OnCall(nint state) => 0;")]
    [InlineData("private static int OnCall(nint state) => -1;")]
    [InlineData("private static nint OnCall(nint state) => default;")]
    [InlineData("private static nint OnCall(nint state) => state;")]
    [InlineData("private static long OnCall(int state) => state;")]
    [InlineData("private static void OnCall(nint state) { }")]
    [InlineData("private static int OnCall(nint state) { return 1; }")]
    public async Task Body_that_cannot_throw_reports_nothing(string method)
    {
        await Verifier.VerifyAsync(Callbacks($$"""
                                               [UnmanagedCallersOnly]
                                               {{method}}
                                               """));
    }

    [Fact]
    public async Task Lambdas_and_local_functions_that_throw_inside_the_try_report_nothing()
    {
        await Verifier.VerifyAsync(Callbacks("""
                                             [UnmanagedCallersOnly]
                                             private static int OnCall(nint state)
                                             {
                                                 try
                                                 {
                                                     Func<int> lambda = () => throw new InvalidOperationException();
                                                     return lambda() + Local();

                                                     int Local() => throw new NotSupportedException();
                                                 }
                                                 catch (Exception)
                                                 {
                                                     return 0;
                                                 }

                                                 static int Unused() => throw new NotSupportedException();
                                             }
                                             """));
    }

    [Fact]
    public async Task Nested_block_and_consecutive_guard_tries_report_nothing()
    {
        await Verifier.VerifyAsync(Callbacks("""
                                             [UnmanagedCallersOnly]
                                             private static void OnCall(nint state)
                                             {
                                                 {
                                                     try
                                                     {
                                                         Work(state);
                                                     }
                                                     catch
                                                     {
                                                     }
                                                 }

                                                 try
                                                 {
                                                     Work(state);
                                                 }
                                                 catch (Exception exception)
                                                 {
                                                     Log(exception);
                                                 }
                                             }
                                             """));
    }

    [Theory]
    [InlineData("unsafe")]
    [InlineData("checked")]
    [InlineData("unchecked")]
    public async Task Unsafe_and_checked_blocks_count_as_nested_blocks(string keyword)
    {
        await Verifier.VerifyAsync(Callbacks($$"""
                                               [UnmanagedCallersOnly]
                                               private static int OnCall(nint state)
                                               {
                                                   {{keyword}}
                                                   {
                                                       try
                                                       {
                                                           return Work(state);
                                                       }
                                                       catch (Exception)
                                                       {
                                                           return 0;
                                                       }
                                                   }
                                               }
                                               """));
    }

    [Fact]
    public async Task Attribute_with_arguments_and_full_name_is_recognised()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
                      private static int {|#0:OnCall|}(nint state)
                      {
                          return Work(state);
                      }
                      """),
            Unguarded(0, "OnCall"));
    }

    [Fact]
    public async Task Method_without_the_attribute_is_not_analysed()
    {
        await Verifier.VerifyAsync(Callbacks("""
                                             private static int OnCall(nint state)
                                             {
                                                 return Work(state);
                                             }
                                             """));
    }

    [Fact]
    public async Task Method_in_generated_code_is_not_analysed()
    {
        await Verifier.VerifyAsync("// <auto-generated/>\n" + Callbacks("""
                                                                        [UnmanagedCallersOnly]
                                                                        private static int OnCall(nint state)
                                                                        {
                                                                            return Work(state);
                                                                        }
                                                                        """));
    }

    [Fact]
    public async Task Project_without_a_cheatengine_sdk_reference_is_not_analysed()
    {
        await Verifier.VerifyWithoutCheatEngineSdkAsync(Callbacks("""
                                                                  [UnmanagedCallersOnly]
                                                                  private static int OnCall(nint state)
                                                                  {
                                                                      return Work(state);
                                                                  }
                                                                  """));
    }

    [Fact]
    public async Task Body_without_a_try_reports()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|#0:OnCall|}(nint state)
                      {
                          return Work(state);
                      }
                      """),
            Unguarded(0, "OnCall"));
    }

    [Theory]
    [InlineData("finally { Cleanup(); }")]
    [InlineData("catch (InvalidOperationException) { return 0; }")]
    [InlineData("catch (Exception) { throw; }")]
    [InlineData("catch (Exception exception) { throw new InvalidOperationException(\"wrapped\", exception); }")]
    [InlineData("catch (Exception exception) when (exception is not OutOfMemoryException) { return 0; }")]
    [InlineData("catch (Exception) when (true) { return 0; }")]
    [InlineData("catch (InvalidOperationException) { throw; } catch (Exception) { return 0; }")]
    [InlineData("catch (Exception) { return 0; } finally { if (state == 0) throw new InvalidOperationException(); }")]
    [InlineData(
        "catch (Exception exception) { return exception.Message.Length > 0 ? 0 : throw new InvalidOperationException(); }")]
    [InlineData("catch (Exception) { Func<int> rethrow = () => throw new InvalidOperationException(); return 0; }")]
    [InlineData(
        "catch (Exception exception) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw(); return 0; }")]
    [InlineData(
        "catch (Exception exception) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception); return 0; }")]
    [InlineData("catch (Exception) { Fail(); return 0; }")]
    [InlineData("catch (Exception) { return 0; } finally { Fail(); }")]
    public async Task Try_that_does_not_swallow_everything_reports(string handlers)
    {
        await Verifier.VerifyAsync(
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        private static int {|#0:OnCall|}(nint state)
                        {
                            try
                            {
                                return Work(state);
                            }
                            {{handlers}}
                        }
                        """),
            Unguarded(0, "OnCall"));
    }

    [Theory]
    [InlineData("Cleanup();")]
    [InlineData("int before = Work(state);")]
    [InlineData("using IDisposable scope = Scope();")]
    [InlineData("if (state == 0) { return 0; }")]
    [InlineData("int[] buffer = new int[4];")]
    [InlineData("string text = $\"{state}\";")]
    [InlineData("dynamic late = null; int bound = late;")]
    [InlineData("dynamic late = null; string bound = late;")]
    [InlineData("(Source, int) from = default; (Target, int) to = from;")]
    [InlineData("(Source, int) from = default; (Target, int)? to = from;")]
    [InlineData("Source from = default; Target to = from;")]
    [InlineData("object boxed = state;")]
    [InlineData("object boxed = null; int unboxed = (int)boxed;")]
    [InlineData("int? maybe = null; int value = (int)maybe;")]
    [InlineData("decimal money = 0; int value = (int)money;")]
    [InlineData("long wide = 0; int value = checked((int)wide);")]
    [InlineData("int number = 0; int value = checked(-number);")]
    [InlineData("int number = 0; Index fromEnd = ^number;")]
    [InlineData("int value = Counter;")]
    [InlineData("bool ok = false; int value = ok ? Work(state) : 0;")]
    public async Task Statement_that_can_throw_in_front_of_the_try_reports(string statement)
    {
        await Verifier.VerifyAsync(
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        private static int {|#0:OnCall|}(nint state)
                        {
                            {{statement}}
                            try
                            {
                                return Work(state);
                            }
                            catch (Exception)
                            {
                                return 0;
                            }
                        }
                        """),
            Unguarded(0, "OnCall"));
    }

    [Fact]
    public async Task Return_that_can_throw_after_the_try_reports()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|#0:OnCall|}(nint state)
                      {
                          try
                          {
                              Cleanup();
                          }
                          catch (Exception)
                          {
                          }

                          return Work(state);
                      }
                      """),
            Unguarded(0, "OnCall"));
    }

    [Fact]
    public async Task Body_without_a_try_whose_only_risk_is_a_dynamic_conversion_reports()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|#0:OnCall|}(nint state)
                      {
                          dynamic late = default;
                          int bound = late;
                          return bound;
                      }

                      [UnmanagedCallersOnly]
                      private static int {|#1:OnReturn|}(nint state)
                      {
                          dynamic late = default;
                          return late;
                      }
                      """),
            Unguarded(0, "OnCall"),
            Unguarded(1, "OnReturn"));
    }

    [Theory]
    [InlineData("return ok ? 1 : 0;")]
    [InlineData("return !ok ? 1 : 0;")]
    [InlineData("return -result;")]
    [InlineData("return ~result;")]
    [InlineData("return (int)wide;")]
    [InlineData("return unchecked((int)wide);")]
    [InlineData("return (int)status;")]
    [InlineData("return (int)(ok ? wide : -wide);")]
    public async Task Return_of_a_value_that_cannot_throw_after_the_try_reports_nothing(string tail)
    {
        await Verifier.VerifyAsync(Callbacks($$"""
                                               [UnmanagedCallersOnly]
                                               private static int OnCall(nint state)
                                               {
                                                   bool ok = false;
                                                   int result = 0;
                                                   long wide = 0;
                                                   DayOfWeek status = default;
                                                   try
                                                   {
                                                       result = Work(state);
                                                       wide = result;
                                                       status = (DayOfWeek)result;
                                                       ok = true;
                                                   }
                                                   catch (Exception)
                                                   {
                                                   }

                                                   {{tail}}
                                               }
                                               """));
    }

    [Theory]
    [InlineData("IntPtr.Zero")]
    [InlineData("nint.Zero")]
    [InlineData("(nint)narrow")]
    [InlineData("unchecked((nint)wide)")]
    public async Task Handle_value_that_cannot_throw_reports_nothing(string value)
    {
        await Verifier.VerifyAsync(Callbacks($$"""
                                               [UnmanagedCallersOnly]
                                               private static nint OnCall(nint state)
                                               {
                                                   int narrow = 0;
                                                   ulong wide = 0;
                                                   nint handle = {{value}};
                                                   try
                                                   {
                                                       narrow = Work(state);
                                                       wide = (ulong)narrow;
                                                       handle = narrow;
                                                   }
                                                   catch (Exception)
                                                   {
                                                   }

                                                   return {{value}};
                                               }
                                               """));
    }

    [Fact]
    public async Task Built_in_conversions_that_cannot_throw_report_nothing()
    {
        await Verifier.VerifyAsync(Callbacks("""
                                             [UnmanagedCallersOnly]
                                             private static unsafe void* OnCall(int* state, int count)
                                             {
                                                 long wide = count;
                                                 double real = wide;
                                                 decimal money = count;
                                                 int? maybe = count;
                                                 long? maybeWide = count;
                                                 long? stillWide = maybe;
                                                 Guid? id = default(Guid);
                                                 string text = null;
                                                 object same = text;
                                                 IComparable comparable = text;
                                                 (int Left, int Right) named = default;
                                                 (int, int) unnamed = named;
                                                 void* untyped = state;
                                                 try
                                                 {
                                                     Work((nint)(real + (double)money) + (maybeWide.HasValue ? 1 : 0) + (stillWide.HasValue ? 1 : 0) + (id.HasValue ? 1 : 0));
                                                     Work(same == comparable ? unnamed.Item1 : 0);
                                                 }
                                                 catch (Exception)
                                                 {
                                                     return null;
                                                 }

                                                 return untyped;
                                             }
                                             """));
    }

    [Theory]
    [InlineData("lock (Gate) { try { return Work(state); } catch (Exception) { return 0; } }")]
    [InlineData("using (Scope()) { try { return Work(state); } catch (Exception) { return 0; } }")]
    [InlineData(
        "unsafe { fixed (int* pointer = Numbers) { try { return Work(state) + *pointer; } catch (Exception) { return 0; } } }")]
    [InlineData("checked { return Work(state); }")]
    [InlineData("unsafe { return Work(state); }")]
    public async Task Guard_hidden_inside_another_statement_reports(string body)
    {
        await Verifier.VerifyAsync(
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        private static int {|#0:OnCall|}(nint state)
                        {
                            {{body}}
                        }
                        """),
            Unguarded(0, "OnCall"));
    }

    [Theory]
    [InlineData("private static int {|#0:OnCall|}(nint state) => Work(state);")]
    [InlineData("private static void {|#0:OnCall|}(nint state) => Cleanup();")]
    [InlineData("private static int {|#0:OnCall|}(nint state) => checked((int)state);")]
    [InlineData("private static int {|#0:OnCall|}(nint state) => Shared;")]
    public async Task Expression_body_that_can_throw_reports(string method)
    {
        await Verifier.VerifyAsync(
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        {{method}}
                        """),
            Unguarded(0, "OnCall"));
    }

    [Fact]
    public async Task Local_function_with_the_attribute_is_analysed_on_its_own()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      private static unsafe nint Register()
                      {
                          delegate* unmanaged<nint, int> guarded = &Guarded;
                          delegate* unmanaged<nint, int> unguarded = &Unguarded;
                          delegate* unmanaged<nint, int> expression = &Expression;
                          return (nint)guarded + (nint)unguarded + (nint)expression;

                          [UnmanagedCallersOnly]
                          static int Guarded(nint state)
                          {
                              try
                              {
                                  return Work(state);
                              }
                              catch (Exception)
                              {
                                  return 0;
                              }
                          }

                          [UnmanagedCallersOnly]
                          static int {|#0:Unguarded|}(nint state)
                          {
                              return Work(state);
                          }

                          [UnmanagedCallersOnly]
                          static int {|#1:Expression|}(nint state) => Work(state);
                      }
                      """),
            Unguarded(0, "Unguarded"),
            Unguarded(1, "Expression"));
    }

    [Fact]
    public async Task Unguarded_method_containing_a_guarded_local_function_still_reports()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|#0:OnCall|}(nint state)
                      {
                          return Inner(state);

                          static int Inner(nint value)
                          {
                              try
                              {
                                  return Work(value);
                              }
                              catch (Exception)
                              {
                                  return 0;
                              }
                          }
                      }
                      """),
            Unguarded(0, "OnCall"));
    }

    // A static class with the helpers the bodies above call; every helper may throw as far as the rule knows.
    private static string Callbacks(string members)
    {
        return $$"""
                 using System;
                 using System.Runtime.InteropServices;

                 namespace MyPlugin;

                 internal static class Callbacks
                 {
                     private static readonly object Gate = new();

                     private static readonly int[] Numbers = [1];

                     private static readonly int Counter = Work(1);

                     private static int Shared { get; set; }

                 {{members}}

                     private static int Work(nint state) => state == 0 ? throw new InvalidOperationException() : 1;

                     private static void Cleanup() { }

                     private static void Log(Exception exception) { }

                     private static IDisposable Scope() => throw new NotSupportedException();

                     [System.Diagnostics.CodeAnalysis.DoesNotReturn]
                     private static void Fail() => throw new InvalidOperationException();

                     private struct Source
                     {
                     }

                     private struct Target
                     {
                         public static implicit operator Target(Source source) => throw new InvalidOperationException();
                     }
                 }
                 """;
    }

    private static DiagnosticResult Unguarded(int location, string methodName)
    {
        return Verifier.Diagnostic(DiagnosticDescriptors.UnguardedUnmanagedCallersOnly).WithLocation(location)
            .WithArguments(methodName);
    }
}

using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.CodeFixVerifier<
    CheatEngine.SDK.Analyzers.Usage.UnmanagedCallersOnlyGuardAnalyzer,
    CheatEngine.SDK.Analyzers.CodeFixes.Usage.UnmanagedCallersOnlyGuardCodeFixProvider>;

namespace CheatEngine.SDK.Analyzers.Tests.Usage;

/// <summary>
///     The CESDK1004 fix: the body moves into a try, the catch-all returns the failure value of the return type. Each
///     run also checks that the fixed code is free of CESDK1004, and Fix All whenever a source has several diagnostics.
/// </summary>
public sealed class UnmanagedCallersOnlyGuardCodeFixTests
{
    private const string SystemUsings = "using System;\nusing System.Runtime.InteropServices;\n\n";

    private const string CommentedExpressionBodies = """
                                                     [UnmanagedCallersOnly]
                                                     private static int {|CESDK1004:OnCall|}(nint state) => // why this call
                                                         Work(state); // trailing

                                                     [UnmanagedCallersOnly]
                                                     private static void {|CESDK1004:OnNotify|}(nint state) =>
                                                         // why we call Work here
                                                         /* and why now */
                                                         Work(state);

                                                     [UnmanagedCallersOnly]
                                                     private static int {|CESDK1004:OnQuery|}(nint state)
                                                         // the arrow sits on its own line
                                                         => Work(state) /* inline */ ; // after the semicolon
                                                     """;

    private const string CommentedExpressionBodiesFixed = """
                                                          [UnmanagedCallersOnly]
                                                          private static int OnCall(nint state)
                                                          {
                                                              try
                                                              {
                                                                  // why this call
                                                                  return Work(state); // trailing
                                                              }
                                                              catch (Exception)
                                                              {
                                                                  return 0;
                                                              }
                                                          }

                                                          [UnmanagedCallersOnly]
                                                          private static void OnNotify(nint state)
                                                          {
                                                              try
                                                              {
                                                                  // why we call Work here
                                                                  /* and why now */
                                                                  Work(state);
                                                              }
                                                              catch (Exception)
                                                              {
                                                                  // A managed exception must never unwind into native code.
                                                              }
                                                          }

                                                          [UnmanagedCallersOnly]
                                                          private static int OnQuery(nint state)
                                                          {
                                                              try
                                                              {
                                                                  // the arrow sits on its own line
                                                                  return Work(state) /* inline */ ; // after the semicolon
                                                              }
                                                              catch (Exception)
                                                              {
                                                                  return 0;
                                                              }
                                                          }
                                                          """;

    private const string FixAllCallbacks = """
                                           using System;
                                           using System.Runtime.InteropServices;

                                           namespace MyPlugin;

                                           internal static class FirstCallbacks
                                           {
                                               [UnmanagedCallersOnly]
                                               private static int {|CESDK1004:First|}(nint state)
                                               {
                                                   return state == 0 ? throw new InvalidOperationException() : 1;
                                               }

                                               [UnmanagedCallersOnly]
                                               private static bool {|CESDK1004:Second|}(nint state)
                                               {
                                                   if (state == 0)
                                                   {
                                                       return false;
                                                   }

                                                   return state.ToString().Length > 1;
                                               }

                                               [UnmanagedCallersOnly]
                                               private static int AlreadyGuarded(nint state)
                                               {
                                                   try
                                                   {
                                                       return state.ToString().Length;
                                                   }
                                                   catch
                                                   {
                                                       return 0;
                                                   }
                                               }
                                           }
                                           """;

    private const string FixAllCallbacksFixed = """
                                                using System;
                                                using System.Runtime.InteropServices;

                                                namespace MyPlugin;

                                                internal static class FirstCallbacks
                                                {
                                                    [UnmanagedCallersOnly]
                                                    private static int First(nint state)
                                                    {
                                                        try
                                                        {
                                                            return state == 0 ? throw new InvalidOperationException() : 1;
                                                        }
                                                        catch (Exception)
                                                        {
                                                            return 0;
                                                        }
                                                    }

                                                    [UnmanagedCallersOnly]
                                                    private static bool Second(nint state)
                                                    {
                                                        try
                                                        {
                                                            if (state == 0)
                                                            {
                                                                return false;
                                                            }

                                                            return state.ToString().Length > 1;
                                                        }
                                                        catch (Exception)
                                                        {
                                                            return false;
                                                        }
                                                    }

                                                    [UnmanagedCallersOnly]
                                                    private static int AlreadyGuarded(nint state)
                                                    {
                                                        try
                                                        {
                                                            return state.ToString().Length;
                                                        }
                                                        catch
                                                        {
                                                            return 0;
                                                        }
                                                    }
                                                }
                                                """;

    private const string FixAllNotifications = """
                                               using System.Runtime.InteropServices;

                                               namespace MyPlugin;

                                               internal static class Notifications
                                               {
                                                   [UnmanagedCallersOnly]
                                                   private static void {|CESDK1004:Third|}(nint state)
                                                   {
                                                       state.ToString();
                                                   }
                                               }
                                               """;

    private const string FixAllNotificationsFixed = """
                                                    using System.Runtime.InteropServices;

                                                    namespace MyPlugin;

                                                    internal static class Notifications
                                                    {
                                                        [UnmanagedCallersOnly]
                                                        private static void Third(nint state)
                                                        {
                                                            try
                                                            {
                                                                state.ToString();
                                                            }
                                                            catch (System.Exception)
                                                            {
                                                                // A managed exception must never unwind into native code.
                                                            }
                                                        }
                                                    }
                                                    """;

    [Fact]
    public async Task Block_body_is_wrapped_and_comments_move_with_it()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state)
                      {
                          // Leading comment stays with its statement.
                          int result = Work(state);
                          return result;
                          // Trailing comment moves into the try block.
                      }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
                      {
                          try
                          {
                              // Leading comment stays with its statement.
                              int result = Work(state);
                              return result;
                              // Trailing comment moves into the try block.
                          }
                          catch (Exception)
                          {
                              return 0;
                          }
                      }
                      """));
    }

    [Fact]
    public async Task Exception_type_is_qualified_when_system_is_not_imported()
    {
        await Verifier.VerifyAsync(
            Callbacks(
                """
                [System.Runtime.InteropServices.UnmanagedCallersOnly]
                private static int {|CESDK1004:OnCall|}(nint state)
                {
                    return Work(state);
                }
                """,
                string.Empty),
            Callbacks(
                """
                [System.Runtime.InteropServices.UnmanagedCallersOnly]
                private static int OnCall(nint state)
                {
                    try
                    {
                        return Work(state);
                    }
                    catch (System.Exception)
                    {
                        return 0;
                    }
                }
                """,
                string.Empty));
    }

    [Fact]
    public async Task Void_method_gets_a_commented_empty_catch()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static void {|CESDK1004:OnCall|}(nint state)
                      {
                          Work(state);
                      }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static void OnCall(nint state)
                      {
                          try
                          {
                              Work(state);
                          }
                          catch (Exception)
                          {
                              // A managed exception must never unwind into native code.
                          }
                      }
                      """));
    }

    [Theory]
    [InlineData("byte", "0")]
    [InlineData("short", "0")]
    [InlineData("uint", "0")]
    [InlineData("long", "0")]
    [InlineData("double", "0")]
    [InlineData("bool", "false")]
    [InlineData("nint", "default")]
    [InlineData("nuint", "default")]
    [InlineData("Status", "default")]
    [InlineData("Handle", "default")]
    public async Task Failure_value_follows_the_return_type(string returnType, string failureValue)
    {
        await Verifier.VerifyAsync(
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        private static {{returnType}} {|CESDK1004:OnCall|}(nint state)
                        {
                            return Make<{{returnType}}>(state);
                        }
                        """),
            Callbacks($$"""
                        [UnmanagedCallersOnly]
                        private static {{returnType}} OnCall(nint state)
                        {
                            try
                            {
                                return Make<{{returnType}}>(state);
                            }
                            catch (Exception)
                            {
                                return {{failureValue}};
                            }
                        }
                        """));
    }

    [Fact]
    public async Task Expression_body_becomes_a_guarded_block_body()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state) => Work(state);

                      [UnmanagedCallersOnly]
                      private static void {|CESDK1004:OnNotify|}(nint state) => Work(state);
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
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
                      private static void OnNotify(nint state)
                      {
                          try
                          {
                              Work(state);
                          }
                          catch (Exception)
                          {
                              // A managed exception must never unwind into native code.
                          }
                      }
                      """));
    }

    [Fact]
    public async Task Expression_body_that_is_a_throw_expression_becomes_a_throw_statement()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state) => throw new NotSupportedException();

                      [UnmanagedCallersOnly]
                      private static void {|CESDK1004:OnNotify|}(nint state) => throw new NotSupportedException("not yet");
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
                      {
                          try
                          {
                              throw new NotSupportedException();
                          }
                          catch (Exception)
                          {
                              return 0;
                          }
                      }

                      [UnmanagedCallersOnly]
                      private static void OnNotify(nint state)
                      {
                          try
                          {
                              throw new NotSupportedException("not yet");
                          }
                          catch (Exception)
                          {
                              // A managed exception must never unwind into native code.
                          }
                      }
                      """));
    }

    [Fact]
    public async Task Comments_around_an_expression_body_move_with_the_statement()
    {
        await Verifier.VerifyAsync(Callbacks(CommentedExpressionBodies), Callbacks(CommentedExpressionBodiesFixed));
    }

    [Fact]
    public async Task Expression_body_with_preprocessor_directives_gets_no_fix()
    {
        var source = Callbacks("""
                               [UnmanagedCallersOnly]
                               private static int {|CESDK1004:OnCall|}(nint state) =>
                               #if CHEATENGINE_SDK_TRACE
                                   Work(state) + 1;
                               #else
                                   Work(state);
                               #endif
                               """);

        await Verifier.VerifyAsync(source, source);
    }

    // Inside a block body both branches are whole statements and '#endif' sits in front of the closing brace:
    // everything moves into the try block. The formatter puts directives in column 0 and leaves inactive text alone.
    [Fact]
    public async Task Block_body_with_preprocessor_directives_is_wrapped_with_them()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state)
                      {
                      #if CHEATENGINE_SDK_TRACE
                          return Work(state) + 1;
                      #else
                          return Work(state);
                      #endif
                      }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
                      {
                          try
                          {
                      #if CHEATENGINE_SDK_TRACE
                          return Work(state) + 1;
                      #else
                              return Work(state);
                      #endif
                          }
                          catch (Exception)
                          {
                              return 0;
                          }
                      }
                      """).Replace("    #", "#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Single_line_block_body_is_wrapped()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state) { return Work(state); }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
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
                      """));
    }

    [Fact]
    public async Task Existing_try_without_a_catch_all_is_wrapped_as_a_whole()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int {|CESDK1004:OnCall|}(nint state)
                      {
                          try
                          {
                              return Work(state);
                          }
                          finally
                          {
                              Work(0);
                          }
                      }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static int OnCall(nint state)
                      {
                          try
                          {
                              try
                              {
                                  return Work(state);
                              }
                              finally
                              {
                                  Work(0);
                              }
                          }
                          catch (Exception)
                          {
                              return 0;
                          }
                      }
                      """));
    }

    [Fact]
    public async Task Local_function_is_wrapped_in_place()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      private static unsafe nint Register()
                      {
                          delegate* unmanaged<nint, int> callback = &OnCall;
                          return (nint)callback;

                          [UnmanagedCallersOnly]
                          static int {|CESDK1004:OnCall|}(nint state)
                          {
                              return Work(state);
                          }
                      }
                      """),
            Callbacks("""
                      private static unsafe nint Register()
                      {
                          delegate* unmanaged<nint, int> callback = &OnCall;
                          return (nint)callback;

                          [UnmanagedCallersOnly]
                          static int OnCall(nint state)
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
    public async Task Unguarded_local_function_inside_an_unguarded_method_takes_a_second_fix_all_pass()
    {
        await Verifier.VerifyAsync(
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static unsafe nint {|CESDK1004:Outer|}(nint state)
                      {
                          delegate* unmanaged<nint, int> callback = &Inner;
                          return (nint)callback + Work(state);

                          [UnmanagedCallersOnly]
                          static int {|CESDK1004:Inner|}(nint value)
                          {
                              return Work(value);
                          }
                      }
                      """),
            Callbacks("""
                      [UnmanagedCallersOnly]
                      private static unsafe nint Outer(nint state)
                      {
                          try
                          {
                              delegate* unmanaged<nint, int> callback = &Inner;
                              return (nint)callback + Work(state);

                              [UnmanagedCallersOnly]
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
                          catch (Exception)
                          {
                              return default;
                          }
                      }
                      """),
            fixAllIterations: 2);
    }

    [Fact]
    public async Task Fix_all_wraps_every_method_in_every_document()
    {
        await Verifier.VerifyAsync(
            [("Callbacks.cs", FixAllCallbacks), ("Notifications.cs", FixAllNotifications)],
            [("Callbacks.cs", FixAllCallbacksFixed), ("Notifications.cs", FixAllNotificationsFixed)]);
    }

    // A static class around the members under test. Members are spliced in at class-member indentation, so the
    // expected text matches formatted output character for character.
    private static string Callbacks(string members, string usings = SystemUsings)
    {
        return $$"""
                 {{usings}}namespace MyPlugin;

                 internal enum Status
                 {
                     Failed,
                     Ok,
                 }

                 internal struct Handle
                 {
                     public nint Value;
                 }

                 internal static class Callbacks
                 {
                 {{TestText.Indent(members)}}

                     private static int Work(nint state) => state == 0 ? throw new System.InvalidOperationException() : 1;

                     private static T Make<T>(nint state) where T : unmanaged => default;
                 }
                 """;
    }
}

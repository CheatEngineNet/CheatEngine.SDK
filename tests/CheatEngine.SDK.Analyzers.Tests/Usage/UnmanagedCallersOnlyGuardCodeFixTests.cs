using CheatEngine.SDK.Analyzers.CodeFixes.Usage;
using Verifier = CheatEngine.SDK.Analyzers.Tests.Infrastructure.CodeFixVerifier<
    CheatEngine.SDK.Analyzers.Usage.UnmanagedCallersOnlyGuardAnalyzer,
    CheatEngine.SDK.Analyzers.CodeFixes.Usage.UnmanagedCallersOnlyGuardCodeFixProvider>;

namespace CheatEngine.SDK.Analyzers.Tests.Usage;

/// <summary>
///     CESDK1004 fixes only the one unmanaged failure convention owned by this SDK: the CE bootstrap. The analyzer
///     remains broader, because every unmanaged entry must prevent a managed exception from unwinding into native code.
/// </summary>
public sealed class UnmanagedCallersOnlyGuardCodeFixTests
{
    [Fact]
    public async Task Exact_ce_bootstrap_is_wrapped_with_its_documented_zero_failure_value()
    {
        await Verifier.VerifyAsync(
            """
            using System;
            using System.Runtime.InteropServices;

            namespace CESDK;

            internal static class CESDK
            {
                [UnmanagedCallersOnly]
                public static int {|CESDK1004:CEPluginInitialize|}(IntPtr exportedFunctions, int bootstrap) => Work(exportedFunctions);

                private static int Work(IntPtr exportedFunctions) =>
                    exportedFunctions == default ? throw new InvalidOperationException() : 1;
            }
            """,
            """
            using System;
            using System.Runtime.InteropServices;

            namespace CESDK;

            internal static class CESDK
            {
                [UnmanagedCallersOnly]
                public static int CEPluginInitialize(IntPtr exportedFunctions, int bootstrap)
                {
                    try
                    {
                        return Work(exportedFunctions);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }

                private static int Work(IntPtr exportedFunctions) =>
                    exportedFunctions == default ? throw new InvalidOperationException() : 1;
            }
            """,
            UnmanagedCallersOnlyGuardCodeFixProvider.WrapEquivalenceKey);
    }

    [Fact]
    public async Task Arbitrary_integer_callback_gets_no_generic_zero_return_fix()
    {
        const string source = """
                              using System.Runtime.InteropServices;

                              namespace MyPlugin;

                              internal static class Callbacks
                              {
                                  [UnmanagedCallersOnly]
                                  private static int {|CESDK1004:OnCall|}(nint state) => state.ToString().Length;
                              }
                              """;

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Bootstrap_like_method_with_an_unverified_signature_gets_no_fix()
    {
        const string source = """
                              using System;
                              using System.Runtime.InteropServices;

                              namespace CESDK;

                              internal static class CESDK
                              {
                                  [UnmanagedCallersOnly]
                                  internal static int {|CESDK1004:CEPluginInitialize|}(IntPtr exportedFunctions, int bootstrap) =>
                                      exportedFunctions == IntPtr.Zero ? throw new InvalidOperationException() : 1;
                              }
                              """;

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Bootstrap_name_outside_the_canonical_namespace_gets_no_fix()
    {
        const string source = """
                              using System;
                              using System.Runtime.InteropServices;

                              namespace OtherPlugin;

                              internal static class CESDK
                              {
                                  [UnmanagedCallersOnly]
                                  public static int {|CESDK1004:CEPluginInitialize|}(IntPtr exportedFunctions, int bootstrap) =>
                                      exportedFunctions == IntPtr.Zero ? throw new InvalidOperationException() : 1;
                              }
                              """;

        await Verifier.VerifyAsync(source, source);
    }

    [Fact]
    public async Task Local_unmanaged_callback_inside_the_bootstrap_gets_no_outer_bootstrap_fix()
    {
        const string source = """
                              using System;
                              using System.Runtime.InteropServices;

                              namespace CESDK;

                              internal static class CESDK
                              {
                                  [UnmanagedCallersOnly]
                                  public static int CEPluginInitialize(IntPtr exportedFunctions, int bootstrap)
                                  {
                                      [UnmanagedCallersOnly]
                                      static int {|CESDK1004:Callback|}(nint state) => throw new InvalidOperationException();

                                      try
                                      {
                                          return 1;
                                      }
                                      catch (Exception)
                                      {
                                          return 0;
                                      }
                                  }
                              }
                              """;

        await Verifier.VerifyAsync(source, source);
    }
}

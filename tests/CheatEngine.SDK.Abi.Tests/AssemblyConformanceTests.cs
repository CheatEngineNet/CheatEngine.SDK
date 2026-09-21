using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests;

/// <summary>
///     Assembly-wide gates. They exist so that a structure or enumeration added to <c>CheatEngine.SDK.Abi</c> without a
///     row in the expected-size table, or with a shape the ABI does not allow (see <see cref="AbiShape" />: forbidden
///     field types at any depth, function pointers that are managed, not <c>Stdcall</c>, or carry a forbidden type in
///     their signature), fails the test run instead of slipping through.
/// </summary>
/// <remarks>
///     What reflection cannot see, and therefore stays a review rule: whether <c>[StructLayout]</c> was written out
///     (C# structures are sequential by default, the metadata is identical) and whether a new structure also got its
///     per-field offset test.
/// </remarks>
public sealed class AssemblyConformanceTests
{
    private static readonly Assembly AbiAssembly = typeof(PluginInitRecord).Assembly;

    /// <summary>Every public structure of the assembly with its 64-bit size. Adding a structure means adding a row.</summary>
    private static readonly Dictionary<string, int> ExpectedSizesOn64Bit = new(StringComparer.Ordinal)
    {
        [nameof(Bool32)] = 4,
        [nameof(Bool8)] = 1,
        [nameof(PluginInitRecord)] = 36,
        [nameof(ManagedExportedFunctions)] = 48,
        [nameof(PluginVersion)] = 16,
        [nameof(AddressListPluginInit)] = 16,
        [nameof(MemoryViewPluginInit)] = 24,
        [nameof(DebugEventPluginInit)] = 8,
        [nameof(DebugEventObservation)] = 24,
        [nameof(ProcessWatcherPluginInit)] = 8,
        [nameof(FunctionPointerChangePluginInit)] = 8,
        [nameof(MainMenuPluginInit)] = 24,
        [nameof(DisassemblerContextPluginInit)] = 32,
        [nameof(DisassemblerRenderLinePluginInit)] = 8,
        [nameof(AutoAssemblerPluginInit)] = 8
    };

    [Fact]
    public void Assembly_disables_runtime_marshalling()
    {
        Assert.NotNull(AbiAssembly.GetCustomAttribute<DisableRuntimeMarshallingAttribute>());
    }

    [Fact]
    public void Assembly_references_no_other_cheatengine_sdk_assembly()
    {
        var cheatEngineSdkReferences = AbiAssembly.GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("CheatEngine.SDK", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(cheatEngineSdkReferences);
    }

    [Fact]
    public void Every_public_structure_is_listed_in_the_expected_size_table()
    {
        var actual = PublicStructures().Select(static type => type.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = ExpectedSizesOn64Bit.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_public_structure_on_64_bit_has_the_expected_size()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

        Assert.All(
            PublicStructures(),
            static type =>
            {
                Assert.True(ExpectedSizesOn64Bit.TryGetValue(type.Name, out var expected),
                    $"No expected size for {type.Name}.");
                Assert.Equal(expected, RuntimeHelpers.SizeOf(type.TypeHandle));
            });
    }

    [Fact]
    public void Every_structure_is_sequential_and_blittable_at_any_depth()
    {
        var structures = AllStructures().ToArray();

        Assert.NotEmpty(structures);
        Assert.All(structures, static type => Assert.Null(AbiShape.FindViolation(type, AbiAssembly)));
    }

    [Fact]
    public void Only_the_init_record_overrides_the_default_packing()
    {
        Assert.All(
            AllStructures(),
            static type =>
            {
                // Reflection reports the default either as 0 or as the runtime's default of 8.
                var pack = type.StructLayoutAttribute?.Pack ?? 0;
                if (type == typeof(PluginInitRecord))
                    Assert.Equal(1, pack);
                else
                    Assert.True(pack is 0 or 8,
                        $"{type.Name} declares Pack = {pack.ToString(CultureInfo.InvariantCulture)}: only the init record is packed by the host.");
            });
    }

    [Fact]
    public void Every_public_enumeration_is_four_bytes_wide()
    {
        var enums = AbiAssembly.GetExportedTypes().Where(static type => type.IsEnum).ToArray();

        Assert.NotEmpty(enums);
        Assert.All(enums, static type => Assert.Equal(typeof(int), Enum.GetUnderlyingType(type)));
    }

    private static IEnumerable<Type> PublicStructures()
    {
        return AbiAssembly.GetExportedTypes().Where(static type => type.IsValueType && !type.IsEnum);
    }

    /// <summary>
    ///     Public and non-public structures alike: an internal helper structure is as much part of a layout as the
    ///     public structure that embeds it. Compiler-generated types (static data blobs) are not ours to judge.
    /// </summary>
    private static IEnumerable<Type> AllStructures()
    {
        return AbiAssembly.GetTypes()
            .Where(static type => type.IsValueType && !type.IsEnum && !IsCompilerGenerated(type));
    }

    private static bool IsCompilerGenerated(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.Name.StartsWith('<') || current.IsDefined(typeof(CompilerGeneratedAttribute), false))
                return true;

        return false;
    }
}

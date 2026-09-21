using System.Collections.Generic;
using System.Globalization;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Fixture;

/// <summary>
///     Compares the x64 facts emitted by the independently compiled C++ fixture with measurements from the managed
///     ABI records. The native CI job supplies the facts file; ordinary managed runs deliberately have no C++ fixture
///     filesystem dependency.
/// </summary>
public sealed unsafe class NativeAbiFixtureManagedComparisonTests
{
    /// <summary>Name of the CI-provided absolute path to the validated native fixture facts file.</summary>
    internal const string FactsPathEnvironmentVariable = "CE77_NATIVE_ABI_FACTS_PATH";

    /// <summary>Name of the opt-in gate that makes the native fixture facts mandatory.</summary>
    internal const string RequiredEnvironmentVariable = "CE77_NATIVE_ABI_REQUIRED";

    [Fact]
    public void Native_fixture_layout_facts_match_the_managed_x64_measurements_when_CI_supplies_them()
    {
        var factsPath = ResolveFactsPath(
            Environment.GetEnvironmentVariable(FactsPathEnvironmentVariable),
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable));
        if (factsPath is null)
        {
            Assert.Null(factsPath);
            return;
        }

        Assert.True(Layout.Is64BitProcess, Layout.Requires64BitProcess);

        var nativeFacts = ReadFacts(factsPath);
        var managedFacts = CreateManagedLayoutFacts();
        foreach (var managedFact in managedFacts)
        {
            Assert.True(nativeFacts.TryGetValue(managedFact.Key, out var nativeValue),
                $"The native ABI fixture omitted '{managedFact.Key}'.");
            Assert.Equal(managedFact.Value, nativeValue);
        }
    }

    [Fact]
    public void Native_fixture_facts_path_is_optional_when_required_mode_is_not_enabled()
    {
        Assert.Null(ResolveFactsPath(null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Native_fixture_required_mode_rejects_an_absent_facts_path(string? factsPath)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ResolveFactsPath(factsPath, "true"));

        Assert.Equal(
            $"'{RequiredEnvironmentVariable}=true' requires '{FactsPathEnvironmentVariable}' to name a validated native ABI fixture facts file.",
            exception.Message);
    }

    [Fact]
    public void Native_fixture_comparison_rejects_a_supplied_missing_facts_file()
    {
        var factsPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

        var exception = Assert.Throws<FileNotFoundException>(() => ReadFacts(factsPath));

        Assert.Equal(factsPath, exception.FileName);
    }

    private static string? ResolveFactsPath(string? factsPath, string? requiredMode)
    {
        if (!string.IsNullOrWhiteSpace(factsPath)) return factsPath;

        if (string.Equals(requiredMode, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{RequiredEnvironmentVariable}=true' requires '{FactsPathEnvironmentVariable}' to name a validated native ABI fixture facts file.");
        }

        return null;
    }

    private static Dictionary<string, string> ReadFacts(string factsPath)
    {
        if (!File.Exists(factsPath))
        {
            throw new FileNotFoundException($"The native ABI fixture facts file '{factsPath}' was not found.", factsPath);
        }

        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(factsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var separator = line.IndexOf('=');
            Assert.True(separator > 0, $"The native ABI fixture fact '{line}' is not key=value.");
            var key = line[..separator];
            var value = line[(separator + 1)..];
            Assert.True(facts.TryAdd(key, value), $"The native ABI fixture emitted duplicate fact '{key}'.");
        }

        return facts;
    }

    private static Dictionary<string, string> CreateManagedLayoutFacts()
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        AddLayout<PluginVersion>(facts, "plugin_version");
        AddLayout<PluginType0Record>(facts, "plugin_type0_record");
        AddLayout<AddressListPluginInit>(facts, "plugin_type0_init");
        AddLayout<MemoryViewPluginInit>(facts, "plugin_type1_init");
        AddLayout<DebugEventPluginInit>(facts, "plugin_type2_init");
        AddLayout<ProcessWatcherPluginInit>(facts, "plugin_type3_init");
        AddLayout<FunctionPointerChangePluginInit>(facts, "plugin_type4_init");
        AddLayout<MainMenuPluginInit>(facts, "plugin_type5_init");
        AddLayout<DisassemblerContextPluginInit>(facts, "plugin_type6_init");
        AddLayout<DisassemblerRenderLinePluginInit>(facts, "plugin_type7_init");
        AddLayout<AutoAssemblerPluginInit>(facts, "plugin_type8_init");
        AddLayout<RegisterModificationInfo>(facts, "register_modification_info");
        AddLayout<ExportedFunctionsPrefix>(facts, "exported_functions_prefix");

        PluginVersion pluginVersion = default;
        void* pluginVersionOrigin = &pluginVersion;
        AddOffset(facts, "plugin_version.Version", Layout.OffsetOf(pluginVersionOrigin, &pluginVersion.Version));
        AddOffset(facts, "plugin_version.PluginName", Layout.OffsetOf(pluginVersionOrigin, &pluginVersion.PluginName));

        PluginType0Record pluginType0Record = default;
        void* pluginType0RecordOrigin = &pluginType0Record;
        AddOffset(facts, "plugin_type0_record.InterpretedAddress",
            Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.InterpretedAddress));
        AddOffset(facts, "plugin_type0_record.Address", Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.Address));
        AddOffset(facts, "plugin_type0_record.IsPointer", Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.IsPointer));
        AddOffset(facts, "plugin_type0_record.CountOffsets",
            Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.CountOffsets));
        AddOffset(facts, "plugin_type0_record.Offsets", Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.Offsets));
        AddOffset(facts, "plugin_type0_record.Description",
            Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.Description));
        AddOffset(facts, "plugin_type0_record.ValueType", Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.ValueType));
        AddOffset(facts, "plugin_type0_record.Size", Layout.OffsetOf(pluginType0RecordOrigin, &pluginType0Record.Size));

        AddPluginInitOffsets(facts);
        AddRegisterModificationOffsets(facts);
        AddExportedFunctionsPrefixOffsets(facts);
        return facts;
    }

    private static void AddPluginInitOffsets(Dictionary<string, string> facts)
    {
        AddressListPluginInit type0 = default;
        void* type0Origin = &type0;
        AddOffset(facts, "plugin_type0_init.Name", Layout.OffsetOf(type0Origin, &type0.Name));
        AddOffset(facts, "plugin_type0_init.Callback", Layout.OffsetOf(type0Origin, &type0.Callback));

        MemoryViewPluginInit type1 = default;
        void* type1Origin = &type1;
        AddOffset(facts, "plugin_type1_init.Name", Layout.OffsetOf(type1Origin, &type1.Name));
        AddOffset(facts, "plugin_type1_init.Callback", Layout.OffsetOf(type1Origin, &type1.Callback));
        AddOffset(facts, "plugin_type1_init.Shortcut", Layout.OffsetOf(type1Origin, &type1.Shortcut));

        DebugEventPluginInit type2 = default;
        AddOffset(facts, "plugin_type2_init.Callback", Layout.OffsetOf(&type2, &type2.Callback));

        ProcessWatcherPluginInit type3 = default;
        AddOffset(facts, "plugin_type3_init.Callback", Layout.OffsetOf(&type3, &type3.Callback));

        FunctionPointerChangePluginInit type4 = default;
        AddOffset(facts, "plugin_type4_init.Callback", Layout.OffsetOf(&type4, &type4.Callback));

        MainMenuPluginInit type5 = default;
        void* type5Origin = &type5;
        AddOffset(facts, "plugin_type5_init.Name", Layout.OffsetOf(type5Origin, &type5.Name));
        AddOffset(facts, "plugin_type5_init.Callback", Layout.OffsetOf(type5Origin, &type5.Callback));
        AddOffset(facts, "plugin_type5_init.Shortcut", Layout.OffsetOf(type5Origin, &type5.Shortcut));

        DisassemblerContextPluginInit type6 = default;
        void* type6Origin = &type6;
        AddOffset(facts, "plugin_type6_init.Name", Layout.OffsetOf(type6Origin, &type6.Name));
        AddOffset(facts, "plugin_type6_init.Callback", Layout.OffsetOf(type6Origin, &type6.Callback));
        AddOffset(facts, "plugin_type6_init.CallbackOnPopup", Layout.OffsetOf(type6Origin, &type6.CallbackOnPopup));
        AddOffset(facts, "plugin_type6_init.Shortcut", Layout.OffsetOf(type6Origin, &type6.Shortcut));

        DisassemblerRenderLinePluginInit type7 = default;
        AddOffset(facts, "plugin_type7_init.Callback", Layout.OffsetOf(&type7, &type7.Callback));

        AutoAssemblerPluginInit type8 = default;
        AddOffset(facts, "plugin_type8_init.Callback", Layout.OffsetOf(&type8, &type8.Callback));
    }

    private static void AddRegisterModificationOffsets(Dictionary<string, string> facts)
    {
        RegisterModificationInfo info = default;
        void* origin = &info;
        AddOffset(facts, "register_modification_info.Address", Layout.OffsetOf(origin, &info.Address));
        AddOffset(facts, "register_modification_info.ChangeEax", Layout.OffsetOf(origin, &info.ChangeEax));
        AddOffset(facts, "register_modification_info.ChangeR15", Layout.OffsetOf(origin, &info.ChangeR15));
        AddOffset(facts, "register_modification_info.ChangeOf", Layout.OffsetOf(origin, &info.ChangeOf));
        AddOffset(facts, "register_modification_info.NewEax", Layout.OffsetOf(origin, &info.NewEax));
        AddOffset(facts, "register_modification_info.NewR15", Layout.OffsetOf(origin, &info.NewR15));
        AddOffset(facts, "register_modification_info.NewCf", Layout.OffsetOf(origin, &info.NewCf));
        AddOffset(facts, "register_modification_info.NewOf", Layout.OffsetOf(origin, &info.NewOf));
    }

    private static void AddExportedFunctionsPrefixOffsets(Dictionary<string, string> facts)
    {
        ExportedFunctionsPrefix exports = default;
        void* origin = &exports;
        AddOffset(facts, "exported_functions_prefix.SizeOfExportedFunctions",
            Layout.OffsetOf(origin, &exports.SizeOfExportedFunctions));
        AddOffset(facts, "exported_functions_prefix.ShowMessage", Layout.OffsetOf(origin, &exports.ShowMessage));
        AddOffset(facts, "exported_functions_prefix.RegisterFunction", Layout.OffsetOf(origin, &exports.RegisterFunction));
        AddOffset(facts, "exported_functions_prefix.UnregisterFunction",
            Layout.OffsetOf(origin, &exports.UnregisterFunction));
        AddOffset(facts, "exported_functions_prefix.OpenedProcessId", Layout.OffsetOf(origin, &exports.OpenedProcessId));
        AddOffset(facts, "exported_functions_prefix.OpenedProcessHandle",
            Layout.OffsetOf(origin, &exports.OpenedProcessHandle));
        AddOffset(facts, "exported_functions_prefix.GetMainWindowHandle",
            Layout.OffsetOf(origin, &exports.GetMainWindowHandle));
        AddOffset(facts, "exported_functions_prefix.AutoAssemble", Layout.OffsetOf(origin, &exports.AutoAssemble));
        AddOffset(facts, "exported_functions_prefix.Assembler", Layout.OffsetOf(origin, &exports.Assembler));
        AddOffset(facts, "exported_functions_prefix.Disassembler", Layout.OffsetOf(origin, &exports.Disassembler));
        AddOffset(facts, "exported_functions_prefix.ChangeRegistersAtAddress",
            Layout.OffsetOf(origin, &exports.ChangeRegistersAtAddress));
        AddOffset(facts, "exported_functions_prefix.InjectDll", Layout.OffsetOf(origin, &exports.InjectDll));
        AddOffset(facts, "exported_functions_prefix.FreezeMemory", Layout.OffsetOf(origin, &exports.FreezeMemory));
        AddOffset(facts, "exported_functions_prefix.UnfreezeMemory", Layout.OffsetOf(origin, &exports.UnfreezeMemory));
        AddOffset(facts, "exported_functions_prefix.FixMemory", Layout.OffsetOf(origin, &exports.FixMemory));
        AddOffset(facts, "exported_functions_prefix.ProcessList", Layout.OffsetOf(origin, &exports.ProcessList));
        AddOffset(facts, "exported_functions_prefix.ReloadSettings", Layout.OffsetOf(origin, &exports.ReloadSettings));
        AddOffset(facts, "exported_functions_prefix.GetAddressFromPointer",
            Layout.OffsetOf(origin, &exports.GetAddressFromPointer));
    }

    private static void AddLayout<T>(Dictionary<string, string> facts, string key)
        where T : unmanaged
    {
        facts.Add($"sizeof.{key}", Layout.SizeOf<T>().ToString(CultureInfo.InvariantCulture));
        facts.Add($"alignof.{key}", Layout.AlignmentOf<T>().ToString(CultureInfo.InvariantCulture));
    }

    private static void AddOffset(Dictionary<string, string> facts, string key, int value)
    {
        facts.Add($"offsetof.{key}", value.ToString(CultureInfo.InvariantCulture));
    }
}

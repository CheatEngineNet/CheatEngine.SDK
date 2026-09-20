using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>
///     CE 7.7-shaped table fixtures for the protected inspection boundary. These are deterministic Lua fixtures, not a
///     claim that a live Cheat Engine process was attached.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class EngineInspectionTests
{
    [Fact]
    public void EnumerateModules_current_and_explicit_process_copy_ce77_fields_and_preserve_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          enumModules = function(pid)
                            if pid == nil then
                              return {
                                { Name = 'game.exe', Address = 0x140000000, Size = 0x320000, Is64Bit = true, PathToFile = 'C:/games/game.exe' },
                                { Name = 'xinput1_4.dll', Address = 0x180000000, Size = 0x12000, Is64Bit = true, PathToFile = 'C:/Windows/System32/xinput1_4.dll' }
                              }
                            end
                            if pid == 4242 then
                              return { { Name = 'other.exe', Address = 0x400000, Size = 0x1000, Is64Bit = false, PathToFile = 'C:/other.exe' } }
                            end
                            error('unexpected process id')
                          end
                          """u8);

        var top = L.Top;
        var current = new ModuleInfo[2];
        var status = EngineInspection.EnumerateModules(current, out var currentCount);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(2, currentCount);
        Assert.Equal("game.exe", current[0].Name);
        Assert.Equal(0x140000000UL, current[0].BaseAddress.Value);
        Assert.True(current[0].ImageSize.HasValue);
        Assert.Equal(0x320000UL, current[0].ImageSize.GetValueOrDefault().Value);
        Assert.True(current[0].Is64Bit);
        Assert.Equal("C:/games/game.exe", current[0].PathToFile);
        Assert.Equal(top, L.Top);

        var explicitProcess = new ModuleInfo[1];
        status = EngineInspection.EnumerateModules(new TargetProcessId(4242), explicitProcess, out var explicitCount);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(1, explicitCount);
        Assert.Equal("other.exe", explicitProcess[0].Name);
        Assert.False(explicitProcess[0].Is64Bit);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void EnumerateModules_without_size_returns_a_null_image_size()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          enumModules = function()
                            return {
                              { Name = 'game.exe', Address = 0x140000000, Is64Bit = true, PathToFile = 'C:/games/game.exe' }
                            }
                          end
                          """u8);

        var modules = new ModuleInfo[1];
        var top = L.Top;
        var status = EngineInspection.EnumerateModules(modules, out var written);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(1, written);
        Assert.Equal("game.exe", modules[0].Name);
        Assert.Null(modules[0].ImageSize);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void EnumerateModules_destination_too_small_writes_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          enumModules = function()
                            return {
                              { Name = 'one', Address = 1, Size = 1, Is64Bit = false, PathToFile = 'one' },
                              { Name = 'two', Address = 2, Size = 2, Is64Bit = false, PathToFile = 'two' }
                            }
                          end
                          """u8);

        ModuleInfo sentinel = new("sentinel", 0x10, new MemorySize(4), Is64Bit: false, "sentinel");
        ModuleInfo[] destination = [sentinel];
        var top = L.Top;
        var status = EngineInspection.EnumerateModules(destination, out var written);

        Assert.Equal(InspectionStatus.DestinationTooSmall, status);
        Assert.Equal(0, written);
        Assert.Equal(sentinel, destination[0]);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void EnumerateModules_malformed_later_entry_writes_no_partial_snapshot()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          enumModules = function()
                            return {
                              { Name = 'valid', Address = 1, Size = 1, Is64Bit = false, PathToFile = 'valid' },
                              { Name = 'bad', Address = 2, Size = 2, Is64Bit = 'not-a-boolean', PathToFile = 'bad' }
                            }
                          end
                          """u8);

        ModuleInfo first = new("first", 0x10, new MemorySize(1), Is64Bit: false, "first");
        ModuleInfo second = new("second", 0x20, new MemorySize(2), Is64Bit: true, "second");
        ModuleInfo[] destination = [first, second];
        var top = L.Top;
        var status = EngineInspection.EnumerateModules(destination, out var written);

        Assert.Equal(InspectionStatus.InvalidResult, status);
        Assert.Equal(0, written);
        Assert.Equal(first, destination[0]);
        Assert.Equal(second, destination[1]);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void EnumerateSections_address_and_name_selectors_copy_file_offsets_without_confusing_them_with_addresses()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          enumSectionsOfModule = function(selector)
                            if selector == 0x140000000 then
                              return { { Name = '.text', Size = 0x5000, Address = 0x140001000, FileAddress = 0x1000 } }
                            end
                            if selector == 'game.exe' then
                              return { { Name = '.rdata', Size = 0x2000, Address = 0x140006000, FileAddress = 0x6000 } }
                            end
                            error('unexpected selector')
                          end
                          """u8);

        var sections = new ModuleSectionInfo[1];
        var top = L.Top;
        var status = EngineInspection.EnumerateSections(0x140000000UL, sections, out var written);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(1, written);
        Assert.Equal(".text", sections[0].Name);
        Assert.Equal(0x140001000UL, sections[0].Address.Value);
        Assert.Equal(0x1000UL, sections[0].FileOffset.Value);
        Assert.Equal(top, L.Top);

        status = EngineInspection.EnumerateSections(new ModuleName("game.exe"), sections, out written);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(1, written);
        Assert.Equal(".rdata", sections[0].Name);
        Assert.Equal(0x6000UL, sections[0].FileOffset.Value);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void ResolveAddress_distinguishes_a_nil_miss_from_a_zero_address_and_lua_failure()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          getAddressSafe = function(expression, localFlag, shallow)
                            if expression == 'zero' and not localFlag and not shallow then return 0 end
                            if expression == 'hostSymbol' and localFlag and shallow then return 0x7FF600001000 end
                            if expression == 'broken' then error('symbol handler unavailable') end
                            return nil
                          end
                          """u8);

        var top = L.Top;
        var status = EngineInspection.ResolveAddress(new SymbolExpression("zero"), default, out var zero);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(Address.Zero, zero);
        Assert.Equal(top, L.Top);

        status = EngineInspection.ResolveHostAddress(new SymbolExpression("hostSymbol"),
            new AddressResolutionOptions(Shallow: true), out var found);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(unchecked((nuint)0x7FF600001000UL), found.Value);
        Assert.Equal(top, L.Top);

        status = EngineInspection.ResolveAddress(new SymbolExpression("missing"), default, out var missing);

        Assert.Equal(InspectionStatus.NotFound, status);
        Assert.Equal(Address.Zero, missing);
        Assert.Equal(top, L.Top);

        status = EngineInspection.ResolveAddress(new SymbolExpression("broken"), default, out var failed);

        Assert.Equal(InspectionStatus.LuaFailure, status);
        Assert.Equal(Address.Zero, failed);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void ResolveAddress_reports_an_unavailable_global_without_entering_lua()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var top = L.Top;

        var status = EngineInspection.ResolveAddress(new SymbolExpression("missingGlobal"), default, out var address);

        Assert.Equal(InspectionStatus.GlobalUnavailable, status);
        Assert.Equal(Address.Zero, address);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void Legacy_positional_host_option_is_preserved_but_rejected_by_target_resolution()
    {
        var legacy = new AddressResolutionOptions(true);
        legacy.Deconstruct(out var useHostSymbolTable, out var shallow);

        Assert.True(useHostSymbolTable);
        Assert.False(shallow);
        Assert.Throws<ArgumentException>(() =>
            EngineInspection.ResolveAddress(new SymbolExpression("hostSymbol"), legacy, out _));
    }

    [Fact]
    public void Inspection_preserves_lua_failures_from_global_and_table_field_resolution()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          setmetatable(_G, { __index = function(_, key)
                            if key == 'getAddressSafe' then error('global lookup failed') end
                          end })
                          getSymbolInfo = function()
                            return setmetatable({}, { __index = function() error('field lookup failed') end })
                          end
                          """u8);

        var top = L.Top;
        var status = EngineInspection.ResolveAddress(new SymbolExpression("any"), default, out var address);
        Assert.Equal(InspectionStatus.LuaFailure, status);
        Assert.Equal(Address.Zero, address);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetSymbolInfo(new SymbolExpression("any"), out var symbol);
        Assert.Equal(InspectionStatus.LuaFailure, status);
        Assert.Equal(default, symbol);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void GetSymbolInfo_uses_the_symbol_list_canonical_symbolsize_field_and_preserves_nil_absence()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          getSymbolInfo = function(expression)
                            if expression == 'Game.Update' then
                              return { modulename = 'game.exe', searchkey = 'Game.Update', address = 0x140012340, symbolsize = 42 }
                            end
                            if expression == 'malformed' then return { modulename = 'game.exe', searchkey = 'bad', address = 1, size = 42 } end
                            return nil
                          end
                          """u8);

        var top = L.Top;
        var status = EngineInspection.GetSymbolInfo(new SymbolExpression("Game.Update"), out var symbol);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal("game.exe", symbol.ModuleName);
        Assert.Equal("Game.Update", symbol.SearchKey);
        Assert.Equal(0x140012340UL, symbol.Address.Value);
        Assert.Equal(42UL, symbol.Size.Value);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetSymbolInfo(new SymbolExpression("missing"), out symbol);
        Assert.Equal(InspectionStatus.NotFound, status);
        Assert.Equal(default, symbol);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetSymbolInfo(new SymbolExpression("malformed"), out symbol);
        Assert.Equal(InspectionStatus.InvalidResult, status);
        Assert.Equal(default, symbol);
        Assert.Equal(top, L.Top);
    }

    [Fact]
    public void Memory_regions_copy_windows_fields_optional_extra_and_reject_malformed_or_nil_single_records()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        EngineTest.Run(L, """
                          local function region(base, extra)
                            return {
                              BaseAddress = base, AllocationBase = base, AllocationProtect = 0x04,
                              RegionSize = 0x2000, State = 0x1000, Protect = 0x20, Type = 0x1000000, Extra = extra
                            }
                          end
                          enumMemoryRegions = function() return { region(0x140000000, 'C:/game.exe'), region(0x150000000, nil) } end
                          getMemoryRegionInfo = function(address)
                            if address == 0x140000000 then return region(address, 'C:/game.exe') end
                            if address == 0x1234 then return { BaseAddress = 1 } end
                            return nil
                          end
                          """u8);

        var regions = new MemoryRegionInfo[2];
        var top = L.Top;
        var status = EngineInspection.EnumerateMemoryRegions(regions, out var written);

        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(2, written);
        Assert.Equal(0x140000000UL, regions[0].BaseAddress.Value);
        Assert.Equal(0x2000UL, regions[0].Size.Value);
        Assert.Equal(MemoryRegionState.Committed, regions[0].State);
        Assert.Equal(MemoryRegionType.Image, regions[0].Type);
        Assert.Equal("C:/game.exe", regions[0].Extra);
        Assert.Null(regions[1].Extra);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetMemoryRegionInfo(0x140000000UL, out var one);
        Assert.Equal(InspectionStatus.Success, status);
        Assert.Equal(regions[0], one);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetMemoryRegionInfo(0x1234UL, out one);
        Assert.Equal(InspectionStatus.InvalidResult, status);
        Assert.Equal(default, one);
        Assert.Equal(top, L.Top);

        status = EngineInspection.GetMemoryRegionInfo(0x9999UL, out one);
        Assert.Equal(InspectionStatus.InvalidResult, status);
        Assert.Equal(default, one);
        Assert.Equal(top, L.Top);
    }
}

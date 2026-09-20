using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     Protected inspection calls for CE 7.7 modules, sections, symbols, address resolution and memory regions.
/// </summary>
/// <remarks>
///     <para>
///         <b>Provenance and compatibility.</b> The global names, argument order, result tables and nil semantics come
///         from the exact Cheat Engine 7.7.0.10621 <c>celua.txt</c> (SHA-256
///         <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>): lines 132-133,
///         167-182 and 204-208, plus the <c>SymbolList</c> class entry. The operations target the Windows x64 CE 7.7
///         host. They are deliberately scoped to that tested contract and do not normalize later host changes.
///     </para>
///     <para>
///         <b>Threading and lifecycle.</b> The CE 7.7 <c>celua.txt</c> entries for these globals do not declare
///         main-thread affinity. These methods therefore do not assert a main-thread contract, dispatch, or synchronize;
///         they use the calling thread's host-provided Lua state and require an enabled plugin. A main-thread annotation
///         may be added only after a live probe or primary host source establishes that requirement.
///     </para>
///     <para>
///         <b>Ownership and failure.</b> Results are managed snapshots and own no Lua registry reference, CE object,
///         target handle or buffer. Every global is resolved through an attach-epoch-aware <see cref="LuaRef" /> and every
///         call is protected. The original Lua stack height is restored on success, Lua failure, malformed result and
///         push failure. <see cref="InspectionStatus.NotFound" /> is reserved for the documented nil result; all other
///         failures stay distinct through <see cref="InspectionStatus" />.
///     </para>
///     <para>
///         <b>Allocation.</b> Enumeration is intentionally a cold snapshot operation: it allocates managed strings for
///         CE table fields and one temporary array so a malformed later entry never publishes a partial destination.
///         Symbol and single-region queries allocate only the returned strings. None of these APIs is a memory-read hot
///         path; callers that need scalar throughput should use the dedicated target-memory surface.
///     </para>
/// </remarks>
public static class EngineInspection
{
    private static readonly LuaRef SEnumMemoryRegions = new();
    private static readonly LuaRef SGetMemoryRegionInfo = new();
    private static readonly LuaRef SEnumModules = new();
    private static readonly LuaRef SEnumSectionsOfModule = new();
    private static readonly LuaRef SGetAddressSafe = new();
    private static readonly LuaRef SGetSymbolInfo = new();

    /// <summary>
    ///     Copies the current target's module table into <paramref name="destination" />.
    /// </summary>
    /// <param name="destination">The destination for copied module snapshots.</param>
    /// <param name="written">The total module count on success; 0 for any other status.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" />, <see cref="InspectionStatus.DestinationTooSmall" /> before any
    ///     element is written, or a binding failure. CE documents the name, address, bitness and file path. A host that
    ///     additionally supplies <c>Size</c> is represented by a non-null <see cref="ModuleInfo.ImageSize" />.
    /// </returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus EnumerateModules(Span<ModuleInfo> destination, out int written)
    {
        return EnumerateModulesCore(destination, out written, processId: default, hasProcessId: false);
    }

    /// <summary>
    ///     Copies the specified process's module table into <paramref name="destination" />.
    /// </summary>
    /// <param name="processId">The positive process identifier passed as CE's optional <c>processid</c> argument.</param>
    /// <param name="destination">The destination for copied module snapshots.</param>
    /// <param name="written">The total module count on success; 0 for any other status.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" />, <see cref="InspectionStatus.DestinationTooSmall" /> before any
    ///     element is written, or a binding failure.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="processId" /> is the default or otherwise non-positive
    ///     identifier.
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus EnumerateModules(TargetProcessId processId, Span<ModuleInfo> destination,
        out int written)
    {
        ValidateProcessId(processId);
        return EnumerateModulesCore(destination, out written, processId: processId, hasProcessId: true);
    }

    /// <summary>
    ///     Copies the sections of the module loaded at <paramref name="moduleBase" /> into
    ///     <paramref name="destination" />.
    /// </summary>
    /// <param name="moduleBase">The target-process base address passed as the first form of CE's module selector.</param>
    /// <param name="destination">The destination for copied section snapshots.</param>
    /// <param name="written">The total section count on success; 0 for any other status.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" />, <see cref="InspectionStatus.DestinationTooSmall" /> before any
    ///     element is written, or a binding failure. CE 7.7 documents the returned <c>Name</c>, <c>Size</c>,
    ///     <c>Address</c> and <c>FileAddress</c> fields.
    /// </returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus EnumerateSections(Address moduleBase, Span<ModuleSectionInfo> destination,
        out int written)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        written = 0;
        try
        {
            var status = PushGlobal(state, SEnumSectionsOfModule, "enumSectionsOfModule"u8);
            if (status != InspectionStatus.Success) return status;

            Address.Push(state, moduleBase);
            return ReadSectionCollectionAfterCall(state, 1, destination, out written);
        }
        catch (LuaException)
        {
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>Copies the sections of the named module into <paramref name="destination" />.</summary>
    /// <param name="moduleName">The module-name form of CE's selector; it is forwarded as UTF-8 without normalization.</param>
    /// <param name="destination">The destination for copied section snapshots.</param>
    /// <param name="written">The total section count on success; 0 for any other status.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" />, <see cref="InspectionStatus.DestinationTooSmall" /> before any
    ///     element is written, or a binding failure. A default or empty <paramref name="moduleName" /> is rejected before
    ///     Lua is entered.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="moduleName" /> has no usable module name.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus EnumerateSections(ModuleName moduleName, Span<ModuleSectionInfo> destination,
        out int written)
    {
        ValidateModuleName(moduleName);
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        written = 0;
        try
        {
            var status = PushGlobal(state, SEnumSectionsOfModule, "enumSectionsOfModule"u8);
            if (status != InspectionStatus.Success) return status;

            state.PushString(moduleName.Value.AsSpan());
            return ReadSectionCollectionAfterCall(state, 1, destination, out written);
        }
        catch (LuaException)
        {
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>Resolves a symbol expression with CE's non-throwing <c>getAddressSafe</c> global.</summary>
    /// <param name="expression">The non-empty expression supplied to CE's symbol handler.</param>
    /// <param name="options">The optional CE lookup flags, forwarded without managed reinterpretation.</param>
    /// <param name="address">The resolved target address on success; <see cref="Address.Zero" /> otherwise.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" /> when the result is an address, <see cref="InspectionStatus.NotFound" />
    ///     only when CE returns Lua <c>nil</c>, or a distinct binding failure.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> has no usable symbol expression.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
        out Address address)
    {
        ValidateSymbolExpression(expression);
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        address = Address.Zero;
        try
        {
            var status = PushGlobal(state, SGetAddressSafe, "getAddressSafe"u8);
            if (status != InspectionStatus.Success) return status;

            state.PushString(expression.Value.AsSpan());
            state.PushBoolean(options.UseHostSymbolTable);
            state.PushBoolean(options.Shallow);
            if (!state.TryCall(3, 1).IsOk) return InspectionStatus.LuaFailure;
            if (state.IsNil(-1)) return InspectionStatus.NotFound;
            return Address.TryRead(state, -1, out address)
                ? InspectionStatus.Success
                : InspectionStatus.InvalidResult;
        }
        catch (LuaException)
        {
            address = Address.Zero;
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>Copies the metadata of a symbol from CE's <c>getSymbolInfo</c> table.</summary>
    /// <param name="expression">The non-empty symbol expression passed to Cheat Engine.</param>
    /// <param name="symbol">The copied symbol information on success; <see langword="default" /> otherwise.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.NotFound" /> only for CE's Lua <c>nil</c>; a malformed non-nil table remains
    ///     <see cref="InspectionStatus.InvalidResult" />.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> has no usable symbol expression.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus GetSymbolInfo(SymbolExpression expression, out SymbolInfo symbol)
    {
        ValidateSymbolExpression(expression);
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        symbol = default;
        try
        {
            var status = PushGlobal(state, SGetSymbolInfo, "getSymbolInfo"u8);
            if (status != InspectionStatus.Success) return status;

            state.PushString(expression.Value.AsSpan());
            if (!state.TryCall(1, 1).IsOk) return InspectionStatus.LuaFailure;
            if (state.IsNil(-1)) return InspectionStatus.NotFound;
            return state.IsTable(-1) && TryReadSymbolInfo(state, -1, out symbol)
                ? InspectionStatus.Success
                : InspectionStatus.InvalidResult;
        }
        catch (LuaException)
        {
            symbol = default;
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>Copies CE's complete virtual-memory layout into <paramref name="destination" />.</summary>
    /// <param name="destination">The destination for copied memory-region snapshots.</param>
    /// <param name="written">The total region count on success; 0 for any other status.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" />, <see cref="InspectionStatus.DestinationTooSmall" /> before any
    ///     element is written, or a binding failure. CE represents mapped-file metadata by an optional <c>Extra</c> field.
    /// </returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus EnumerateMemoryRegions(Span<MemoryRegionInfo> destination, out int written)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        written = 0;
        try
        {
            var status = PushGlobal(state, SEnumMemoryRegions, "enumMemoryRegions"u8);
            if (status != InspectionStatus.Success) return status;

            return ReadMemoryRegionCollectionAfterCall(state, 0, destination, out written);
        }
        catch (LuaException)
        {
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    /// <summary>Copies the memory-region record containing <paramref name="address" />.</summary>
    /// <param name="address">The target-process address passed to CE.</param>
    /// <param name="region">The copied region snapshot on success; <see langword="default" /> otherwise.</param>
    /// <returns>
    ///     <see cref="InspectionStatus.Success" /> only for a complete CE table. The CE 7.7 text does not document
    ///     a nil absence return for <c>getMemoryRegionInfo</c>, so a nil result is
    ///     <see cref="InspectionStatus.InvalidResult" />.
    /// </returns>
    /// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
    [RequiresPluginEnabled]
    public static InspectionStatus GetMemoryRegionInfo(Address address, out MemoryRegionInfo region)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        region = default;
        try
        {
            var status = PushGlobal(state, SGetMemoryRegionInfo, "getMemoryRegionInfo"u8);
            if (status != InspectionStatus.Success) return status;

            Address.Push(state, address);
            if (!state.TryCall(1, 1).IsOk) return InspectionStatus.LuaFailure;
            return state.IsTable(-1) && TryReadMemoryRegionInfo(state, -1, out region)
                ? InspectionStatus.Success
                : InspectionStatus.InvalidResult;
        }
        catch (LuaException)
        {
            region = default;
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static InspectionStatus EnumerateModulesCore(Span<ModuleInfo> destination, out int written,
        TargetProcessId processId, bool hasProcessId)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var state = operation.State;
        var top = state.Top;
        written = 0;
        try
        {
            var status = PushGlobal(state, SEnumModules, "enumModules"u8);
            if (status != InspectionStatus.Success) return status;

            if (hasProcessId) state.PushInteger(processId.Value);
            if (!state.TryCall(hasProcessId ? 1 : 0, 1).IsOk) return InspectionStatus.LuaFailure;
            return ReadModuleCollection(state, -1, destination, out written);
        }
        catch (LuaException)
        {
            return InspectionStatus.LuaFailure;
        }
        finally
        {
            state.SetTop(top);
        }
    }

    private static InspectionStatus ReadSectionCollectionAfterCall(LuaState state, int argumentCount,
        Span<ModuleSectionInfo> destination, out int written)
    {
        if (!state.TryCall(argumentCount, 1).IsOk)
        {
            written = 0;
            return InspectionStatus.LuaFailure;
        }

        return ReadSectionCollection(state, -1, destination, out written);
    }

    private static InspectionStatus ReadMemoryRegionCollectionAfterCall(LuaState state, int argumentCount,
        Span<MemoryRegionInfo> destination, out int written)
    {
        if (!state.TryCall(argumentCount, 1).IsOk)
        {
            written = 0;
            return InspectionStatus.LuaFailure;
        }

        return ReadMemoryRegionCollection(state, -1, destination, out written);
    }

    private static InspectionStatus ReadModuleCollection(LuaState state, int tableIndex, Span<ModuleInfo> destination,
        out int written)
    {
        written = 0;
        if (!state.IsTable(tableIndex)) return InspectionStatus.InvalidResult;
        var count = TryGetSequenceCount(state, tableIndex, out var sequenceCount);
        if (count != InspectionStatus.Success) return count;
        if (sequenceCount > destination.Length) return InspectionStatus.DestinationTooSmall;

        if (sequenceCount == 0) return InspectionStatus.Success;

        var snapshot = new ModuleInfo[sequenceCount];
        for (var index = 0; index < sequenceCount; index++)
        {
            if (state.RawGetSequenceItem(tableIndex, index) != LuaType.Table ||
                !TryReadModuleInfo(state, -1, out snapshot[index]))
                return InspectionStatus.InvalidResult;

            state.Pop(1);
        }

        snapshot.AsSpan().CopyTo(destination);
        written = sequenceCount;
        return InspectionStatus.Success;
    }

    private static InspectionStatus ReadSectionCollection(LuaState state, int tableIndex,
        Span<ModuleSectionInfo> destination, out int written)
    {
        written = 0;
        if (!state.IsTable(tableIndex)) return InspectionStatus.InvalidResult;
        var count = TryGetSequenceCount(state, tableIndex, out var sequenceCount);
        if (count != InspectionStatus.Success) return count;
        if (sequenceCount > destination.Length) return InspectionStatus.DestinationTooSmall;

        if (sequenceCount == 0) return InspectionStatus.Success;

        var snapshot = new ModuleSectionInfo[sequenceCount];
        for (var index = 0; index < sequenceCount; index++)
        {
            if (state.RawGetSequenceItem(tableIndex, index) != LuaType.Table ||
                !TryReadModuleSectionInfo(state, -1, out snapshot[index]))
                return InspectionStatus.InvalidResult;

            state.Pop(1);
        }

        snapshot.AsSpan().CopyTo(destination);
        written = sequenceCount;
        return InspectionStatus.Success;
    }

    private static InspectionStatus ReadMemoryRegionCollection(LuaState state, int tableIndex,
        Span<MemoryRegionInfo> destination, out int written)
    {
        written = 0;
        if (!state.IsTable(tableIndex)) return InspectionStatus.InvalidResult;
        var count = TryGetSequenceCount(state, tableIndex, out var sequenceCount);
        if (count != InspectionStatus.Success) return count;
        if (sequenceCount > destination.Length) return InspectionStatus.DestinationTooSmall;

        if (sequenceCount == 0) return InspectionStatus.Success;

        var snapshot = new MemoryRegionInfo[sequenceCount];
        for (var index = 0; index < sequenceCount; index++)
        {
            if (state.RawGetSequenceItem(tableIndex, index) != LuaType.Table ||
                !TryReadMemoryRegionInfo(state, -1, out snapshot[index]))
                return InspectionStatus.InvalidResult;

            state.Pop(1);
        }

        snapshot.AsSpan().CopyTo(destination);
        written = sequenceCount;
        return InspectionStatus.Success;
    }

    private static InspectionStatus TryGetSequenceCount(LuaState state, int tableIndex, out int count)
    {
        try
        {
            count = checked((int)state.RawLength(tableIndex));
            return InspectionStatus.Success;
        }
        catch (OverflowException)
        {
            count = 0;
            return InspectionStatus.InvalidResult;
        }
    }

    private static bool TryReadModuleInfo(LuaState state, int tableIndex, out ModuleInfo module)
    {
        module = default;
        if (!TryReadRequiredStringField(state, tableIndex, "Name"u8, out var name) ||
            !TryReadAddressField(state, tableIndex, "Address"u8, out var address) ||
            !TryReadOptionalMemorySizeField(state, tableIndex, "Size"u8, out var size) ||
            !TryReadBooleanField(state, tableIndex, "Is64Bit"u8, out var is64Bit) ||
            !TryReadRequiredStringField(state, tableIndex, "PathToFile"u8, out var pathToFile))
            return false;

        module = new ModuleInfo(name, address, size, is64Bit, pathToFile);
        return true;
    }

    private static bool TryReadModuleSectionInfo(LuaState state, int tableIndex, out ModuleSectionInfo section)
    {
        section = default;
        if (!TryReadRequiredStringField(state, tableIndex, "Name"u8, out var name) ||
            !TryReadMemorySizeField(state, tableIndex, "Size"u8, out var size) ||
            !TryReadAddressField(state, tableIndex, "Address"u8, out var address) ||
            !TryReadUInt64Field(state, tableIndex, "FileAddress"u8, out var fileAddress))
            return false;

        section = new ModuleSectionInfo(name, size, address, new ModuleFileOffset(fileAddress));
        return true;
    }

    private static bool TryReadSymbolInfo(LuaState state, int tableIndex, out SymbolInfo symbol)
    {
        symbol = default;
        if (!TryReadRequiredStringField(state, tableIndex, "modulename"u8, out var moduleName) ||
            !TryReadRequiredStringField(state, tableIndex, "searchkey"u8, out var searchKey) ||
            !TryReadAddressField(state, tableIndex, "address"u8, out var address) ||
            !TryReadMemorySizeField(state, tableIndex, "symbolsize"u8, out var size))
            return false;

        symbol = new SymbolInfo(moduleName, searchKey, address, size);
        return true;
    }

    private static bool TryReadMemoryRegionInfo(LuaState state, int tableIndex, out MemoryRegionInfo region)
    {
        region = default;
        if (!TryReadAddressField(state, tableIndex, "BaseAddress"u8, out var baseAddress) ||
            !TryReadAddressField(state, tableIndex, "AllocationBase"u8, out var allocationBase) ||
            !TryReadProtectionField(state, tableIndex, "AllocationProtect"u8, out var allocationProtection) ||
            !TryReadMemorySizeField(state, tableIndex, "RegionSize"u8, out var size) ||
            !TryReadUInt32Field(state, tableIndex, "State"u8, out var stateValue) ||
            !TryReadProtectionField(state, tableIndex, "Protect"u8, out var protection) ||
            !TryReadUInt32Field(state, tableIndex, "Type"u8, out var typeValue) ||
            !TryReadOptionalStringField(state, tableIndex, "Extra"u8, out var extra))
            return false;

        region = new MemoryRegionInfo(baseAddress, allocationBase, allocationProtection, size,
            (MemoryRegionState)stateValue, protection, (MemoryRegionType)typeValue, extra);
        return true;
    }

    private static bool TryReadRequiredStringField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        var read = state.TryReadString(-1, out value);
        state.Pop(1);
        return read;
    }

    private static bool TryReadOptionalStringField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
        out string? value)
    {
        value = null;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        if (state.IsNil(-1))
        {
            state.Pop(1);
            return true;
        }

        var read = state.TryReadString(-1, out value);
        state.Pop(1);
        return read;
    }

    private static bool TryReadAddressField(LuaState state, int tableIndex, ReadOnlySpan<byte> field, out Address value)
    {
        value = Address.Zero;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        var read = Address.TryRead(state, -1, out value);
        state.Pop(1);
        return read;
    }

    private static bool TryReadBooleanField(LuaState state, int tableIndex, ReadOnlySpan<byte> field, out bool value)
    {
        value = false;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        var read = state.TypeOf(-1) == LuaType.Boolean;
        if (read) value = state.ToBoolean(-1);
        state.Pop(1);
        return read;
    }

    private static bool TryReadMemorySizeField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
        out MemorySize value)
    {
        value = default;
        if (!TryReadUInt64Field(state, tableIndex, field, out var bytes)) return false;
        value = new MemorySize(bytes);
        return true;
    }

    private static bool TryReadOptionalMemorySizeField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
        out MemorySize? value)
    {
        value = null;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        if (state.IsNil(-1))
        {
            state.Pop(1);
            return true;
        }

        var read = state.TryReadInteger(-1, out var signed);
        state.Pop(1);
        if (!read || signed < 0) return false;
        value = new MemorySize((ulong)signed);
        return true;
    }

    private static bool TryReadProtectionField(LuaState state, int tableIndex, ReadOnlySpan<byte> field,
        out MemoryProtection value)
    {
        value = default;
        if (!TryReadUInt32Field(state, tableIndex, field, out var raw)) return false;
        value = (MemoryProtection)raw;
        return true;
    }

    private static bool TryReadUInt32Field(LuaState state, int tableIndex, ReadOnlySpan<byte> field, out uint value)
    {
        value = 0;
        if (!TryReadUInt64Field(state, tableIndex, field, out var raw) || raw > uint.MaxValue) return false;
        value = (uint)raw;
        return true;
    }

    private static bool TryReadUInt64Field(LuaState state, int tableIndex, ReadOnlySpan<byte> field, out ulong value)
    {
        value = 0;
        if (!state.TryGetField(tableIndex, field).IsOk) return false;
        var read = state.TryReadInteger(-1, out var signed);
        state.Pop(1);
        if (!read || signed < 0) return false;
        value = (ulong)signed;
        return true;
    }

    private static InspectionStatus PushGlobal(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
    {
        return LuaGlobalFunctions.TryPush(state, cache, name)
            ? InspectionStatus.Success
            : InspectionStatus.GlobalUnavailable;
    }

    private static void ValidateModuleName(ModuleName moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName.Value))
            throw new ArgumentException("A module name must not be empty or white space.", nameof(moduleName));
    }

    private static void ValidateProcessId(TargetProcessId processId)
    {
        if (processId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId.Value,
                "A target process identifier must be positive.");
    }

    private static void ValidateSymbolExpression(SymbolExpression expression)
    {
        if (string.IsNullOrWhiteSpace(expression.Value))
            throw new ArgumentException("A symbol expression must not be empty or white space.", nameof(expression));
    }
}

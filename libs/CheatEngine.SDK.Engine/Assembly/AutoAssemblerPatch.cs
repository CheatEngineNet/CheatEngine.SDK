using System;
using System.Threading;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     The sole owner of one successfully applied Auto Assembler patch and its CE disable-info table.
/// </summary>
/// <remarks>
///     <para>
///         The patch owns the exact Lua table returned by CE's successful <c>autoAssemble(script)</c> call. Calling
///         <see cref="Release" /> runs <c>autoAssemble(script, disableInfo)</c> once; <see cref="Dispose" /> makes
///         the same best-effort attempt for <see langword="using" /> cleanup. Either path consumes ownership before
///         starting Lua work. It therefore never repeats a disable after CE reports failure or after the state becomes
///         stale.
///     </para>
///     <para>
///         A Lua runtime detach, re-attach, or supported state replacement invalidates the disable-info reference.
///         In that case CE cannot safely execute the old <c>[DISABLE]</c> information. The owner is consumed and
///         <see cref="RequiresManualRecovery" /> becomes <see langword="true" /> rather than routing a stale table
///         into a new Lua state.
///     </para>
/// </remarks>
public sealed class AutoAssemblerPatch : IDisposable
{
    private readonly string _script;
    private readonly TargetProcessIncarnation _targetIncarnation;
    private LuaRef? _disableInfo;
    private TargetReleaseOutcome _lastReleaseOutcome;
    private int _requiresManualRecovery;

    internal AutoAssemblerPatch(string script, LuaRef disableInfo, TargetProcessIncarnation targetIncarnation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(disableInfo);
        _script = script;
        _disableInfo = disableInfo;
        _targetIncarnation = targetIncarnation;
    }

    /// <summary>
    ///     Gets whether the patch still owns a current CE disable-info table and can attempt target validation before a
    ///     normal disable.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            var disableInfo = Volatile.Read(ref _disableInfo);
            return LuaRuntime.IsAttached && disableInfo is not null && disableInfo.IsCurrent;
        }
    }

    /// <summary>
    ///     Gets whether CE may retain a partially-applied patch that this owner could not safely disable.
    /// </summary>
    /// <remarks>
    ///     This remains observable after cleanup consumes ownership. A value of <see langword="true" /> means a CE
    ///     failure, protected Lua failure, or stale state prevented a confirmed disable; it does not authorize a retry
    ///     with the old disable-info table.
    /// </remarks>
    public bool RequiresManualRecovery => Volatile.Read(ref _requiresManualRecovery) != 0;

    /// <summary>
    ///     Gets whether this owner was consumed by <see cref="Release" /> or <see cref="Dispose" />.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disableInfo) is null;

    /// <summary>Gets the copied process incarnation that was qualified when this patch was applied.</summary>
    public TargetProcessIncarnation TargetIncarnation => _targetIncarnation;

    /// <summary>Gets the factual result of the one disable attempt, including a safe target refusal.</summary>
    public TargetReleaseOutcome LastReleaseOutcome => _lastReleaseOutcome;

    /// <summary>
    ///     Disables the patch and observes failure.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner was already released or disposed.</exception>
    /// <exception cref="EngineOperationFailedException">Cheat Engine did not confirm that the disable completed.</exception>
    /// <exception cref="EngineTargetIdentityException">The patch target is no longer the current qualified target.</exception>
    /// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or not a function.</exception>
    /// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
    /// <exception cref="EngineMarshallingException">CE returned a non-boolean disable result.</exception>
    /// <remarks>
    ///     Ownership is consumed before the CE call. If the call returns false, raises, or cannot use an invalidated
    ///     state, <see cref="RequiresManualRecovery" /> is set and a future call cannot re-run <c>[DISABLE]</c>. A
    ///     mismatched or unavailable target is refused without selecting another target or invoking the disable script.
    /// </remarks>
    [RequiresPluginEnabled]
    public void Release()
    {
        var disableInfo = TakeOwnership();
        try
        {
            _lastReleaseOutcome = AutoAssemblerPatcher.TryDisable(_script, disableInfo, _targetIncarnation);
            if (_lastReleaseOutcome.Status == TargetReleaseStatus.Released) return;

            Volatile.Write(ref _requiresManualRecovery, 1);
            if (_lastReleaseOutcome.TargetCheck.HasValue)
                throw new EngineTargetIdentityException("AutoAssemblerDisable", _lastReleaseOutcome.TargetCheck.Value);

            throw new EngineOperationFailedException("AutoAssemblerDisable");
        }
        catch (EngineTargetIdentityException)
        {
            Volatile.Write(ref _requiresManualRecovery, 1);
            throw;
        }
        catch (EngineException exception)
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(exception.Kind);
            Volatile.Write(ref _requiresManualRecovery, 1);
            throw;
        }
        catch
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(failureKind: null);
            Volatile.Write(ref _requiresManualRecovery, 1);
            throw;
        }
    }

    /// <summary>
    ///     Best-effort, no-throw cleanup of this patch. It never retries after a CE, Lua, or lifecycle failure.
    /// </summary>
    [RequiresPluginEnabled]
    public void Dispose()
    {
        var disableInfo = Interlocked.Exchange(ref _disableInfo, null);
        if (disableInfo is null) return;

        try
        {
            _lastReleaseOutcome = AutoAssemblerPatcher.TryDisable(_script, disableInfo, _targetIncarnation);
            if (_lastReleaseOutcome.Status != TargetReleaseStatus.Released)
                Volatile.Write(ref _requiresManualRecovery, 1);
        }
        catch (EngineException exception)
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(exception.Kind);
            Volatile.Write(ref _requiresManualRecovery, 1);
        }
        catch (Exception)
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(failureKind: null);
            Volatile.Write(ref _requiresManualRecovery, 1);
        }
    }

    private LuaRef TakeOwnership()
    {
        var disableInfo = Interlocked.Exchange(ref _disableInfo, null);
        if (disableInfo is null)
            throw new ObjectDisposedException(nameof(AutoAssemblerPatch),
                "The Auto Assembler patch no longer owns CE's disable information.");

        return disableInfo;
    }
}

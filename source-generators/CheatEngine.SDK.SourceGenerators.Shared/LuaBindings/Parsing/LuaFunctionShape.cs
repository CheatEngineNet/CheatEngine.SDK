using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Decides whether a <c>[LuaFunction]</c> method can be wrapped by a generated thunk and classifies its signature.
///     Symbols in, flags and a value-only signature out: the CESDK2xxx analyzer links this file (with
///     <see cref="LuaValueKindMapper" />, <see cref="ContainingTypeShape" /> and <c>LuaEmit/LuaNames.cs</c>) so that a
///     method the generator skips is exactly a method the analyzer reports.
/// </summary>
/// <remarks>
///     The rules, in the order the flags are set: an ordinary, static, non-generic, non-<see langword="async" /> method
///     (an <c>async void</c> method returns <see langword="void" /> like any other and would otherwise pass every other
///     check, but the thunk calls it synchronously and cannot catch what its continuation throws); parameters by value,
///     none optional or <see langword="params" />, each of a built-in marshalled kind or carrying an explicit valid
///     <c>[LuaMarshaller]</c>, except a leading <c>LuaState</c> that receives the callback's state; a return type that
///     is <see langword="void" /> or has the same conversion contract. The name and the containing type are checked by
///     the caller (<c>LuaNames.IsValidName</c>, <see cref="ContainingTypeShape" />);
///     duplicate names are a group rule (<c>LuaFunctionTables</c>).
/// </remarks>
[SuppressMessage(
    "Meziantou.Analyzer",
    "MA0182",
    Justification =
        "This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaFunctionShape
{
    /// <summary>Inspects <paramref name="method" /> against resolved SDK Lua binding contracts.</summary>
    /// <param name="compilation">The consumer compilation that must be able to call a custom marshaller directly.</param>
    /// <param name="method">The attributed method.</param>
    /// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
    /// <param name="luaMarshallerAttribute">The real SDK marshaller annotation, or <see langword="null" />.</param>
    /// <param name="luaMarshallerContract">The real SDK static marshaller contract, or <see langword="null" />.</param>
    /// <param name="signature">
    ///     What could be classified; complete only when the result is
    ///     <see cref="LuaFunctionShapeIssues.None" />.
    /// </param>
    public static LuaFunctionShapeIssues Inspect(Compilation compilation, IMethodSymbol method, INamedTypeSymbol? luaState,
        INamedTypeSymbol? luaMarshallerAttribute, INamedTypeSymbol? luaMarshallerContract,
        out LuaFunctionSignature signature)
    {
        return InspectCore(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
            out signature);
    }

    /// <summary>
    ///     Compatibility overload for consumers that only validate the built-in scalar contract. The LuaBindings
    ///     generator and its analyzer call the overload that resolves <c>[LuaMarshaller]</c> explicitly.
    /// </summary>
    public static LuaFunctionShapeIssues Inspect(IMethodSymbol method, INamedTypeSymbol? luaState,
        out LuaFunctionSignature signature)
    {
        return InspectCore(null, method, luaState, null, null, out signature);
    }

    private static LuaFunctionShapeIssues InspectCore(Compilation? compilation, IMethodSymbol method,
        INamedTypeSymbol? luaState, INamedTypeSymbol? luaMarshallerAttribute,
        INamedTypeSymbol? luaMarshallerContract, out LuaFunctionSignature signature)
    {
        var issues = LuaFunctionShapeIssues.None;

        if (method.MethodKind != MethodKind.Ordinary) issues |= LuaFunctionShapeIssues.NotOrdinaryMethod;

        if (!method.IsStatic) issues |= LuaFunctionShapeIssues.NotStatic;

        if (method.IsGenericMethod) issues |= LuaFunctionShapeIssues.Generic;

        if (method.IsAsync) issues |= LuaFunctionShapeIssues.Async;

        issues |= InspectParameters(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
            out var passesState, out var arguments);
        issues |= InspectReturn(compilation, method, luaMarshallerAttribute, luaMarshallerContract, out var returnKind,
            out var returnMarshaller);

        signature = new LuaFunctionSignature(passesState, arguments, returnKind, returnMarshaller);
        return issues;
    }

    private static LuaFunctionShapeIssues InspectParameters(Compilation? compilation, IMethodSymbol method,
        INamedTypeSymbol? luaState,
        INamedTypeSymbol? luaMarshallerAttribute, INamedTypeSymbol? luaMarshallerContract,
        out bool passesState,
        out EquatableArray<LuaArgumentModel> arguments)
    {
        var issues = LuaFunctionShapeIssues.None;
        passesState = false;
        var builder = ImmutableArray.CreateBuilder<LuaArgumentModel>(method.Parameters.Length);
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None) issues |= LuaFunctionShapeIssues.ByRefParameter;

            if (parameter.IsParams) issues |= LuaFunctionShapeIssues.ParamsParameter;

            if (parameter.IsOptional || parameter.HasExplicitDefaultValue)
                issues |= LuaFunctionShapeIssues.OptionalParameter;

            if (LuaValueKindMapper.IsLuaState(parameter.Type, luaState))
            {
                if (i == 0)
                    passesState = true;
                else
                    issues |= LuaFunctionShapeIssues.StateParameterNotFirst;
            }
            else if (!LuaMarshallerResolver.TryResolve(compilation, method.ContainingType, parameter.Type,
                         parameter.GetAttributes(), luaMarshallerAttribute, luaMarshallerContract,
                         out var customMarshaller, out _))
            {
                issues |= LuaFunctionShapeIssues.UnsupportedParameterType;
            }
            else if (customMarshaller is not null)
            {
                builder.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), LuaValueKind.Int32,
                    IsNullable: false, CustomMarshaller: customMarshaller));
            }
            else if (LuaValueKindMapper.TryMap(parameter.Type, out var kind, out var isNullable))
            {
                builder.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), kind, isNullable));
            }
            else
            {
                issues |= LuaFunctionShapeIssues.UnsupportedParameterType;
            }
        }

        arguments = new EquatableArray<LuaArgumentModel>(builder.ToImmutable());
        return issues;
    }

    private static LuaFunctionShapeIssues InspectReturn(Compilation? compilation, IMethodSymbol method,
        INamedTypeSymbol? luaMarshallerAttribute,
        INamedTypeSymbol? luaMarshallerContract, out LuaValueKind? returnKind,
        out LuaCustomMarshallerModel? returnMarshaller)
    {
        returnKind = null;
        returnMarshaller = null;
        if (method.ReturnsVoid) return LuaFunctionShapeIssues.None;

        if (method is not { ReturnsByRef: false, ReturnsByRefReadonly: false })
            return LuaFunctionShapeIssues.UnsupportedReturnType;

        if (!LuaMarshallerResolver.TryResolve(compilation, method.ContainingType, method.ReturnType,
                method.GetReturnTypeAttributes(), luaMarshallerAttribute, luaMarshallerContract,
                out returnMarshaller, out _)
            || (returnMarshaller is not null && method.ReturnType.IsRefLikeType))
            return LuaFunctionShapeIssues.UnsupportedReturnType;

        if (returnMarshaller is not null) return LuaFunctionShapeIssues.None;

        if (!LuaValueKindMapper.TryMap(method.ReturnType, out var kind, out _))
            return LuaFunctionShapeIssues.UnsupportedReturnType;
        returnKind = kind;
        return LuaFunctionShapeIssues.None;
    }
}

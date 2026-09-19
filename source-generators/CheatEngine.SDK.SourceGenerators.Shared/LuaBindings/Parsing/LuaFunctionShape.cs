using System.Collections.Immutable;
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
///     none optional or <see langword="params" />, each of a marshalled kind, except a leading <c>LuaState</c> that
///     receives the callback's state; a return type that is <see langword="void" /> or of a marshalled kind. The name and
///     the containing type are checked by the caller (<c>LuaNames.IsValidName</c>, <see cref="ContainingTypeShape" />);
///     duplicate names are a group rule (<c>LuaFunctionTables</c>).
/// </remarks>
internal static class LuaFunctionShape
{
    /// <summary>Inspects <paramref name="method" />; never throws on malformed (error) symbols.</summary>
    /// <param name="method">The attributed method.</param>
    /// <param name="signature">
    ///     What could be classified; complete only when the result is
    ///     <see cref="LuaFunctionShapeIssues.None" />.
    /// </param>
    public static LuaFunctionShapeIssues Inspect(IMethodSymbol method, out LuaFunctionSignature signature)
    {
        var issues = LuaFunctionShapeIssues.None;

        if (method.MethodKind != MethodKind.Ordinary) issues |= LuaFunctionShapeIssues.NotOrdinaryMethod;

        if (!method.IsStatic) issues |= LuaFunctionShapeIssues.NotStatic;

        if (method.IsGenericMethod) issues |= LuaFunctionShapeIssues.Generic;

        if (method.IsAsync) issues |= LuaFunctionShapeIssues.Async;

        issues |= InspectParameters(method, out var passesState, out var arguments);
        issues |= InspectReturn(method, out var returnKind);

        signature = new LuaFunctionSignature(passesState, arguments, returnKind);
        return issues;
    }

    private static LuaFunctionShapeIssues InspectParameters(IMethodSymbol method, out bool passesState,
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

            if (LuaValueKindMapper.IsLuaState(parameter.Type))
            {
                if (i == 0)
                    passesState = true;
                else
                    issues |= LuaFunctionShapeIssues.StateParameterNotFirst;
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

    private static LuaFunctionShapeIssues InspectReturn(IMethodSymbol method, out LuaValueKind? returnKind)
    {
        returnKind = null;
        if (method.ReturnsVoid) return LuaFunctionShapeIssues.None;

        if (method is not { ReturnsByRef: false, ReturnsByRefReadonly: false }
            || !LuaValueKindMapper.TryMap(method.ReturnType, out var kind, out _))
            return LuaFunctionShapeIssues.UnsupportedReturnType;
        returnKind = kind;
        return LuaFunctionShapeIssues.None;
    }
}

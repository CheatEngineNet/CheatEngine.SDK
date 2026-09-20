using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Decides whether a <c>[LuaGlobal]</c> method can receive a generated body and classifies its signature into one
///     of the two call forms. Symbols in, flags and a value-only signature out: the CESDK2xxx analyzer links this file
///     (with <see cref="LuaValueKindMapper" />, <see cref="ContainingTypeShape" /> and <c>LuaEmit/LuaNames.cs</c>) so
///     that a method the generator skips is exactly a method the analyzer reports.
/// </summary>
/// <remarks>
///     <para>
///         The rules: an ordinary, static, non-generic, non-async method that is the defining declaration of a partial
///         method without an implementing part. Parameters, in order: an optional leading <c>LuaState</c> (the body then
///         uses it instead of acquiring a state), then the arguments (by value, of a marshalled kind, none optional or
///         <see langword="params" />), then the results: <see langword="out" /> parameters of a marshalled kind other than
///         <c>ReadOnlySpan&lt;byte&gt;</c>, or a copy-out pair <c>Span&lt;byte&gt; destination, out int written</c>.
///     </para>
///     <para>
///         The form follows from the results: any <see langword="out" /> result makes the method the Try form, which must
///         return <see langword="bool" />; no result makes it the throwing form, whose return type is
///         <see langword="void" />
///         or a marshalled kind other than <c>ReadOnlySpan&lt;byte&gt;</c> (a <see langword="bool" /> return without
///         results is therefore a throwing wrapper that reads a Lua boolean). A Try form without a result cannot be
///         written: a method with no <see langword="out" /> result is always the throwing form.
///     </para>
/// </remarks>
internal static class LuaGlobalShape
{
    /// <summary>Inspects <paramref name="method" /> against the resolved SDK <paramref name="luaState" /> symbol.</summary>
    /// <param name="method">The attributed method.</param>
    /// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
    /// <param name="signature">
    ///     What could be classified; complete only when the result is
    ///     <see cref="LuaGlobalShapeIssues.None" />.
    /// </param>
    public static LuaGlobalShapeIssues Inspect(IMethodSymbol method, INamedTypeSymbol? luaState,
        out LuaGlobalSignature signature)
    {
        var issues = InspectMethod(method);

        ParameterWalk walk = new();
        issues |= walk.Run(method.Parameters, luaState);

        EquatableArray<LuaResultModel> results = new(walk.Results.ToImmutable());
        var form = results.IsEmpty ? LuaCallForm.Throwing : LuaCallForm.Try;
        issues |= InspectReturn(method, form, out var returnKind, out var returnIsNullable);

        signature = new LuaGlobalSignature(
            walk.StateParameterName,
            new EquatableArray<LuaArgumentModel>(walk.Arguments.ToImmutable()),
            form,
            results,
            returnKind,
            returnIsNullable);
        return issues;
    }

    private static LuaGlobalShapeIssues InspectMethod(IMethodSymbol method)
    {
        var issues = LuaGlobalShapeIssues.None;
        if (method.MethodKind != MethodKind.Ordinary) issues |= LuaGlobalShapeIssues.NotOrdinaryMethod;

        if (!method.IsStatic) issues |= LuaGlobalShapeIssues.NotStatic;

        if (!method.IsPartialDefinition)
            issues |= LuaGlobalShapeIssues.NotPartialDefinition;
        else if (method.PartialImplementationPart is not null) issues |= LuaGlobalShapeIssues.AlreadyImplemented;

        if (method.IsGenericMethod) issues |= LuaGlobalShapeIssues.Generic;

        if (method.IsAsync) issues |= LuaGlobalShapeIssues.Async;

        return issues;
    }

    private static LuaGlobalShapeIssues InspectReturn(IMethodSymbol method, LuaCallForm form,
        out LuaValueKind? returnKind, out bool returnIsNullable)
    {
        returnKind = null;
        returnIsNullable = false;
        var byRef = method.ReturnsByRef || method.ReturnsByRefReadonly;

        if (form == LuaCallForm.Try)
            return method.ReturnType.SpecialType == SpecialType.System_Boolean && !byRef
                ? LuaGlobalShapeIssues.None
                : LuaGlobalShapeIssues.TryFormReturnNotBool;

        if (method.ReturnsVoid) return LuaGlobalShapeIssues.None;

        if (byRef) return LuaGlobalShapeIssues.UnsupportedReturnType;

        if (LuaValueKindMapper.IsReadOnlySpanOfByte(method.ReturnType)) return LuaGlobalShapeIssues.SpanResult;

        if (!LuaValueKindMapper.TryMap(method.ReturnType, out var kind, out returnIsNullable) ||
            !LuaValueKinds.CanBeResult(kind)) return LuaGlobalShapeIssues.UnsupportedReturnType;
        returnKind = kind;
        return LuaGlobalShapeIssues.None;
    }

    // The parameter list, left to right: the leading state, the arguments, then the results.
    private sealed class ParameterWalk
    {
        private bool _inResults;

        public string StateParameterName { get; private set; } = string.Empty;

        public ImmutableArray<LuaArgumentModel>.Builder Arguments { get; } =
            ImmutableArray.CreateBuilder<LuaArgumentModel>();

        public ImmutableArray<LuaResultModel>.Builder Results { get; } = ImmutableArray.CreateBuilder<LuaResultModel>();

        public LuaGlobalShapeIssues Run(ImmutableArray<IParameterSymbol> parameters, INamedTypeSymbol? luaState)
        {
            var issues = LuaGlobalShapeIssues.None;
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.IsParams) issues |= LuaGlobalShapeIssues.ParamsParameter;

                if (parameter.IsOptional || parameter.HasExplicitDefaultValue)
                    issues |= LuaGlobalShapeIssues.OptionalParameter;

                if (parameter.RefKind == RefKind.Out)
                    issues |= AddOutResult(parameter);
                else if (parameter.RefKind != RefKind.None)
                    issues |= LuaGlobalShapeIssues.ByRefParameter;
                else if (LuaValueKindMapper.IsSpanOfByte(parameter.Type))
                    issues |= AddCopyOutResult(parameters, ref i);
                else
                    issues |= AddArgument(parameter, i, luaState);
            }

            return issues;
        }

        private LuaGlobalShapeIssues AddOutResult(IParameterSymbol parameter)
        {
            _inResults = true;
            if (LuaValueKindMapper.IsReadOnlySpanOfByte(parameter.Type)) return LuaGlobalShapeIssues.SpanResult;

            if (!LuaValueKindMapper.TryMap(parameter.Type, out var kind, out var isNullable) ||
                !LuaValueKinds.CanBeResult(kind)) return LuaGlobalShapeIssues.UnsupportedResultType;
            Results.Add(LuaResultModel.Value(kind, Identifiers.Escape(parameter.Name), isNullable));
            return LuaGlobalShapeIssues.None;
        }

        // A copy-out result is the destination and the count together: 'Span<byte> destination, out int written'.
        private LuaGlobalShapeIssues AddCopyOutResult(ImmutableArray<IParameterSymbol> parameters, ref int index)
        {
            _inResults = true;
            if (index + 1 >= parameters.Length
                || parameters[index + 1] is
                    not { RefKind: RefKind.Out, Type.SpecialType: SpecialType.System_Int32 } written)
                return LuaGlobalShapeIssues.UnsupportedResultType;
            var destinationIsScoped = parameters[index].ScopedKind != ScopedKind.None;
            Results.Add(LuaResultModel.CopyOut(Identifiers.Escape(parameters[index].Name),
                Identifiers.Escape(written.Name), destinationIsScoped));
            index++;
            return LuaGlobalShapeIssues.None;
        }

        // A by-value parameter: an argument, or the leading state.
        private LuaGlobalShapeIssues AddArgument(IParameterSymbol parameter, int index, INamedTypeSymbol? luaState)
        {
            var issues = _inResults ? LuaGlobalShapeIssues.ResultBeforeArgument : LuaGlobalShapeIssues.None;
            if (LuaValueKindMapper.IsLuaState(parameter.Type, luaState))
            {
                if (index != 0) return issues | LuaGlobalShapeIssues.StateParameterNotFirst;
                StateParameterName = Identifiers.Escape(parameter.Name);
                return issues;
            }

            if (!LuaValueKindMapper.TryMap(parameter.Type, out var kind, out var isNullable))
                return issues | LuaGlobalShapeIssues.UnsupportedParameterType;
            var isScoped = parameter.ScopedKind != ScopedKind.None;
            Arguments.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), kind, isNullable, isScoped));
            return issues;
        }
    }
}

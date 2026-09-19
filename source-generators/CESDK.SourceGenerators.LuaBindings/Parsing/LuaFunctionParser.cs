using System.Threading;
using CESDK.SourceGenerators.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CESDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     The <c>ForAttributeWithMetadataName</c> transform for <c>[LuaFunction]</c>: the only place of that pipeline that
///     touches symbols. It reduces the attributed method to a <see cref="LuaFunctionModel" /> and lets go of everything
///     else.
/// </summary>
internal static class LuaFunctionParser
{
    /// <summary>Builds the model of one attributed method.</summary>
    public static LuaFunctionModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = (IMethodSymbol)context.TargetSymbol;
        var luaName = AttributeArguments.ReadName(context.Attributes);

        var issues = LuaFunctionShape.Inspect(method, out var signature);
        if (!LuaNames.IsValidName(luaName)) issues |= LuaFunctionShapeIssues.InvalidName;

        var typeIssues = ContainingTypeShape.Inspect(method.ContainingType, cancellationToken);
        var containingType = ContainingTypeParser.Parse(method.ContainingType);

        LuaThunkModel? thunk = null;
        if (issues == LuaFunctionShapeIssues.None && typeIssues == ContainingTypeIssues.None)
            thunk = new LuaThunkModel(
                luaName!,
                LuaThunkModel.ThunkNameFor(luaName!),
                containingType.FullyQualifiedName + "." + Identifiers.Escape(method.Name),
                signature.PassesState,
                signature.Arguments,
                signature.ReturnKind,
                LuaBindingsDeclaredDiagnosticIds.Collect(method));

        return new LuaFunctionModel(containingType, typeIssues, luaName ?? string.Empty, issues, thunk);
    }
}

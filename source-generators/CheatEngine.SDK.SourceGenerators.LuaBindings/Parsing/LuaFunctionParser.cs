using System.Threading;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

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
        var compilation = context.SemanticModel.Compilation;
        var isSdkAttribute = LuaBindingSymbols.ContainsSdkAttribute(context.Attributes, compilation,
            LuaBindingsGenerator.LuaFunctionAttributeMetadataName);
        var luaName = LuaBindingSymbols.ReadSdkAttributeName(context.Attributes, compilation,
            LuaBindingsGenerator.LuaFunctionAttributeMetadataName);

        var issues = LuaFunctionShape.Inspect(compilation, method, LuaBindingSymbols.ResolveLuaState(compilation),
            LuaBindingSymbols.ResolveLuaMarshallerAttribute(compilation),
            LuaBindingSymbols.ResolveLuaMarshallerContract(compilation), out var signature);
        if (!isSdkAttribute || !LuaNames.IsValidName(luaName)) issues |= LuaFunctionShapeIssues.InvalidName;

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
                LuaBindingsDeclaredDiagnosticIds.Collect(method),
                signature.ReturnMarshaller);

        return new LuaFunctionModel(containingType, typeIssues, luaName ?? string.Empty, issues, thunk,
            HasGeneratedIdentityCollision(method, luaName));
    }

    // A source generator must not rely on a later CS0111/CS0102 failure to protect user code. The thunk's name is
    // per entry; the registration pair is shared by every valid function on this containing type. When either exists
    // already, this entry is intentionally dropped and CESDK2007 identifies the colliding source member.
    private static bool HasGeneratedIdentityCollision(IMethodSymbol method, string? luaName)
    {
        var type = method.ContainingType;
        if (type.GetMembers(LuaRegistrationEmitter.RegisterMethodName).Length != 0
            || type.GetMembers(LuaRegistrationEmitter.UnregisterMethodName).Length != 0)
            return true;

        return LuaNames.IsValidName(luaName)
               && type.GetMembers(LuaThunkModel.ThunkNameFor(luaName!)).Length != 0;
    }
}

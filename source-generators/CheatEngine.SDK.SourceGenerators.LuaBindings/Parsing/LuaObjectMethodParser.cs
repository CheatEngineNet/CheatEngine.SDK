using System;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>Parses the supported partial instance-method form of <c>[LuaMethod]</c>.</summary>
internal static class LuaObjectMethodParser
{
    private const string StateLocal = "__ceState";
    private const string OperationLocal = "__ceOperation";
    private const string TopLocal = "__ceTop";
    private const string StatusLocal = "__ceStatus";
    private const string ResultLocal = "__ceResult";

    /// <summary>Builds one value-only method model; an invalid input receives no generated implementation.</summary>
    public static LuaObjectMethodModel Parse(GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = (IMethodSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var isSdkAttribute = LuaBindingSymbols.ContainsSdkAttribute(context.Attributes, compilation,
            LuaBindingsGenerator.LuaMethodAttributeMetadataName);
        var luaName = LuaBindingSymbols.ReadSdkAttributeName(context.Attributes, compilation,
            LuaBindingsGenerator.LuaMethodAttributeMetadataName);
        var described = TryDescribe(method, context.TargetNode as MethodDeclarationSyntax, out var model);
        var valid = isSdkAttribute && LuaNames.IsValidName(luaName)
                                   && LuaClassParser.IsGeneratedHandle(method.ContainingType, compilation,
                                       cancellationToken)
                                   && described;

        if (valid)
            return model with { LuaName = luaName!, IsValid = true };

        return new LuaObjectMethodModel(
            ContainingTypeParser.Parse(method.ContainingType),
            luaName ?? string.Empty,
            string.Empty,
            Identifiers.Escape(method.Name),
            EquatableArray<LuaArgumentModel>.Empty,
            LuaCallForm.Throwing,
            EquatableArray<LuaResultModel>.Empty,
            ReturnKind: null,
            ReturnIsNullable: false,
            SortKey(method),
            IsValid: false);
    }

    private static bool TryDescribe(IMethodSymbol method, MethodDeclarationSyntax? declaration,
        out LuaObjectMethodModel model)
    {
        var arguments = ImmutableArray.CreateBuilder<LuaArgumentModel>(method.Parameters.Length);
        var results = ImmutableArray.CreateBuilder<LuaResultModel>();
        var valid = method.MethodKind == MethodKind.Ordinary
                    && !method.IsStatic
                    && method.IsPartialDefinition
                    && method.PartialImplementationPart is null
                    && !method.IsGenericMethod
                    && !method.IsAsync;
        valid &= DescribeParameters(method.Parameters, arguments, results);

        var form = results.Count == 0 ? LuaCallForm.Throwing : LuaCallForm.Try;
        LuaValueKind? returnKind = null;
        var returnNullable = false;
        valid &= TryDescribeReturn(method, form, out returnKind, out returnNullable);

        model = new LuaObjectMethodModel(
            ContainingTypeParser.Parse(method.ContainingType),
            string.Empty,
            Modifiers(declaration),
            Identifiers.Escape(method.Name),
            new EquatableArray<LuaArgumentModel>(arguments.ToImmutable()),
            form,
            new EquatableArray<LuaResultModel>(results.ToImmutable()),
            returnKind,
            returnNullable,
            SortKey(method),
            valid);
        return valid;
    }

    private static bool DescribeParameters(ImmutableArray<IParameterSymbol> parameters,
        ImmutableArray<LuaArgumentModel>.Builder arguments, ImmutableArray<LuaResultModel>.Builder results)
    {
        var valid = true;
        var seenResult = false;
        foreach (var parameter in parameters)
        {
            if (IsReserved(parameter.Name) || parameter.IsParams || parameter.IsOptional
                || parameter.HasExplicitDefaultValue) valid = false;

            if (parameter.RefKind == RefKind.Out)
            {
                seenResult = true;
                if (!LuaValueKindMapper.TryMap(parameter.Type, out var kind, out var nullable)
                    || !LuaValueKinds.CanBeResult(kind))
                    valid = false;
                else
                    results.Add(LuaResultModel.Value(kind, Identifiers.Escape(parameter.Name), nullable));

                continue;
            }

            if (parameter.RefKind != RefKind.None || seenResult
                                                  || !LuaValueKindMapper.TryMap(parameter.Type, out var argumentKind,
                                                      out var argumentNullable))
                valid = false;
            else
                arguments.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), argumentKind, argumentNullable,
                    parameter.ScopedKind != ScopedKind.None));
        }

        return valid;
    }

    private static bool TryDescribeReturn(IMethodSymbol method, LuaCallForm form, out LuaValueKind? returnKind,
        out bool returnNullable)
    {
        returnKind = null;
        returnNullable = false;
        if (form == LuaCallForm.Try)
            return method.ReturnType.SpecialType == SpecialType.System_Boolean && !method.ReturnsByRef
                                                                               && !method.ReturnsByRefReadonly;

        if (method.ReturnsVoid) return true;

        if (method.ReturnsByRef || method.ReturnsByRefReadonly
                                || !LuaValueKindMapper.TryMap(method.ReturnType, out var kind, out returnNullable)
                                || !LuaValueKinds.CanBeResult(kind))
            return false;

        returnKind = kind;
        return true;
    }

    private static bool IsReserved(string name)
    {
        return string.Equals(name, StateLocal, StringComparison.Ordinal)
               || string.Equals(name, OperationLocal, StringComparison.Ordinal)
               || string.Equals(name, TopLocal, StringComparison.Ordinal)
               || string.Equals(name, StatusLocal, StringComparison.Ordinal)
               || string.Equals(name, ResultLocal, StringComparison.Ordinal);
    }

    private static string Modifiers(MethodDeclarationSyntax? declaration)
    {
        StringBuilder modifiers = new();
        var isNew = false;
        var isUnsafe = false;
        if (declaration is not null)
            foreach (var token in declaration.Modifiers)
                switch (token.Kind())
                {
                    case SyntaxKind.PublicKeyword:
                    case SyntaxKind.InternalKeyword:
                    case SyntaxKind.ProtectedKeyword:
                    case SyntaxKind.PrivateKeyword:
                        Append(modifiers, token.ValueText);
                        break;
                    case SyntaxKind.NewKeyword:
                        isNew = true;
                        break;
                    case SyntaxKind.ReadOnlyKeyword:
                        Append(modifiers, token.ValueText);
                        break;
                    case SyntaxKind.UnsafeKeyword:
                        isUnsafe = true;
                        break;
                }

        if (isNew) Append(modifiers, "new");

        if (isUnsafe) Append(modifiers, "unsafe");

        Append(modifiers, "partial");
        return modifiers.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0) builder.Append(' ');

        builder.Append(value);
    }

    private static string SortKey(IMethodSymbol method)
    {
        StringBuilder key = new(method.Name);
        key.Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) key.Append(", ");

            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None) key.Append(parameter.RefKind).Append(' ');

            key.Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return key.Append(')').ToString();
    }
}

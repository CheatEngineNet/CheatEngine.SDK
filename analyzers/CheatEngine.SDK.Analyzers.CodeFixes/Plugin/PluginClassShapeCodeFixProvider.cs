using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.Analyzers.CodeFixes.Plugin;

/// <summary>
///     The mechanical fixes of CESDK0001, one action per problem kind, chosen from the problem name the analyzer puts
///     in the diagnostic properties:
///     <list type="bullet">
///         <item>
///             <c>abstract</c> or <c>static</c> class: replace the modifier with <c>sealed</c>, in every part that
///             carries it;
///         </item>
///         <item>
///             no parameterless constructor: add <c>public Name() { }</c> (not offered when the new constructor would
///             have to pass arguments only the author knows: the class has a primary constructor to chain to, or its base
///             class
///             has no accessible constructor that can be called without arguments);
///         </item>
///         <item>inaccessible parameterless constructor: make it <c>public</c>.</item>
///     </list>
///     The other problems (generic, nesting, base class, accessibility of the class, file-local, required members,
///     obsolete errors, display name) are design decisions and get no fix.
/// </summary>
/// <remarks>
///     The edits may land in another document than the diagnostic (partial classes), so the actions change the
///     solution. Fix All uses the batch fixer: the actions of one equivalence key never touch the same declaration
///     twice.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PluginClassShapeCodeFixProvider))]
[Shared]
public sealed class PluginClassShapeCodeFixProvider : CodeFixProvider
{
    internal const string MakeSealedEquivalenceKey = DiagnosticIds.InvalidPluginClass + ".MakeSealed";

    internal const string AddConstructorEquivalenceKey =
        DiagnosticIds.InvalidPluginClass + ".AddParameterlessConstructor";

    internal const string MakeConstructorPublicEquivalenceKey =
        DiagnosticIds.InvalidPluginClass + ".MakeConstructorPublic";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [DiagnosticIds.InvalidPluginClass];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var cancellationToken = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(DiagnosticProperties.PluginClassProblem, out var problemName)
                || !Enum.TryParse(problemName, out PluginShapeIssues problem)
                || root.FindToken(diagnostic.Location.SourceSpan.Start).Parent
                    ?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration
                || semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not { } type)
                continue;

            var solution = context.Document.Project.Solution;
            var action = problem switch
            {
                PluginShapeIssues.Abstract => CreateMakeSealed(solution, type, SyntaxKind.AbstractKeyword, "abstract",
                    cancellationToken),
                PluginShapeIssues.Static => CreateMakeSealed(solution, type, SyntaxKind.StaticKeyword, "static",
                    cancellationToken),
                PluginShapeIssues.MissingParameterlessConstructor => CreateAddConstructor(context.Document, root,
                    declaration, type, semanticModel.Compilation, cancellationToken),
                PluginShapeIssues.InaccessibleParameterlessConstructor => CreateMakeConstructorPublic(solution, type,
                    cancellationToken),
                _ => null
            };

            if (action is not null) context.RegisterCodeFix(action, diagnostic);
        }
    }

    private static CodeAction? CreateMakeSealed(Solution solution, INamedTypeSymbol type, SyntaxKind modifierKind,
        string modifierText, CancellationToken cancellationToken)
    {
        // Every part of a partial class may repeat the modifier; parts in generated code have no document.
        Dictionary<DocumentId, List<SyntaxToken>> modifiersByDocument = [];
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax part
                || solution.GetDocument(reference.SyntaxTree) is not { } document)
                continue;

            foreach (var modifier in part.Modifiers)
            {
                if (!modifier.IsKind(modifierKind)) continue;

                if (!modifiersByDocument.TryGetValue(document.Id, out var modifiers))
                {
                    modifiers = [];
                    modifiersByDocument.Add(document.Id, modifiers);
                }

                modifiers.Add(modifier);
            }
        }

        if (modifiersByDocument.Count == 0) return null;

        return CodeAction.Create(
            $"Replace '{modifierText}' with 'sealed'",
            async actionCancellationToken =>
            {
                var changed = solution;
                foreach (var entry in modifiersByDocument)
                {
                    var document = changed.GetDocument(entry.Key);
                    var root = document is null
                        ? null
                        : await document.GetSyntaxRootAsync(actionCancellationToken).ConfigureAwait(false);
                    if (root is not null)
                        changed = changed.WithDocumentSyntaxRoot(entry.Key,
                            root.ReplaceTokens(entry.Value,
                                static (original, _) => PluginClassRewriter.ToSealed(original)));
                }

                return changed;
            },
            MakeSealedEquivalenceKey);
    }

    private static CodeAction? CreateAddConstructor(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        // 'class Plugin;' has no member list to add to.
        if (declaration.OpenBraceToken.IsKind(SyntaxKind.None) || declaration.OpenBraceToken.IsMissing) return null;

        // A primary constructor forces every other constructor to chain to it: not mechanical.
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax { ParameterList: not null })
                return null;

        // 'public Name() { }' chains to 'base()' implicitly: same reason when the base class has nothing to bind it to.
        if (!HasImplicitlyCallableBaseConstructor(type, compilation)) return null;

        return CodeAction.Create(
            "Add public parameterless constructor",
            _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(declaration,
                PluginClassRewriter.AddParameterlessConstructor(declaration)))),
            AddConstructorEquivalenceKey);
    }

    // Overload resolution of the implicit 'base()': a constructor whose parameters can all be omitted (none,
    // optional, params), accessible from the derived class. An unresolved base class has no constructors: no fix.
    private static bool HasImplicitlyCallableBaseConstructor(INamedTypeSymbol type, Compilation compilation)
    {
        if (type.BaseType is not { } baseType) return false;

        foreach (var constructor in baseType.InstanceConstructors)
            if (CanOmitEveryArgument(constructor) && compilation.IsSymbolAccessibleWithin(constructor, type))
                return true;

        return false;
    }

    private static bool CanOmitEveryArgument(IMethodSymbol constructor)
    {
        foreach (var parameter in constructor.Parameters)
            if (!parameter.IsOptional && !parameter.IsParams)
                return false;

        return true;
    }

    private static CodeAction? CreateMakeConstructorPublic(Solution solution, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!constructor.Parameters.IsEmpty
                || constructor.IsImplicitlyDeclared
                || constructor.DeclaringSyntaxReferences.IsEmpty
                || constructor.DeclaringSyntaxReferences[0] is not { } reference
                || reference.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax syntax
                || solution.GetDocument(reference.SyntaxTree) is not { } document)
                continue;

            return CodeAction.Create(
                "Make parameterless constructor public",
                async actionCancellationToken =>
                {
                    var root = await document.GetSyntaxRootAsync(actionCancellationToken).ConfigureAwait(false);
                    return root is null
                        ? solution
                        : solution.WithDocumentSyntaxRoot(document.Id,
                            root.ReplaceNode(syntax, PluginClassRewriter.MakePublic(syntax)));
                },
                MakeConstructorPublicEquivalenceKey);
        }

        return null;
    }
}

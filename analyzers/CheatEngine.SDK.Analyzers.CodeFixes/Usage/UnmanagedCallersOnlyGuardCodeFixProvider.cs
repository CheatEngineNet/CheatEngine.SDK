using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using CheatEngine.SDK.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.Analyzers.CodeFixes.Usage;

/// <summary>
///     The fix of CESDK1004: moves the whole body of the <c>[UnmanagedCallersOnly]</c> method or local function into a
///     <c>try</c> and adds <c>catch (Exception)</c> returning the failure value of the return type (<c>0</c> for
///     numeric types, <c>false</c>, <c>default</c> for pointers, handles and structs, nothing for <c>void</c>). An
///     expression body becomes a block body, with the comments around the expression; it gets no fix when preprocessor
///     directives sit inside it (see <see cref="ExceptionGuardRewriter.CanGuard" />).
/// </summary>
/// <remarks>
///     The inserted catch block is the place to log; the fix does not invent a logging call. Fix All uses the batch
///     fixer: each fix rewrites one method, so the edits of one document never overlap, except for an unguarded local
///     function nested in an unguarded method, which takes a second pass.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnmanagedCallersOnlyGuardCodeFixProvider))]
[Shared]
public sealed class UnmanagedCallersOnlyGuardCodeFixProvider : CodeFixProvider
{
    internal const string WrapEquivalenceKey = DiagnosticIds.UnguardedUnmanagedCallersOnly + ".WrapInTryCatch";

    private const string WrapTitle = "Wrap body in try/catch returning a failure value";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [DiagnosticIds.UnguardedUnmanagedCallersOnly];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var declaration = FindDeclaration(root, diagnostic.Location.SourceSpan);
            if (declaration is null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    WrapTitle,
                    cancellationToken => WrapAsync(context.Document, declaration, cancellationToken),
                    WrapEquivalenceKey),
                diagnostic);
        }
    }

    // The diagnostic sits on the identifier of a method or local function that has a body.
    private static SyntaxNode? FindDeclaration(SyntaxNode root, TextSpan span)
    {
        for (var node = root.FindToken(span.Start).Parent; node is not null; node = node.Parent)
            switch (node)
            {
                case MethodDeclarationSyntax { Body: not null }:
                case LocalFunctionStatementSyntax { Body: not null }:
                    return node;
                case MethodDeclarationSyntax { ExpressionBody: { } expressionBody } method:
                    return ExceptionGuardRewriter.CanGuard(expressionBody, method.SemicolonToken) ? node : null;
                case LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } localFunction:
                    return ExceptionGuardRewriter.CanGuard(expressionBody, localFunction.SemicolonToken) ? node : null;
                case MemberDeclarationSyntax:
                    return null;
            }

        return null;
    }

    private static async Task<Document> WrapAsync(Document document, SyntaxNode declaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null ||
            semanticModel?.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol method)
            return document;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var endOfLine = ExceptionGuardRewriter.DetectEndOfLine(text);

        var guarded = declaration switch
        {
            MethodDeclarationSyntax { Body: { } body } syntax => syntax.WithBody(
                ExceptionGuardRewriter.Guard(body, method.ReturnType, endOfLine)),
            MethodDeclarationSyntax { ExpressionBody: { } expressionBody } syntax => syntax
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithBody(ExceptionGuardRewriter.Guard(expressionBody, syntax.SemicolonToken, method.ReturnType,
                    endOfLine)),
            LocalFunctionStatementSyntax { Body: { } body } syntax => syntax.WithBody(
                ExceptionGuardRewriter.Guard(body, method.ReturnType, endOfLine)),
            LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } syntax => syntax
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithBody(ExceptionGuardRewriter.Guard(expressionBody, syntax.SemicolonToken, method.ReturnType,
                    endOfLine)),
            _ => declaration
        };

        return document.WithSyntaxRoot(root.ReplaceNode(declaration, guarded));
    }
}

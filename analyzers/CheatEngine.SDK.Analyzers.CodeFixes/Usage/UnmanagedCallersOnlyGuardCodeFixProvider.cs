using System;
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
///     The fix of CESDK1004: moves the whole body of the exact CE bootstrap method into a
///     <see langword="try" /> and adds <c>catch (Exception)</c> returning the documented CE bootstrap failure value
///     (<c>0</c>).
///     The action is deliberately available only for the exact <c>CESDK.CESDK.CEPluginInitialize(IntPtr, int)</c>
///     convention. CESDK1004 can identify other unsafe callbacks, but their failure convention belongs to their native
///     contract and must not be guessed. An expression body becomes a block body, with the comments around the
///     expression; it gets no fix when preprocessor directives sit inside it (see
///     <see cref="ExceptionGuardRewriter.CanGuard" />).
/// </summary>
/// <remarks>
///     The inserted catch block is the place to log; the fix does not invent a logging call. Fix All uses the batch
///     fixer: each offered action rewrites the one CE bootstrap declaration that owns the documented failure value.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnmanagedCallersOnlyGuardCodeFixProvider))]
[Shared]
public sealed class UnmanagedCallersOnlyGuardCodeFixProvider : CodeFixProvider
{
	internal const string WrapEquivalenceKey = DiagnosticIds.UnguardedUnmanagedCallersOnly + ".WrapInTryCatch";

	private const string WrapTitle = "Wrap body in try/catch returning a failure value";

	/// <inheritdoc />
	public override ImmutableArray<string> FixableDiagnosticIds
	{
		get;
	} =
		[DiagnosticIds.UnguardedUnmanagedCallersOnly];

	/// <inheritdoc />
	public override FixAllProvider GetFixAllProvider()
	{
		return WellKnownFixAllProviders.BatchFixer;
	}

	/// <inheritdoc />
	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel =
			await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null || semanticModel is null)
		{
			return;
		}

		foreach (Diagnostic diagnostic in context.Diagnostics)
		{
			MethodDeclarationSyntax? declaration = FindDeclaration(root, diagnostic.Location.SourceSpan);
			if (declaration is null
				|| semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method
				|| !HasKnownFailureConvention(method))
			{
				continue;
			}

			context.RegisterCodeFix(
				CodeAction.Create(
					WrapTitle,
					cancellationToken => WrapAsync(context.Document, declaration, cancellationToken),
					WrapEquivalenceKey),
				diagnostic);
		}
	}

	// The known CE bootstrap convention is a type member, so a local function never has a safe failure convention to fix.
	private static MethodDeclarationSyntax? FindDeclaration(SyntaxNode root, TextSpan span)
	{
		for (SyntaxNode? node = root.FindToken(span.Start).Parent; node is not null; node = node.Parent)
		{
			switch (node)
			{
				case MethodDeclarationSyntax { Body: not null } method:
					return method;
				case MethodDeclarationSyntax { ExpressionBody: { } expressionBody } method:
					return ExceptionGuardRewriter.CanGuard(expressionBody, method.SemicolonToken) ? method : null;
				case LocalFunctionStatementSyntax:
				case AnonymousFunctionExpressionSyntax:
					return null;
				case MemberDeclarationSyntax:
					return null;
			}
		}

		return null;
	}

	private static async Task<Document> WrapAsync(Document document, MethodDeclarationSyntax declaration,
		CancellationToken cancellationToken)
	{
		SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
		if (root is null ||
			semanticModel?.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol method
			|| !HasKnownFailureConvention(method))
		{
			return document;
		}

		SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
		SyntaxTrivia endOfLine = ExceptionGuardRewriter.DetectEndOfLine(text);

		MethodDeclarationSyntax guarded = declaration switch
		{
			{ Body: { } body } syntax => syntax.WithBody(
				ExceptionGuardRewriter.Guard(body, method.ReturnType)),
			{ ExpressionBody: { } expressionBody } syntax => syntax
				.WithExpressionBody(null)
				.WithSemicolonToken(default)
				.WithBody(ExceptionGuardRewriter.Guard(expressionBody, syntax.SemicolonToken, method.ReturnType,
					endOfLine)),
			_ => declaration
		};

		return document.WithSyntaxRoot(root.ReplaceNode(declaration, guarded));
	}

	// CE's managed bootstrap signature is the one callback failure convention owned by this SDK today. A bare
	// [UnmanagedCallersOnly] method has no such contract: returning zero may mean success, a count, or an address.
	private static bool HasKnownFailureConvention(IMethodSymbol method)
	{
		if (!method.IsStatic
			|| method.IsGenericMethod
			|| method.DeclaredAccessibility != Accessibility.Public
			|| !string.Equals(method.Name, "CEPluginInitialize", StringComparison.Ordinal)
			|| method.ReturnsByRef
			|| method.ReturnsByRefReadonly
			|| method.ReturnType.SpecialType != SpecialType.System_Int32
			|| method.Parameters.Length != 2
			|| method.Parameters[0].RefKind != RefKind.None
			|| method.Parameters[0].Type.SpecialType != SpecialType.System_IntPtr
			|| method.Parameters[1].RefKind != RefKind.None
			|| method.Parameters[1].Type.SpecialType != SpecialType.System_Int32)
		{
			return false;
		}

		INamedTypeSymbol containingType = method.ContainingType;
		return string.Equals(containingType.Name, "CESDK", StringComparison.Ordinal)
			   && containingType.ContainingType is null
			   && string.Equals(containingType.ContainingNamespace.ToDisplayString(), "CESDK",
				   StringComparison.Ordinal);
	}
}

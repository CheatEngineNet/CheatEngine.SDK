using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CheatEngine.SDK.Analyzers.CodeFixes.Plugin;

/// <summary>The syntax edits behind the CESDK0001 fixes. Pure syntax in, syntax out.</summary>
internal static class PluginClassRewriter
{
	/// <summary>The same token position and trivia, with the keyword <see langword="sealed" />.</summary>
	public static SyntaxToken ToSealed(SyntaxToken modifier)
	{
		return SyntaxFactory.Token(modifier.LeadingTrivia, SyntaxKind.SealedKeyword, modifier.TrailingTrivia);
	}

	/// <summary>
	///     Adds <c>public Name() { }</c>: in front of the first declared constructor, else after the last field, else
	///     as the first member.
	/// </summary>
	public static TypeDeclarationSyntax AddParameterlessConstructor(TypeDeclarationSyntax declaration)
	{
		ConstructorDeclarationSyntax constructor = SyntaxFactory
			.ConstructorDeclaration(declaration.Identifier.WithoutTrivia())
			.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
			.WithBody(SyntaxFactory.Block())
			.WithAdditionalAnnotations(Formatter.Annotation);

		return declaration.WithMembers(declaration.Members.Insert(FindConstructorIndex(declaration.Members),
			constructor));
	}

	/// <summary>
	///     Replaces whatever accessibility the constructor declares (none means <see langword="private" />) with
	///     <see langword="public" />, keeping the trivia of the declaration.
	/// </summary>
	public static ConstructorDeclarationSyntax MakePublic(ConstructorDeclarationSyntax constructor)
	{
		SyntaxTokenList modifiers = constructor.Modifiers;
		int first = IndexOfAccessibility(modifiers, 0);
		if (first < 0)
		{
			// No accessibility keyword: 'public' becomes the first token after the attributes and takes over the
			// leading trivia (indentation, comments) of the token that was there.
			SyntaxToken anchor = modifiers.Count > 0 ? modifiers[0] : constructor.Identifier;
			SyntaxToken publicKeyword = SyntaxFactory.Token(anchor.LeadingTrivia, SyntaxKind.PublicKeyword,
				SyntaxFactory.TriviaList(SyntaxFactory.Space));
			ConstructorDeclarationSyntax stripped =
				constructor.ReplaceToken(anchor, anchor.WithLeadingTrivia(SyntaxFactory.TriviaList()));
			return stripped.WithModifiers(stripped.Modifiers.Insert(0, publicKeyword));
		}

		SyntaxToken original = modifiers[first];
		modifiers = modifiers.Replace(original,
			SyntaxFactory.Token(original.LeadingTrivia, SyntaxKind.PublicKeyword, original.TrailingTrivia));

		// 'private protected' and 'protected internal' are two keywords: drop the second one, but not what the
		// author wrote around it. Its comments move behind the modifier in front of it, which always exists.
		for (int next = IndexOfAccessibility(modifiers, first + 1);
			 next >= 0;
			 next = IndexOfAccessibility(modifiers, next))
		{
			SyntaxToken dropped = modifiers[next];
			SyntaxToken previous = modifiers[next - 1];
			SyntaxTriviaList carried = FromFirstComment(dropped.LeadingTrivia.AddRange(dropped.TrailingTrivia));
			modifiers = modifiers
				.Replace(previous, previous.WithTrailingTrivia(previous.TrailingTrivia.AddRange(carried)))
				.RemoveAt(next);
		}

		return constructor.WithModifiers(modifiers);
	}

	// The trivia from the first one that is not white space to the end: empty for a keyword that carries no comment,
	// so that an ordinary 'private protected' loses the keyword and its spacing together.
	private static SyntaxTriviaList FromFirstComment(SyntaxTriviaList trivia)
	{
		SyntaxTriviaList kept = SyntaxFactory.TriviaList();
		foreach (SyntaxTrivia item in trivia)
		{
			if (kept.Count > 0 ||
				!(item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia)))
			{
				kept = kept.Add(item);
			}
		}

		return kept;
	}

	private static int FindConstructorIndex(SyntaxList<MemberDeclarationSyntax> members)
	{
		int afterLastField = 0;
		for (int index = 0; index < members.Count; index++)
		{
			switch (members[index])
			{
				case ConstructorDeclarationSyntax:
					return index;
				case FieldDeclarationSyntax:
					afterLastField = index + 1;
					break;
			}
		}

		return afterLastField;
	}

	private static int IndexOfAccessibility(SyntaxTokenList modifiers, int start)
	{
		for (int index = start; index < modifiers.Count; index++)
		{
			if (modifiers[index].Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword
				or SyntaxKind.ProtectedKeyword or SyntaxKind.PrivateKeyword)
			{
				return index;
			}
		}

		return -1;
	}
}

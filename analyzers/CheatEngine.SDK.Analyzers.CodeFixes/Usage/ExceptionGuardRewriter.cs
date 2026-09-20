using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.Analyzers.CodeFixes.Usage;

/// <summary>
///     Builds the guarded body that CESDK1004's known bootstrap convention asks for: the whole former body inside one
///     <c>try</c>, and a <c>catch (System.Exception)</c> that returns <c>0</c>.
/// </summary>
/// <remarks>
///     Pure syntax in, syntax out. The result carries <see cref="Formatter.Annotation" /> (indentation is left to the
///     formatter) and the exception type carries <see cref="Simplifier.Annotation" /> (it becomes <c>Exception</c> where
///     <c>using System;</c> is in scope, <c>System.Exception</c> elsewhere).
/// </remarks>
internal static class ExceptionGuardRewriter
{
    /// <summary>Wraps the statements of a block body.</summary>
    public static BlockSyntax Guard(BlockSyntax body, ITypeSymbol returnType)
    {
        // Comments in front of the closing brace belong to the old body: they move into the try block with it.
        var tryBlock = SyntaxFactory.Block(body.Statements)
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(body.CloseBraceToken.LeadingTrivia));

        return body
            .WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(CreateTry(tryBlock, returnType)))
            .WithCloseBraceToken(body.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.ElasticMarker))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>Turns an expression body into a guarded block body.</summary>
    /// <param name="expressionBody">The arrow clause; must not contain preprocessor directives.</param>
    /// <param name="semicolon">The semicolon that ends the declaration; it becomes the end of the statement.</param>
    /// <param name="returnType">Decides between a return and an expression statement, and the failure value.</param>
    /// <param name="endOfLine">The line break of the document, see <see cref="DetectEndOfLine" />.</param>
    /// <remarks>
    ///     Nothing the author wrote is dropped. Comments between the signature and the expression (<c>=> // why</c>, or
    ///     on lines of their own) move in front of the statement, one per line; the semicolon keeps its trivia up to the
    ///     last comment (<c>; // why</c> stays on the statement line) and whatever follows that comment, normally the
    ///     line break, ends the new block. A throw expression is only legal in an expression body: it becomes a throw
    ///     statement (<c>return throw ...;</c> is CS8115).
    /// </remarks>
    public static BlockSyntax Guard(ArrowExpressionClauseSyntax expressionBody, SyntaxToken semicolon,
        ITypeSymbol returnType, SyntaxTrivia endOfLine)
    {
        var comments = CommentLines(expressionBody.ArrowToken.LeadingTrivia, endOfLine)
            .AddRange(CommentLines(expressionBody.ArrowToken.TrailingTrivia, endOfLine))
            .AddRange(CommentLines(expressionBody.Expression.GetLeadingTrivia(), endOfLine));

        var afterSemicolon = semicolon.TrailingTrivia;
        var statementPart = IndexAfterLastComment(afterSemicolon);
        var withStatement = Slice(afterSemicolon, 0, statementPart);
        if (statementPart > 0 && afterSemicolon[statementPart - 1].IsKind(SyntaxKind.SingleLineCommentTrivia))
            // The closing brace of the try block follows: it must not end up inside the comment.
            withStatement = withStatement.Add(endOfLine);

        var statementSemicolon = semicolon.WithTrailingTrivia(withStatement.Add(SyntaxFactory.ElasticMarker));
        var expression = expressionBody.Expression.WithoutLeadingTrivia();
        StatementSyntax statement = expression switch
        {
            ThrowExpressionSyntax throwExpression => SyntaxFactory.ThrowStatement(throwExpression.ThrowKeyword,
                throwExpression.Expression, statementSemicolon),
            _ when returnType.SpecialType == SpecialType.System_Void => SyntaxFactory.ExpressionStatement(expression,
                statementSemicolon),
            _ => SyntaxFactory.ReturnStatement(expression).WithSemicolonToken(statementSemicolon)
        };

        var tryBlock = SyntaxFactory.Block(statement.WithLeadingTrivia(comments));
        return SyntaxFactory.Block(CreateTry(tryBlock, returnType))
            .WithTrailingTrivia(Slice(afterSemicolon, statementPart, afterSemicolon.Count))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    ///     Whether the expression body can be converted: not when a preprocessor directive sits between the signature
    ///     and the semicolon. <c>#if</c> branches there are halves of an expression body; as statements of a block the
    ///     inactive half would no longer compile, and the matching <c>#endif</c> lies outside the declaration.
    /// </summary>
    public static bool CanGuard(ArrowExpressionClauseSyntax expressionBody, SyntaxToken semicolon)
    {
        return !expressionBody.ContainsDirectives && !semicolon.ContainsDirectives;
    }

    /// <summary>
    ///     The line break the document already uses, so that the inserted comment line does not introduce a second
    ///     convention; an elastic CR LF (replaced by the formatter's own choice) when the document has no line break.
    /// </summary>
    public static SyntaxTrivia DetectEndOfLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var length = line.EndIncludingLineBreak - line.End;
            if (length > 0)
                return SyntaxFactory.ElasticEndOfLine(
                    text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak)));
        }

        return SyntaxFactory.ElasticCarriageReturnLineFeed;
    }

    // Every comment of the list, each followed by a line break; white space and line breaks are dropped.
    private static SyntaxTriviaList CommentLines(SyntaxTriviaList trivia, SyntaxTrivia endOfLine)
    {
        var comments = SyntaxFactory.TriviaList();
        foreach (var item in trivia)
            if (IsComment(item))
                comments = comments.Add(item).Add(endOfLine);

        return comments;
    }

    private static int IndexAfterLastComment(SyntaxTriviaList trivia)
    {
        for (var index = trivia.Count - 1; index >= 0; index--)
            if (IsComment(trivia[index]))
                return index + 1;

        return 0;
    }

    private static SyntaxTriviaList Slice(SyntaxTriviaList trivia, int start, int end)
    {
        var slice = SyntaxFactory.TriviaList();
        for (var index = start; index < end; index++) slice = slice.Add(trivia[index]);

        return slice;
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;
    }

    private static TryStatementSyntax CreateTry(BlockSyntax tryBlock, ITypeSymbol returnType)
    {
        var exceptionType = SyntaxFactory.ParseTypeName("global::System.Exception")
            .WithAdditionalAnnotations(Simplifier.Annotation);
        var catchClause = SyntaxFactory.CatchClause()
            .WithDeclaration(SyntaxFactory.CatchDeclaration(exceptionType))
            .WithBlock(CreateCatchBlock(returnType));

        return SyntaxFactory.TryStatement(tryBlock, SyntaxFactory.SingletonList(catchClause), null);
    }

    private static BlockSyntax CreateCatchBlock(ITypeSymbol returnType)
    {
        return SyntaxFactory.Block(SyntaxFactory.ReturnStatement(CreateFailureValue(returnType)));
    }

    // The provider verifies the exact CE bootstrap signature before calling this rewriter. Keep that precondition
    // here too, so a future caller cannot silently turn an unknown callback into a generic "return 0" fix.
    private static LiteralExpressionSyntax CreateFailureValue(ITypeSymbol returnType)
    {
        if (returnType.SpecialType != SpecialType.System_Int32)
            throw new ArgumentException("The CE bootstrap failure convention returns Int32.", nameof(returnType));

        return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
    }
}

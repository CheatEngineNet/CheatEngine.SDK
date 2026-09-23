using System.Text;
using System.Threading;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     The <c>ForAttributeWithMetadataName</c> transform for <c>[LuaGlobal]</c> on a method: the only place of that
///     pipeline that touches symbols or syntax. It reduces the attributed declaration to a <see cref="LuaGlobalModel" />.
/// </summary>
internal static class LuaGlobalParser
{
	/// <summary>Builds the model of one attributed method declaration.</summary>
	public static LuaGlobalModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		IMethodSymbol method = (IMethodSymbol) context.TargetSymbol;
		Compilation compilation = context.SemanticModel.Compilation;
		bool isSdkAttribute = LuaBindingSymbols.ContainsSdkAttribute(context.Attributes, compilation,
			LuaBindingsGenerator.LuaGlobalAttributeMetadataName);
		string? luaName = LuaBindingSymbols.ReadSdkAttributeName(context.Attributes, compilation,
			LuaBindingsGenerator.LuaGlobalAttributeMetadataName);

		LuaGlobalShapeIssues issues = LuaGlobalShape.Inspect(compilation, method,
			LuaBindingSymbols.ResolveLuaState(compilation),
			LuaBindingSymbols.ResolveLuaMarshallerAttribute(compilation),
			LuaBindingSymbols.ResolveLuaMarshallerContract(compilation),
			LuaBindingSymbols.ResolveLuaOptional(compilation),
			LuaBindingSymbols.ResolveLuaOperationStatus(compilation), out LuaGlobalSignature signature);
		if (!isSdkAttribute || !LuaNames.IsValidName(luaName))
		{
			issues |= LuaGlobalShapeIssues.InvalidName;
		}

		ContainingTypeIssues typeIssues = ContainingTypeShape.Inspect(method.ContainingType, cancellationToken);
		ContainingTypeModel containingType = ContainingTypeParser.Parse(method.ContainingType);
		bool hasGeneratedIdentityCollision = HasGeneratedIdentityCollision(method, luaName);

		LuaGlobalCallModel? call = null;
		if (issues == LuaGlobalShapeIssues.None && typeIssues == ContainingTypeIssues.None)
		{
			call = new LuaGlobalCallModel(
				luaName!,
				LuaGlobalCallModel.CacheFieldFor(luaName!),
				Modifiers(context.TargetNode as MethodDeclarationSyntax),
				Identifiers.Escape(method.Name),
				signature.StateParameterName,
				signature.Arguments,
				signature.Form,
				signature.Results,
				signature.ReturnKind,
				signature.ReturnIsNullable,
				method.IsExtensionMethod,
				containingType.FullyQualifiedName + "." + LuaGlobalCallModel.CacheFieldFor(luaName!),
				signature.ReturnMarshaller);
		}

		return new LuaGlobalModel(containingType, typeIssues, issues, call, SortKey(method),
			hasGeneratedIdentityCollision);
	}

	// The implementing declaration must repeat the defining declaration's accessibility, 'new', 'static' and
	// 'unsafe' exactly (CS8799, CS0763, CS0764), and must not add an accessibility to an old-style partial method
	// that has none. Read from the syntax, in a canonical order, 'partial' last.
	private static string Modifiers(MethodDeclarationSyntax? declaration)
	{
		StringBuilder modifiers = new();
		bool isNew = false;
		bool isUnsafe = false;
		if (declaration is not null)
		{
			foreach (SyntaxToken token in declaration.Modifiers)
			{
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
					case SyntaxKind.UnsafeKeyword:
						isUnsafe = true;
						break;
				}
			}
		}

		if (isNew)
		{
			Append(modifiers, "new");
		}

		Append(modifiers, "static");
		if (isUnsafe)
		{
			Append(modifiers, "unsafe");
		}

		Append(modifiers, "partial");
		return modifiers.ToString();
	}

	private static void Append(StringBuilder modifiers, string modifier)
	{
		if (modifiers.Length > 0)
		{
			modifiers.Append(' ');
		}

		modifiers.Append(modifier);
	}

	// Name and parameter types: overloads of one name (a copy-out and a string form of the same global) must
	// still sort deterministically.
	private static string SortKey(IMethodSymbol method)
	{
		StringBuilder key = new(method.Name);
		key.Append('(');
		for (int i = 0; i < method.Parameters.Length; i++)
		{
			if (i > 0)
			{
				key.Append(", ");
			}

			IParameterSymbol parameter = method.Parameters[i];
			if (parameter.RefKind != RefKind.None)
			{
				key.Append(parameter.RefKind.ToString()).Append(' ');
			}

			key.Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		}

		return key.Append(')').ToString();
	}

	private static bool HasGeneratedIdentityCollision(IMethodSymbol method, string? luaName)
	{
		foreach (IParameterSymbol parameter in method.Parameters)
		{
			if (LuaGlobalCallEmitter.IsReservedLocal(parameter.Name))
			{
				return true;
			}
		}

		return LuaNames.IsValidName(luaName)
			   && method.ContainingType.GetMembers(LuaGlobalCallModel.CacheFieldFor(luaName!)).Length != 0;
	}
}

using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;

/// <summary>Emits the borrowed-handle implementation for one valid <c>[LuaClass]</c> readonly partial struct.</summary>
internal static class LuaClassFileEmitter
{
	/// <summary>Stable suffix for generated class-handle files.</summary>
	public const string HintSuffix = ".LuaClass.g.cs";

	private static readonly string GeneratedCodeAttribute =
		GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(LuaClassFileEmitter));

	/// <summary>Returns the deterministic source hint name.</summary>
	public static string HintName(LuaClassModel model)
	{
		return model.HintName;
	}

	/// <summary>Writes the complete generated partial part.</summary>
	public static SourceText Emit(LuaClassModel model)
	{
		SourceWriter writer = new(2048);
		GeneratedCodeText.WriteFileHeader(writer);
		OpenHandleType(writer, model.ContainingType);
		WriteMembers(writer, model.ContainingType.FullyQualifiedName, model.ContainingType.Declarations[^1].Name);
		CloseHandleType(writer, model.ContainingType);
		return writer.ToSourceText();
	}

	private static void OpenHandleType(SourceWriter writer, ContainingTypeModel type)
	{
		if (type.Namespace.Length > 0)
		{
			writer.Write("namespace ");
			writer.WriteLine(type.Namespace);
			writer.OpenBlock();
		}

		for (int i = 0; i < type.Declarations.Length; i++)
		{
			TypeDeclarationModel declaration = type.Declarations[i];
			if (declaration.IsReadOnly)
			{
				writer.Write("readonly ");
			}

			writer.Write("partial ");
			writer.Write(declaration.Keyword);
			writer.Write(' ');
			writer.Write(declaration.Name);
			if (i == type.Declarations.Length - 1)
			{
				writer.Write(" : global::System.IEquatable<");
				writer.Write(type.FullyQualifiedName);
				writer.Write(">, ");
				writer.Write(LuaApiNames.ICEObject);
				writer.Write('<');
				writer.Write(type.FullyQualifiedName);
				writer.Write(">, ");
				writer.Write(LuaApiNames.ILuaMarshaller);
				writer.Write('<');
				writer.Write(type.FullyQualifiedName);
				writer.Write('>');
			}

			writer.WriteLine();
			writer.OpenBlock();
		}
	}

	private static void CloseHandleType(SourceWriter writer, ContainingTypeModel type)
	{
		for (int i = 0; i < type.Declarations.Length; i++)
		{
			writer.CloseBlock();
		}

		if (type.Namespace.Length > 0)
		{
			writer.CloseBlock();
		}
	}

	private static void WriteMembers(SourceWriter writer, string selfType, string constructorName)
	{
		WriteStorage(writer, selfType, constructorName);
		WriteHandleConversion(writer, selfType);
		WriteEquality(writer, selfType);
		WriteMarshaller(writer, selfType);
	}

	private static void WriteStorage(SourceWriter writer, string selfType, string constructorName)
	{
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("private readonly ");
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(" _handle;");
		writer.WriteLine();

		writer.WriteLine(GeneratedCodeAttribute);
		writer.WriteLine("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
		writer.Write("private ");
		writer.Write(constructorName);
		writer.Write('(');
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(" handle)");
		writer.OpenBlock();
		writer.WriteLine("_handle = handle;");
		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteHandleConversion(SourceWriter writer, string selfType)
	{
		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public ");
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(" Handle => _handle;");
		writer.WriteLine();

		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static ");
		writer.Write(selfType);
		writer.Write(" FromHandle(");
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(" handle)");
		writer.OpenBlock();
		writer.WriteLine("return new(handle);");
		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteEquality(SourceWriter writer, string selfType)
	{
		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public bool Equals(");
		writer.Write(selfType);
		writer.WriteLine(" other)");
		writer.OpenBlock();
		writer.WriteLine("return _handle == other._handle;");
		writer.CloseBlock();
		writer.WriteLine();

		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.WriteLine("public override bool Equals(object? obj)");
		writer.OpenBlock();
		writer.Write("return obj is ");
		writer.Write(selfType);
		writer.WriteLine(" other && Equals(other);");
		writer.CloseBlock();
		writer.WriteLine();

		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.WriteLine("public override int GetHashCode()");
		writer.OpenBlock();
		writer.WriteLine("return _handle.GetHashCode();");
		writer.CloseBlock();
		writer.WriteLine();

		WriteOperators(writer, selfType);
	}

	private static void WriteOperators(SourceWriter writer, string selfType)
	{
		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static bool operator ==(");
		writer.Write(selfType);
		writer.Write(" left, ");
		writer.Write(selfType);
		writer.WriteLine(" right)");
		writer.OpenBlock();
		writer.WriteLine("return left.Equals(right);");
		writer.CloseBlock();
		writer.WriteLine();

		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static bool operator !=(");
		writer.Write(selfType);
		writer.Write(" left, ");
		writer.Write(selfType);
		writer.WriteLine(" right)");
		writer.OpenBlock();
		writer.WriteLine("return !left.Equals(right);");
		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteMarshaller(SourceWriter writer, string selfType)
	{
		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static void Push(");
		writer.Write(LuaApiNames.LuaState);
		writer.Write(" state, ");
		writer.Write(selfType);
		writer.WriteLine(" value)");
		writer.OpenBlock();
		writer.WriteLine("value._handle.Push(state);");
		writer.CloseBlock();
		writer.WriteLine();

		writer.WriteLine("/// <inheritdoc />");
		writer.WriteLine(GeneratedCodeAttribute);
		writer.Write("public static bool TryRead(");
		writer.Write(LuaApiNames.LuaState);
		writer.Write(" state, int index, out ");
		writer.Write(selfType);
		writer.WriteLine(" value)");
		writer.OpenBlock();
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(" __handle;");
		writer.Write("if (");
		writer.Write(LuaApiNames.CEObject);
		writer.WriteLine(".TryRead(state, index, out __handle))");
		writer.OpenBlock();
		writer.WriteLine("value = new(__handle);");
		writer.WriteLine("return true;");
		writer.CloseBlock();
		writer.WriteLine();
		writer.WriteLine("value = default;");
		writer.WriteLine("return false;");
		writer.CloseBlock();
	}
}

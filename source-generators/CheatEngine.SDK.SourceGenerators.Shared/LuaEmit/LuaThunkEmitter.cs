using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     Writes one <c>lua_CFunction</c> thunk around a managed static method: the shape
///     <c>CheatEngine.SDK.Lua.Callbacks.LuaThunk</c> documents, with argument-count and argument-kind checks in front of
///     the call.
/// </summary>
/// <remarks>
///     <para>
///         The thunk is <c>[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })] static int (nint)</c>: what
///         <c>LuaNativeFunction</c>'s typed constructor accepts, so <c>&amp;Thunk</c> is checked by the compiler. It wraps
///         the handle in a <c>LuaState</c> (the state Lua passed, never one acquired from the runtime), and its whole body
///         is one <see langword="try" /> whose catch-all converts any exception into <c>LuaThunk.Fail(L, exception)</c>:
///         no managed
///         exception reaches the native boundary, and no Lua API that can raise is called (the pushes are the only Lua
///         allocations, as in every C function).
///     </para>
///     <para>
///         Checks, in order: the argument count must equal the number of Lua parameters (Lua's own functions ignore extra
///         arguments; a managed method has a fixed arity, so a mismatch is reported:
///         <c>
///             wrong number of arguments to 'add'
///             (2 expected)
///         </c>
///         ); every argument is read through its marshaller and a value of the wrong kind, or an absent
///         one, is reported with Lua's wording through <c>LuaThunk.FailBadArgument</c> (
///         <c>
///             bad argument #1 (integer
///             expected, got nil)
///         </c>
///         ). All failures travel through the runtime's error channel (sentinel + message, turned
///         into <c>error(message, 2)</c> by the Lua-side wrapper), never through <c>lua_error</c>.
///     </para>
///     <para>
///         Locals are numbered and double-underscore prefixed (<c>__L</c>, <c>__arg0</c>, <c>__result</c>,
///         <c>__exception</c>): the target's parameter names are never used, so nothing can collide.
///     </para>
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaThunkEmitter
{
	private const string Handle = "__handle";
	private const string State = "__L";
	private const string ArgumentPrefix = "__arg";
	private const string Result = "__result";
	private const string Exception = "__exception";

	/// <summary>Writes the attribute, signature and body of the thunk at the writer's current indentation.</summary>
	public static void Emit(SourceWriter writer, LuaThunkModel model)
	{
		if (writer is null)
		{
			throw new ArgumentNullException(nameof(writer));
		}

		if (model is null)
		{
			throw new ArgumentNullException(nameof(model));
		}

		writer.WriteLine(LuaApiNames.UnmanagedCallersOnlyCdecl);
		writer.Write("private static int ");
		writer.Write(model.ThunkMethodName);
		writer.Write("(nint ");
		writer.Write(Handle);
		writer.WriteLine(")");
		writer.OpenBlock();

		writer.Write(LuaApiNames.LuaState);
		writer.Write(' ');
		writer.Write(State);
		writer.Write(" = new(");
		writer.Write(Handle);
		writer.WriteLine(");");
		writer.WriteLine("try");
		writer.OpenBlock();
		WriteArgumentCountCheck(writer, model);
		for (int i = 0; i < model.Arguments.Length; i++)
		{
			writer.WriteLine();
			WriteArgumentRead(writer, model.Arguments[i], i);
		}

		writer.WriteLine();
		WriteCallAndResult(writer, model);
		writer.CloseBlock();
		WriteCatchAll(writer);

		writer.CloseBlock();
	}

	/// <summary>The message a thunk reports when it is called with the wrong number of arguments.</summary>
	public static string WrongArgumentCountMessage(string luaName, int expected)
	{
		return "wrong number of arguments to '" + luaName + "' (" + expected.ToString(CultureInfo.InvariantCulture) +
			   " expected)";
	}

	// The count check first, so that a missing argument and a surplus one get the same, complete message.
	private static void WriteArgumentCountCheck(SourceWriter writer, LuaThunkModel model)
	{
		int count = model.Arguments.Length;
		writer.Write("if (");
		writer.Write(State);
		writer.Write(".Top != ");
		writer.Write(count.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(")");
		writer.OpenBlock();
		writer.Write("return ");
		writer.Write(LuaApiNames.LuaThunk);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(CSharpLiteral.ToUtf8Literal(WrongArgumentCountMessage(model.LuaName, count)));
		writer.WriteLine(");");
		writer.CloseBlock();
	}

	// One read per argument, at its 1-based stack index, which is also the argument number in the message.
	private static void WriteArgumentRead(SourceWriter writer, LuaArgumentModel argument, int index)
	{
		string position = (index + 1).ToString(CultureInfo.InvariantCulture);
		writer.Write("if (!");
		writer.Write(argument.GeneratedMarshallerTypeName);
		writer.Write(".TryRead(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(position);
		writer.Write(", out ");
		// A string local is declared nullable: the marshaller's out parameter is [MaybeNullWhen(false)], and the
		// flow analysis knows it is not null once the read succeeded, so it flows into a 'string' parameter.
		writer.Write(argument.CustomMarshaller?.ValueTypeName ?? LuaValueKinds.TypeName(argument.Kind, true));
		writer.Write(' ');
		writer.Write(ArgumentPrefix);
		writer.Write(index.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine("))");
		writer.OpenBlock();
		writer.Write("return ");
		writer.Write(LuaApiNames.LuaThunk);
		writer.Write(".FailBadArgument(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(position);
		writer.Write(", ");
		writer.Write(CSharpLiteral.ToUtf8Literal(argument.ExpectedArgumentTypeName));
		writer.WriteLine(");");
		writer.CloseBlock();
	}

	// The call, its result pushed as the single Lua result.
	private static void WriteCallAndResult(SourceWriter writer, LuaThunkModel model)
	{
		if (model.HasReturn)
		{
			// A string target may return null (pushed as nil), whatever its annotation says.
			writer.Write(model.ReturnTypeName);
			writer.Write(' ');
			writer.Write(Result);
			writer.Write(" = ");
		}

		writer.Write(model.TargetMethod);
		writer.Write('(');
		bool first = true;
		if (model.PassesState)
		{
			writer.Write(State);
			first = false;
		}

		for (int i = 0; i < model.Arguments.Length; i++)
		{
			if (!first)
			{
				writer.Write(", ");
			}

			first = false;
			writer.Write(ArgumentPrefix);
			writer.Write(i.ToString(CultureInfo.InvariantCulture));
		}

		writer.WriteLine(");");

		if (model.HasReturn)
		{
			writer.Write(model.ReturnMarshallerTypeName);
			writer.Write(".Push(");
			writer.Write(State);
			writer.Write(", ");
			writer.Write(Result);
			writer.WriteLine(");");
			writer.WriteLine("return 1;");
		}
		else
		{
			writer.WriteLine("return 0;");
		}
	}

	private static void WriteCatchAll(SourceWriter writer)
	{
		writer.Write("catch (");
		writer.Write(LuaApiNames.Exception);
		writer.Write(' ');
		writer.Write(Exception);
		writer.WriteLine(")");
		writer.OpenBlock();
		writer.Write("return ");
		writer.Write(LuaApiNames.LuaThunk);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Exception);
		writer.WriteLine(");");
		writer.CloseBlock();
	}
}

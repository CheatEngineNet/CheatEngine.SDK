using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     Writes one wrapper method that calls a Lua global, in the call shape <c>CheatEngine.SDK.Lua</c> defines (worked
///     examples
///     in <c>tests/CheatEngine.SDK.Lua.Tests/Generated/</c>).
/// </summary>
/// <remarks>
///     <para>
///         The body records the state top before pushing the cached global through <c>LuaGlobalFunctions.TryPush</c>,
///         pushes one marshalled value per argument, calls once, reads the results, then returns. A
///         <see langword="finally" />
///         restores the recorded top even when a Lua operation throws a managed <c>LuaException</c>; Try wrappers
///         translate that exception to <see langword="false" /> with defaulted results. There is no
///         <see langword="string" /> at run
///         time: the name is a <c>u8</c> literal.
///     </para>
///     <para>
///         Three exits besides success, each a call into <c>LuaCallSupport</c> that restores the stack first: the global
///         could not be resolved, the call raised, a result is not of the declared kind (Cheat Engine's <c>nil</c> for
///         "failed" included). The Try form returns <see langword="false" /> with every result defaulted; the throwing
///         form
///         raises a <c>LuaException</c> through <c>[DoesNotReturn]</c> helpers, so the compiler sees no fall-through.
///     </para>
///     <para>
///         Local names start with two underscores (<c>__L</c>, <c>__top</c>, <c>__ok</c>, <c>__status</c>, <c>__result</c>
///         ):
///         a leading double underscore is not a reserved C# identifier form, so this is a convention rather than a
///         language guarantee. The LuaBindings parser validates the generated local and cache identities before this
///         emitter is reached; an annotated declaration with a colliding parameter or user field is skipped and its
///         sibling bindings remain usable.
///     </para>
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaGlobalCallEmitter
{
	/// <summary>
	///     Bodies that push or keep more values than this call <c>TryEnsureStack</c> first: a C function is
	///     guaranteed <c>LUA_MINSTACK</c> = 20 free slots (Lua 5.3 manual, section 4.2) and the protected helpers
	///     need up to four.
	/// </summary>
	public const int StackCheckThreshold = 16;

	private const string State = "__L";
	private const string Operation = "__operation";
	private const string Top = "__top";
	private const string Ok = "__ok";
	private const string Status = "__status";
	private const string Result = "__result";
	private const string ArgumentCount = "__argc";
	private const string Rest = "__rest";

	/// <summary>
	///     Whether <paramref name="name" /> is a local of a generated body: a parameter with that name would collide with it,
	///     so the LuaBindings generator skips such a declaration and CESDK2007 names the parameter.
	/// </summary>
	public static bool IsReservedLocal(string name)
	{
		return name is State or Operation or Top or Ok or Status or Result or "__resolution" or "__exception"
			or ArgumentCount or Rest;
	}

	/// <summary>Writes the method, signature and body, at the writer's current indentation.</summary>
	public static void Emit(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (writer is null)
		{
			throw new ArgumentNullException(nameof(writer));
		}

		if (model is null)
		{
			throw new ArgumentNullException(nameof(model));
		}

		WriteSignature(writer, model);
		writer.OpenBlock();
		WriteBody(writer, model);
		writer.CloseBlock();
	}

	/// <summary>Writes the parameter list, parentheses included: the state, the arguments, then the results.</summary>
	public static void WriteParameterList(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (writer is null)
		{
			throw new ArgumentNullException(nameof(writer));
		}

		if (model is null)
		{
			throw new ArgumentNullException(nameof(model));
		}

		writer.Write('(');
		bool first = true;
		bool isExtensionReceiver = model.IsExtensionMethod;
		if (model.TakesState)
		{
			if (isExtensionReceiver)
			{
				writer.Write("this ");
			}

			writer.Write(LuaApiNames.LuaState);
			writer.Write(' ');
			writer.Write(model.StateParameterName);
			first = false;
			isExtensionReceiver = false;
		}

		for (int i = 0; i < model.Arguments.Length; i++)
		{
			LuaArgumentModel argument = model.Arguments[i];
			if (argument.IsFixed)
			{
				continue;
			}

			WriteSeparator(writer, ref first);
			WriteArgumentParameter(writer, argument, isExtensionReceiver);
			isExtensionReceiver = false;
		}

		if (model.IsTryLike)
		{
			foreach (LuaResultModel result in model.Results)
			{
				WriteSeparator(writer, ref first);
				WriteResultParameter(writer, result, isExtensionReceiver);
				isExtensionReceiver = false;
			}
		}

		writer.Write(')');
	}

	// 'scoped <type> name': the scoped modifier is written first when the declaration used it (Utf8 is the only
	// argument kind of ref struct type; every other kind's IsScoped is always false, see LuaArgumentModel).
	private static void WriteArgumentParameter(SourceWriter writer, LuaArgumentModel argument, bool isExtensionReceiver)
	{
		if (isExtensionReceiver)
		{
			writer.Write("this ");
		}

		if (argument.IsScoped)
		{
			writer.Write("scoped ");
		}

		writer.Write(argument.GeneratedTypeName);
		writer.Write(' ');
		writer.Write(argument.Name);
	}

	// 'scoped Span<byte> destination, out int written', 'Span<T> values, out int count' or 'out <type> name'.
	private static void WriteResultParameter(SourceWriter writer, LuaResultModel result, bool isExtensionReceiver)
	{
		if (result.Shape is LuaResultShape.CopyOut or LuaResultShape.Variadic)
		{
			if (isExtensionReceiver)
			{
				writer.Write("this ");
			}

			if (result.DestinationIsScoped)
			{
				writer.Write("scoped ");
			}

			writer.Write(result.Shape == LuaResultShape.CopyOut
				? LuaApiNames.SpanOfByte
				: LuaApiNames.Span + "<" + LuaValueKinds.TypeName(result.Kind) + ">");
			writer.Write(' ');
			writer.Write(result.DestinationName);
			writer.Write(", out int ");
		}
		else
		{
			writer.Write("out ");
			writer.Write(result.GeneratedTypeName);
			writer.Write(' ');
		}

		writer.Write(result.Name);
	}

	/// <summary>
	///     The return type as written in the signature: <see langword="bool" /> for the Try form, the result type or
	///     <see langword="void" />
	///     otherwise.
	/// </summary>
	public static string ReturnTypeName(LuaGlobalCallModel model)
	{
		if (model is null)
		{
			throw new ArgumentNullException(nameof(model));
		}

		return model.Form == LuaCallForm.Try
			? "bool"
			: model.Form == LuaCallForm.Outcome
				? LuaApiNames.LuaOperationStatus
				: model.HasReturn
					? model.ReturnTypeName
					: "void";
	}

	private static void WriteSignature(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (model.Modifiers.Length > 0)
		{
			writer.Write(model.Modifiers);
			writer.Write(' ');
		}

		writer.Write(ReturnTypeName(model));
		writer.Write(' ');
		writer.Write(model.MethodName);
		WriteParameterList(writer, model);
		writer.WriteLine();
	}

	private static void WriteBody(SourceWriter writer, LuaGlobalCallModel model)
	{
		int argumentCount = model.Arguments.Length;
		int resultCount = model.ResultCount;

		if (model.HasOptionalArguments)
		{
			WriteArgumentCount(writer, model);
		}

		WriteStateAndTop(writer, model);
		WriteProtectedBody(writer, model, argumentCount, resultCount);
		WriteExceptionHandler(writer, model);
		WriteStackRestore(writer);
	}

	private static void WriteProtectedBody(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		writer.WriteLine("try");
		writer.OpenBlock();

		// Only bodies that would exceed the guaranteed free slots check the stack. A call that keeps every result
		// (LUA_MULTRET) needs no room for them in advance: Lua makes the returned values fit.
		int slots = model.HasDynamicResults ? 1 + argumentCount : Math.Max(1 + argumentCount, resultCount);
		if (slots > StackCheckThreshold)
		{
			WriteStackCheck(writer, model, slots);
		}

		WriteGlobalPush(writer, model);
		WriteArguments(writer, model);
		WriteCallAndResults(writer, model, argumentCount, resultCount);
		writer.CloseBlock();
	}

	private static void WriteArguments(SourceWriter writer, LuaGlobalCallModel model)
	{
		for (int i = 0; i < model.Arguments.Length; i++)
		{
			LuaArgumentModel argument = model.Arguments[i];
			if (argument.IsOptional)
			{
				WriteOptionalArgument(writer, argument, i);
				continue;
			}

			writer.Write(argument.GeneratedMarshallerTypeName);
			writer.Write(".Push(");
			writer.Write(State);
			writer.Write(", ");
			writer.Write(argument.FixedValue ?? argument.Name);
			writer.WriteLine(");");
		}
	}

	// An optional argument is pushed only when the computed count reaches its position: a present value through its
	// marshaller, Nil as nil. The count already stops before the first omitted argument.
	private static void WriteOptionalArgument(SourceWriter writer, LuaArgumentModel argument, int position)
	{
		writer.Write("if (");
		writer.Write(ArgumentCount);
		writer.Write(" > ");
		writer.Write(position.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(")");
		writer.OpenBlock();
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".PushOptional<");
		writer.Write(LuaValueKinds.TypeName(argument.Kind));
		writer.Write(", ");
		writer.Write(argument.GeneratedMarshallerTypeName);
		writer.Write(">(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(argument.Name);
		writer.WriteLine(");");
		writer.CloseBlock();
	}

	// Before the state is acquired: the number of arguments to push is the position after the last optional argument
	// that is not omitted. An omitted argument followed by a present one cannot be expressed in Lua, so it is a caller
	// error, thrown before anything touches Lua (also while no runtime is attached).
	private static void WriteArgumentCount(SourceWriter writer, LuaGlobalCallModel model)
	{
		int required = 0;
		while (required < model.Arguments.Length && !model.Arguments[required].IsOptional)
		{
			required++;
		}

		writer.Write("int ");
		writer.Write(ArgumentCount);
		writer.Write(" = ");
		for (int i = model.Arguments.Length - 1; i >= required; i--)
		{
			writer.Write('!');
			writer.Write(model.Arguments[i].Name);
			writer.Write(".IsOmitted ? ");
			writer.Write((i + 1).ToString(CultureInfo.InvariantCulture));
			writer.Write(" : ");
		}

		writer.Write(required.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(";");
		for (int i = required; i < model.Arguments.Length - 1; i++)
		{
			string name = UnescapedName(model.Arguments[i].Name);
			writer.Write("if (");
			writer.Write(ArgumentCount);
			writer.Write(" > ");
			writer.Write((i + 1).ToString(CultureInfo.InvariantCulture));
			writer.Write(" && ");
			writer.Write(model.Arguments[i].Name);
			writer.WriteLine(".IsOmitted)");
			writer.OpenBlock();
			writer.Write("throw new ");
			writer.Write(LuaApiNames.ArgumentException);
			writer.Write('(');
			writer.Write(CSharpLiteral.ToStringLiteral(OmittedBeforePresentMessage(model.GlobalName, name)));
			writer.Write(", ");
			writer.Write(CSharpLiteral.ToStringLiteral(name));
			writer.WriteLine(");");
			writer.CloseBlock();
		}

		writer.WriteLine();
	}

	/// <summary>
	///     The message of the <c>ArgumentException</c> a wrapper throws, before touching Lua, when an optional argument is
	///     omitted while a later one is present.
	/// </summary>
	public static string OmittedBeforePresentMessage(string globalName, string parameterName)
	{
		return "The optional argument '" + parameterName + "' of the Lua global '" + globalName +
			   "' is omitted while a later optional argument is present; Lua cannot receive an argument after an absent one. Pass LuaOptional.Nil<T>() to send nil in its place.";
	}

	private static string UnescapedName(string name)
	{
		return name.Length > 0 && name[0] == '@' ? name.Substring(1) : name;
	}

	private static string ArgumentCountText(LuaGlobalCallModel model, int argumentCount)
	{
		return model.HasOptionalArguments ? ArgumentCount : argumentCount.ToString(CultureInfo.InvariantCulture);
	}

	private static string ResultCountText(LuaGlobalCallModel model, int resultCount)
	{
		return model.HasDynamicResults
			? LuaApiNames.LuaState + ".MultipleResults"
			: resultCount.ToString(CultureInfo.InvariantCulture);
	}

	// The absolute stack index of result 'position' after a LUA_MULTRET call: the function was pushed at __top + 1 and
	// the call replaced it, with its arguments, by the results.
	private static string AbsoluteResultIndex(int position)
	{
		return Top + " + " + (position + 1).ToString(CultureInfo.InvariantCulture);
	}

	private static void WriteCallAndResults(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		if (model.Form == LuaCallForm.Try)
		{
			if (model.HasDynamicResults)
			{
				WriteDynamicTryCallAndResults(writer, model, argumentCount);
			}
			else
			{
				WriteTryCallAndResults(writer, model, argumentCount, resultCount);
			}
		}
		else if (model.Form == LuaCallForm.Outcome)
		{
			WriteOutcomeCallAndResults(writer, model, argumentCount, resultCount);
		}
		else
		{
			WriteThrowingCall(writer, model, argumentCount, resultCount);
			WriteThrowingResult(writer, model);
		}
	}

	private static void WriteExceptionHandler(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (model.Form == LuaCallForm.Try)
		{
			writer.Write("catch (");
			writer.Write(LuaApiNames.LuaException);
			writer.WriteLine(")");
			writer.OpenBlock();
			WriteTryExceptionFailure(writer, model);
			writer.CloseBlock();
		}
		else if (model.Form == LuaCallForm.Outcome)
		{
			writer.Write("catch (");
			writer.Write(LuaApiNames.LuaException);
			writer.Write(' ');
			writer.Write("__exception");
			writer.WriteLine(")");
			writer.OpenBlock();
			WriteOutcomeFailure(writer, model, LuaApiNames.LuaOperationStatus + ".LuaFailure(__exception.Status)", 0);
			writer.CloseBlock();
		}
	}

	private static void WriteStackRestore(SourceWriter writer)
	{
		writer.WriteLine("finally");
		writer.OpenBlock();
		writer.Write(State);
		writer.Write(".SetTop(");
		writer.Write(Top);
		writer.WriteLine(");");
		writer.CloseBlock();
	}

	private static void WriteStateAndTop(SourceWriter writer, LuaGlobalCallModel model)
	{
		writer.Write("using ");
		writer.Write(LuaApiNames.LuaRuntimeOperation);
		writer.Write(' ');
		writer.Write(Operation);
		writer.Write(" = ");
		if (model.TakesState)
		{
			writer.Write(LuaApiNames.LuaRuntime);
			writer.Write(".AcquireOperation(");
			writer.Write(model.StateParameterName);
			writer.Write(')');
		}
		else
		{
			writer.Write(LuaApiNames.AcquireOperation);
		}

		writer.WriteLine(";");
		writer.Write(LuaApiNames.LuaState);
		writer.Write(' ');
		writer.Write(State);
		writer.Write(" = ");
		writer.Write(Operation);
		writer.Write(".State");
		writer.WriteLine(";");
		writer.Write("int ");
		writer.Write(Top);
		writer.Write(" = ");
		writer.Write(State);
		writer.WriteLine(".Top;");
	}

	private static void WriteStackCheck(SourceWriter writer, LuaGlobalCallModel model, int slots)
	{
		writer.Write("if (!");
		writer.Write(State);
		writer.Write(".TryEnsureStack(");
		writer.Write(slots.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine("))");
		writer.OpenBlock();
		WriteStackExit(writer, model, slots);
		writer.CloseBlock();
		writer.WriteLine();
	}

	// Exit 1: the global could not be resolved.
	private static void WriteGlobalPush(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (model.Form == LuaCallForm.Outcome)
		{
			writer.Write("var __resolution = ");
			writer.Write(LuaApiNames.LuaGlobalFunctions);
			writer.Write(".TryPushWithOutcome(");
			writer.Write(State);
			writer.Write(", ");
			writer.Write(model.CacheFieldReference);
			writer.Write(", ");
			writer.Write(CSharpLiteral.ToUtf8Literal(model.GlobalName));
			writer.WriteLine(");");
			writer.Write("if (!__resolution.IsSuccess)");
			writer.OpenBlock();
			WriteOutcomeFailure(writer, model, "__resolution.ToOperationStatus()", 0);
			writer.CloseBlock();
			writer.WriteLine();
			return;
		}

		writer.Write("if (!");
		writer.Write(LuaApiNames.LuaGlobalFunctions);
		writer.Write(".TryPush(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(model.CacheFieldReference);
		writer.Write(", ");
		writer.Write(CSharpLiteral.ToUtf8Literal(model.GlobalName));
		writer.WriteLine("))");
		writer.OpenBlock();
		if (model.Form == LuaCallForm.Try)
		{
			WriteTryFailure(writer, model, 0);
		}
		else
		{
			writer.Write(LuaApiNames.LuaCallSupport);
			writer.Write(".ThrowUnresolvedGlobal(");
			writer.Write(State);
			writer.Write(", ");
			writer.Write(Top);
			writer.Write(", ");
			writer.Write(CSharpLiteral.ToStringLiteral(model.GlobalName));
			writer.WriteLine(");");
		}

		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteTryCallAndResults(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		// Exit 2: the call raised.
		writer.Write("if (!");
		writer.Write(State);
		writer.Write(".TryCall(");
		writer.Write(ArgumentCountText(model, argumentCount));
		writer.Write(", ");
		writer.Write(resultCount.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(").IsOk)");
		writer.OpenBlock();
		WriteTryFailure(writer, model, 0);
		writer.CloseBlock();
		writer.WriteLine();

		if (resultCount == 1)
		{
			// Exit 3 folded into the return value: one read, restore, return.
			writer.Write("bool ");
			writer.Write(Ok);
			writer.Write(" = ");
			WriteResultRead(writer, model.Results[0], -1);
			writer.WriteLine(";");
		}
		else
		{
			// Exit 3 per result: each read that fails defaults the other results and takes the cold exit.
			for (int i = 0; i < resultCount; i++)
			{
				writer.Write("if (!");
				WriteResultRead(writer, model.Results[i], i - resultCount);
				writer.WriteLine(")");
				writer.OpenBlock();
				WriteTryFailure(writer, model, i);
				writer.CloseBlock();
				writer.WriteLine();
			}
		}

		writer.Write("return ");
		writer.Write(resultCount == 1 ? Ok : "true");
		writer.WriteLine(";");
	}

	// A Try form with optional results: the call keeps every result, a required result Lua did not return is a failure
	// (never read as nil), and each optional result is read at its absolute index or left omitted.
	private static void WriteDynamicTryCallAndResults(SourceWriter writer, LuaGlobalCallModel model,
		int argumentCount)
	{
		writer.Write("if (!");
		writer.Write(State);
		writer.Write(".TryCall(");
		writer.Write(ArgumentCountText(model, argumentCount));
		writer.Write(", ");
		writer.Write(ResultCountText(model, 0));
		writer.WriteLine(").IsOk)");
		writer.OpenBlock();
		WriteTryFailure(writer, model, 0);
		writer.CloseBlock();
		writer.WriteLine();

		int required = model.RequiredResultCount;
		if (required > 0)
		{
			WriteMissingResultCheck(writer, required);
			writer.OpenBlock();
			WriteTryFailure(writer, model, 0);
			writer.CloseBlock();
			writer.WriteLine();
		}

		for (int i = 0; i < model.Results.Length; i++)
		{
			LuaResultModel result = model.Results[i];
			writer.Write("if (!");
			if (result.Shape == LuaResultShape.Optional)
			{
				WriteOptionalResultRead(writer, result, i);
			}
			else
			{
				WriteResultRead(writer, result, AbsoluteResultIndex(i));
			}

			writer.WriteLine(")");
			writer.OpenBlock();
			WriteTryFailure(writer, model, i);
			writer.CloseBlock();
			writer.WriteLine();
		}

		writer.WriteLine("return true;");
	}

	// 'if (__L.Top - __top < n)': fewer results than the required ones. Lua pads a fixed-count call with nil, so only a
	// multiple-results call can tell a missing result from a nil one.
	private static void WriteMissingResultCheck(SourceWriter writer, int required)
	{
		writer.Write("if (");
		writer.Write(State);
		writer.Write(".Top - ");
		writer.Write(Top);
		writer.Write(" < ");
		writer.Write(required.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(")");
	}

	// 'LuaCallSupport.TryReadOptional<T, M>(__L, __top + n, out name)': a position beyond the top is omitted.
	private static void WriteOptionalResultRead(SourceWriter writer, LuaResultModel result, int position)
	{
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".TryReadOptional<");
		writer.Write(LuaValueKinds.TypeName(result.Kind));
		writer.Write(", ");
		writer.Write(result.GeneratedMarshallerTypeName);
		writer.Write(">(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(AbsoluteResultIndex(position));
		writer.Write(", out ");
		writer.Write(result.Name);
		writer.Write(')');
	}

	private static void WriteOutcomeCallAndResults(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		WriteOutcomeCall(writer, model, argumentCount, resultCount);
		if (model.HasDynamicResults)
		{
			WriteDynamicOutcomeResults(writer, model);
		}
		else
		{
			WriteOutcomeResults(writer, model, resultCount);
		}

		writer.Write("return ");
		writer.Write(LuaApiNames.LuaOperationStatus);
		writer.WriteLine(".Success;");
	}

	private static void WriteOutcomeCall(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		writer.Write(LuaApiNames.LuaStatus);
		writer.Write(' ');
		writer.Write(Status);
		writer.Write(" = ");
		writer.Write(State);
		writer.Write(".TryCall(");
		writer.Write(ArgumentCountText(model, argumentCount));
		writer.Write(", ");
		writer.Write(ResultCountText(model, resultCount));
		writer.WriteLine(");");
		writer.Write("if (!");
		writer.Write(Status);
		writer.WriteLine(".IsOk)");
		writer.OpenBlock();
		WriteOutcomeFailure(writer, model, LuaApiNames.LuaOperationStatus + ".LuaFailure(" + Status + ")", 0);
		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteOutcomeResults(SourceWriter writer, LuaGlobalCallModel model, int resultCount)
	{
		for (int i = 0; i < resultCount; i++)
		{
			WriteOutcomeResult(writer, model, i, i - resultCount);
		}
	}

	// The results of an Outcome form with optional or variadic results: fewer results than the required ones is
	// MissingResult, never NilResult; optional results are read or left omitted; the variadic tail is copied by one
	// helper call that classifies its own failures.
	private static void WriteDynamicOutcomeResults(SourceWriter writer, LuaGlobalCallModel model)
	{
		int required = model.RequiredResultCount;
		if (required > 0)
		{
			WriteMissingResultCheck(writer, required);
			writer.OpenBlock();
			WriteOutcomeFailure(writer, model, LuaApiNames.LuaOperationStatus + ".MissingResult", 0);
			writer.CloseBlock();
			writer.WriteLine();
		}

		for (int i = 0; i < model.Results.Length; i++)
		{
			switch (model.Results[i].Shape)
			{
				case LuaResultShape.Optional:
					writer.Write("if (!");
					WriteOptionalResultRead(writer, model.Results[i], i);
					writer.WriteLine(")");
					writer.OpenBlock();
					WriteOutcomeFailure(writer, model, LuaApiNames.LuaOperationStatus + ".InvalidResult", i);
					writer.CloseBlock();
					writer.WriteLine();
					break;
				case LuaResultShape.Variadic:
					WriteVariadicResults(writer, model, i);
					break;
				default:
					WriteOutcomeResult(writer, model, i, AbsoluteResultIndex(i));
					break;
			}
		}
	}

	// 'LuaCallSupport.ReadResults<T, M>(__L, __top + n, values, out count)' copies every value from that index to the
	// top, or reports ResultCapacityExceeded with the needed count, NilResult or InvalidResult. On failure the other
	// results are defaulted and the count keeps what the helper wrote.
	private static void WriteVariadicResults(SourceWriter writer, LuaGlobalCallModel model, int position)
	{
		LuaResultModel result = model.Results[position];
		writer.Write(LuaApiNames.LuaOperationStatus);
		writer.Write(' ');
		writer.Write(Rest);
		writer.Write(" = ");
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".ReadResults<");
		writer.Write(LuaValueKinds.TypeName(result.Kind));
		writer.Write(", ");
		writer.Write(LuaValueKinds.MarshallerTypeName(result.Kind));
		writer.Write(">(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(AbsoluteResultIndex(position));
		writer.Write(", ");
		writer.Write(result.DestinationName);
		writer.Write(", out ");
		writer.Write(result.Name);
		writer.WriteLine(");");
		writer.Write("if (!");
		writer.Write(Rest);
		writer.WriteLine(".IsSuccess)");
		writer.OpenBlock();
		for (int i = 0; i < model.Results.Length; i++)
		{
			if (i == position)
			{
				continue;
			}

			LuaResultModel other = model.Results[i];
			writer.Write(other.Name);
			writer.WriteLine(other.IsReferenceType ? " = default!;" : " = default;");
		}

		writer.Write("return ");
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", ");
		writer.Write(Rest);
		writer.WriteLine(");");
		writer.CloseBlock();
		writer.WriteLine();
	}

	private static void WriteOutcomeResult(SourceWriter writer, LuaGlobalCallModel model, int resultIndex,
		int stackIndex)
	{
		WriteOutcomeResult(writer, model, resultIndex, stackIndex.ToString(CultureInfo.InvariantCulture));
	}

	private static void WriteOutcomeResult(SourceWriter writer, LuaGlobalCallModel model, int resultIndex,
		string stackIndex)
	{
		writer.Write("if (!");
		WriteResultRead(writer, model.Results[resultIndex], stackIndex);
		writer.WriteLine(")");
		writer.OpenBlock();
		// Every other result is defaulted too (all read or none, like the Try form): a declaration with a single result
		// writes nothing here.
		for (int i = 0; i < model.Results.Length; i++)
		{
			if (i == resultIndex)
			{
				continue;
			}

			LuaResultModel other = model.Results[i];
			writer.Write(other.Name);
			writer.WriteLine(other.IsReferenceType ? " = default!;" : " = default;");
		}

		writer.Write("return ");
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", ");
		writer.Write(State);
		writer.Write(".IsNil(");
		writer.Write(stackIndex);
		writer.Write(") ? ");
		writer.Write(LuaApiNames.LuaOperationStatus);
		writer.Write(".NilResult : ");
		writer.Write(LuaApiNames.LuaOperationStatus);
		writer.Write(".InvalidResult, out ");
		writer.Write(model.Results[resultIndex].Name);
		writer.WriteLine(");");
		writer.CloseBlock();
		writer.WriteLine();
	}

	// Exit 2: the call raised; the status travels to the throw helper, which reads the error value.
	private static void WriteThrowingCall(SourceWriter writer, LuaGlobalCallModel model, int argumentCount,
		int resultCount)
	{
		writer.Write(LuaApiNames.LuaStatus);
		writer.Write(' ');
		writer.Write(Status);
		writer.Write(" = ");
		writer.Write(State);
		writer.Write(".TryCall(");
		writer.Write(ArgumentCountText(model, argumentCount));
		writer.Write(", ");
		writer.Write(resultCount.ToString(CultureInfo.InvariantCulture));
		writer.WriteLine(");");
		writer.Write("if (!");
		writer.Write(Status);
		writer.WriteLine(".IsOk)");
		writer.OpenBlock();
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".Throw(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", ");
		writer.Write(Status);
		writer.WriteLine(");");
		writer.CloseBlock();
	}

	// Exit 3: the result is nil or of another kind; the helper names the Lua type it found.
	private static void WriteThrowingResult(SourceWriter writer, LuaGlobalCallModel model)
	{
		if (!model.HasReturn)
		// A void call keeps no result: the successful call already left the stack at its recorded top.
		{
			return;
		}

		writer.WriteLine();
		writer.Write("if (!");
		writer.Write(model.ReturnMarshallerTypeName);
		writer.Write(".TryRead(");
		writer.Write(State);
		writer.Write(", -1, out ");
		// A string local is declared nullable: the marshaller's out parameter is [MaybeNullWhen(false)], and the
		// flow analysis knows it is not null once the read succeeded.
		writer.Write(model.ReturnMarshaller?.ValueTypeName ?? LuaValueKinds.TypeName(model.ReturnKind!.Value, true));
		writer.Write(' ');
		writer.Write(Result);
		writer.WriteLine("))");
		writer.OpenBlock();
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".ThrowUnexpectedResult(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", -1, ");
		writer.Write(CSharpLiteral.ToStringLiteral(model.GlobalName));
		writer.Write(", ");
		writer.Write(CSharpLiteral.ToStringLiteral(model.ExpectedReturnTypeName));
		writer.WriteLine(");");
		writer.CloseBlock();
		writer.WriteLine();
		writer.Write("return ");
		writer.Write(Result);
		writer.WriteLine(";");
	}

	// The read of one result at a (negative) stack index, as a boolean expression.
	private static void WriteResultRead(SourceWriter writer, LuaResultModel result, int index)
	{
		WriteResultRead(writer, result, index.ToString(CultureInfo.InvariantCulture));
	}

	// The read of one result at a stack index expression: a negative constant after a fixed-count call, an absolute
	// '__top + n' after a multiple-results call.
	private static void WriteResultRead(SourceWriter writer, LuaResultModel result, string index)
	{
		if (result.Shape == LuaResultShape.CopyOut)
		{
			writer.Write(State);
			writer.Write(".TryCopyUtf8(");
			writer.Write(index);
			writer.Write(", ");
			writer.Write(result.DestinationName);
			writer.Write(", out ");
			writer.Write(result.Name);
			writer.Write(')');
			return;
		}

		writer.Write(result.GeneratedMarshallerTypeName);
		writer.Write(".TryRead(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(index);
		writer.Write(", out ");
		writer.Write(result.Name);
		writer.Write(')');
	}

	// The Try form's cold exit: every result other than 'failing' is defaulted in place, that one goes through
	// LuaCallSupport.Fail, which restores the stack, defaults it and returns false.
	private static void WriteTryFailure(SourceWriter writer, LuaGlobalCallModel model, int failing)
	{
		for (int i = 0; i < model.Results.Length; i++)
		{
			if (i == failing)
			{
				continue;
			}

			LuaResultModel other = model.Results[i];
			writer.Write(other.Name);
			writer.WriteLine(other.IsReferenceType ? " = default!;" : " = default;");
		}

		writer.Write("return ");
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", out ");
		writer.Write(model.Results[failing].Name);
		writer.WriteLine(");");
	}

	private static void WriteOutcomeFailure(SourceWriter writer, LuaGlobalCallModel model, string status, int failing)
	{
		for (int i = 0; i < model.Results.Length; i++)
		{
			if (i == failing)
			{
				continue;
			}

			LuaResultModel other = model.Results[i];
			writer.Write(other.Name);
			writer.WriteLine(other.IsReferenceType ? " = default!;" : " = default;");
		}

		writer.Write("return ");
		writer.Write(LuaApiNames.LuaCallSupport);
		writer.Write(".Fail(");
		writer.Write(State);
		writer.Write(", ");
		writer.Write(Top);
		writer.Write(", ");
		writer.Write(status);
		if (!model.Results.IsEmpty)
		{
			writer.Write(", out ");
			writer.Write(model.Results[failing].Name);
		}

		writer.WriteLine(");");
	}

	// Push and conversion operations can throw managed LuaException after native failures. A Try wrapper keeps its
	// ordinary failure contract for that path; the surrounding finally restores its stack snapshot.
	private static void WriteTryExceptionFailure(SourceWriter writer, LuaGlobalCallModel model)
	{
		foreach (LuaResultModel result in model.Results)
		{
			writer.Write(result.Name);
			writer.WriteLine(result.IsReferenceType ? " = default!;" : " = default;");
		}

		writer.WriteLine("return false;");
	}

	// The exit taken when the stack cannot grow: nothing was pushed yet, so the top needs no restoring.
	private static void WriteStackExit(SourceWriter writer, LuaGlobalCallModel model, int slots)
	{
		if (model.Form == LuaCallForm.Try)
		{
			WriteTryFailure(writer, model, 0);
			return;
		}

		if (model.Form == LuaCallForm.Outcome)
		{
			WriteOutcomeFailure(writer, model, LuaApiNames.LuaOperationStatus + ".StackUnavailable", 0);
			return;
		}

		writer.Write("throw new ");
		writer.Write(LuaApiNames.LuaException);
		writer.Write('(');
		writer.Write(CSharpLiteral.ToStringLiteral(
			"The Lua stack could not grow by " + slots.ToString(CultureInfo.InvariantCulture) + " slots to call '" +
			model.GlobalName + "'."));
		writer.WriteLine(");");
	}

	private static void WriteSeparator(SourceWriter writer, ref bool first)
	{
		if (!first)
		{
			writer.Write(", ");
		}

		first = false;
	}
}

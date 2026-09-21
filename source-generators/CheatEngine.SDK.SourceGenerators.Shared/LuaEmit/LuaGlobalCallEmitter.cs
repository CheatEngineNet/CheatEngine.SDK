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

    /// <summary>Writes the method, signature and body, at the writer's current indentation.</summary>
    public static void Emit(SourceWriter writer, LuaGlobalCallModel model)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        if (model is null) throw new ArgumentNullException(nameof(model));

        WriteSignature(writer, model);
        writer.OpenBlock();
        WriteBody(writer, model);
        writer.CloseBlock();
    }

    /// <summary>Writes the parameter list, parentheses included: the state, the arguments, then the results.</summary>
    public static void WriteParameterList(SourceWriter writer, LuaGlobalCallModel model)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        if (model is null) throw new ArgumentNullException(nameof(model));

        writer.Write('(');
        var first = true;
        var isExtensionReceiver = model.IsExtensionMethod;
        if (model.TakesState)
        {
            if (isExtensionReceiver) writer.Write("this ");

            writer.Write(LuaApiNames.LuaState);
            writer.Write(' ');
            writer.Write(model.StateParameterName);
            first = false;
            isExtensionReceiver = false;
        }

        for (var i = 0; i < model.Arguments.Length; i++)
        {
            var argument = model.Arguments[i];
            if (argument.IsFixed) continue;

            WriteSeparator(writer, ref first);
            WriteArgumentParameter(writer, argument, isExtensionReceiver);
            isExtensionReceiver = false;
        }

        if (model.Form == LuaCallForm.Try)
            foreach (var result in model.Results)
            {
                WriteSeparator(writer, ref first);
                WriteResultParameter(writer, result, isExtensionReceiver);
                isExtensionReceiver = false;
            }

        writer.Write(')');
    }

    // 'scoped <type> name': the scoped modifier is written first when the declaration used it (Utf8 is the only
    // argument kind of ref struct type; every other kind's IsScoped is always false, see LuaArgumentModel).
    private static void WriteArgumentParameter(SourceWriter writer, LuaArgumentModel argument, bool isExtensionReceiver)
    {
        if (isExtensionReceiver) writer.Write("this ");

        if (argument.IsScoped) writer.Write("scoped ");

        writer.Write(argument.GeneratedTypeName);
        writer.Write(' ');
        writer.Write(argument.Name);
    }

    // 'scoped Span<byte> destination, out int written' or 'out <type> name'.
    private static void WriteResultParameter(SourceWriter writer, LuaResultModel result, bool isExtensionReceiver)
    {
        if (result.Shape == LuaResultShape.CopyOut)
        {
            if (isExtensionReceiver) writer.Write("this ");

            if (result.DestinationIsScoped) writer.Write("scoped ");

            writer.Write(LuaApiNames.SpanOfByte);
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
        if (model is null) throw new ArgumentNullException(nameof(model));

        return model.Form == LuaCallForm.Try
            ? "bool"
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
        var argumentCount = model.Arguments.Length;
        var resultCount = model.ResultCount;

        WriteStateAndTop(writer, model);
        writer.WriteLine("try");
        writer.OpenBlock();

        // Only bodies that would exceed the guaranteed free slots check the stack.
        var slots = Math.Max(1 + argumentCount, resultCount);
        if (slots > StackCheckThreshold) WriteStackCheck(writer, model, slots);

        WriteGlobalPush(writer, model);

        // The arguments.
        foreach (var argument in model.Arguments)
        {
        writer.Write(argument.GeneratedMarshallerTypeName);
            writer.Write(".Push(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(argument.FixedValue ?? argument.Name);
            writer.WriteLine(");");
        }

        if (model.Form == LuaCallForm.Try)
        {
            WriteTryCallAndResults(writer, model, argumentCount, resultCount);
        }
        else
        {
            WriteThrowingCall(writer, argumentCount, resultCount);
            WriteThrowingResult(writer, model);
        }

        writer.CloseBlock();

        if (model.Form == LuaCallForm.Try)
        {
            writer.Write("catch (");
            writer.Write(LuaApiNames.LuaException);
            writer.WriteLine(")");
            writer.OpenBlock();
            WriteTryExceptionFailure(writer, model);
            writer.CloseBlock();
        }

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
        writer.Write(argumentCount.ToString(CultureInfo.InvariantCulture));
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
            for (var i = 0; i < resultCount; i++)
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

    // Exit 2: the call raised; the status travels to the throw helper, which reads the error value.
    private static void WriteThrowingCall(SourceWriter writer, int argumentCount, int resultCount)
    {
        writer.Write(LuaApiNames.LuaStatus);
        writer.Write(' ');
        writer.Write(Status);
        writer.Write(" = ");
        writer.Write(State);
        writer.Write(".TryCall(");
        writer.Write(argumentCount.ToString(CultureInfo.InvariantCulture));
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
            return;

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
        if (result.Shape == LuaResultShape.CopyOut)
        {
            writer.Write(State);
            writer.Write(".TryCopyUtf8(");
            writer.Write(index.ToString(CultureInfo.InvariantCulture));
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
        writer.Write(index.ToString(CultureInfo.InvariantCulture));
        writer.Write(", out ");
        writer.Write(result.Name);
        writer.Write(')');
    }

    // The Try form's cold exit: every result other than 'failing' is defaulted in place, that one goes through
    // LuaCallSupport.Fail, which restores the stack, defaults it and returns false.
    private static void WriteTryFailure(SourceWriter writer, LuaGlobalCallModel model, int failing)
    {
        for (var i = 0; i < model.Results.Length; i++)
        {
            if (i == failing) continue;

            var other = model.Results[i];
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

    // Push and conversion operations can throw managed LuaException after native failures. A Try wrapper keeps its
    // ordinary failure contract for that path; the surrounding finally restores its stack snapshot.
    private static void WriteTryExceptionFailure(SourceWriter writer, LuaGlobalCallModel model)
    {
        foreach (var result in model.Results)
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
        if (!first) writer.Write(", ");

        first = false;
    }
}

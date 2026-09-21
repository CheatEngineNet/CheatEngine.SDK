using System;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     Writes the registration pair of a containing type: <c>RegisterLuaFunctions(LuaState)</c>, which assigns every
///     thunk to its global, and <c>UnregisterLuaFunctions(LuaState)</c>, which assigns <c>nil</c> to each of them.
/// </summary>
/// <remarks>
///     <para>
///         Registration is <c>LuaRuntime.TryPushGeneratedFunction(state, new LuaNativeFunction(&amp;Thunk))</c> (the C
///         closure wrapped by the error-channel closure, at registration time) followed by <c>TrySetGlobal(name)</c>:
///         the protected assignment
///         honours a <c>__newindex</c> on the globals table and never uses Cheat Engine's <c>LuaRegister</c> export. Both
///         return a <c>LuaStatus</c> and follow its protocol, so the pair does too: <see cref="StatusProtocol" />.
///     </para>
///     <para>
///         Each thunk is wrapped in a closure that captures the current attachment epoch and state generation. A script
///         that kept a function value after disable, reset or re-enable receives an ordinary Lua error
///         instead of entering a stale managed registration.
///     </para>
/// </remarks>
[SuppressMessage(
    "Meziantou.Analyzer",
    "MA0182",
    Justification =
        "This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaRegistrationEmitter
{
    /// <summary>Name of the generated registration method.</summary>
    public const string RegisterMethodName = "RegisterLuaFunctions";

    /// <summary>Name of the generated unregistration method.</summary>
    public const string UnregisterMethodName = "UnregisterLuaFunctions";

    /// <summary>The stack contract of both methods, as their documentation states it.</summary>
    public const string StatusProtocol =
        "Stack: +0 on success; +1 (the error value) on failure, as for every protected operation.";

    private const string StateParameter = "state";
    private const string Status = "__status";

    /// <summary>
    ///     Writes both methods, separated by a blank line, at the writer's current indentation. Thunks are registered in
    ///     the order given.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="thunks">The thunks of the containing type; must not be empty.</param>
    /// <param name="memberAttributes">
    ///     Attribute lines to put on each method (for example <c>[GeneratedCode]</c>); may be
    ///     empty.
    /// </param>
    public static void Emit(SourceWriter writer, EquatableArray<LuaThunkModel> thunks, string memberAttributes)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        if (thunks.IsEmpty)
            throw new ArgumentException("A registration table needs at least one thunk.", nameof(thunks));

        WriteRegister(writer, thunks, memberAttributes);
        writer.WriteLine();
        WriteUnregister(writer, thunks, memberAttributes);
    }

    private static void WriteRegister(SourceWriter writer, EquatableArray<LuaThunkModel> thunks,
        string memberAttributes)
    {
        writer.WriteLine("/// <summary>");
        writer.Write(
            "/// Registers every <c>[LuaFunction]</c> of this type as a global of <paramref name=\"state\"/>: ");
        WriteNameList(writer, thunks);
        writer.WriteLine(".");
        writer.Write("/// ");
        writer.WriteLine(StatusProtocol);
        writer.WriteLine("/// </summary>");
        writer.WriteLine(
            "/// <param name=\"state\">The state to register on; the calling thread's. Requires the plugin to be enabled.</param>");
        writer.WriteLine("/// <returns>The status of the first failing operation, or <c>LuaStatus.Ok</c>.</returns>");
        WriteAttributes(writer, memberAttributes);
        WriteMethodOpening(writer, "public static unsafe ", RegisterMethodName);
        foreach (var thunk in thunks) WriteRegistration(writer, thunk);

        WriteMethodClosing(writer);
    }

    private static void WriteUnregister(SourceWriter writer, EquatableArray<LuaThunkModel> thunks,
        string memberAttributes)
    {
        writer.WriteLine("/// <summary>");
        writer.Write("/// Assigns <c>nil</c> to every global that <see cref=\"");
        writer.Write(RegisterMethodName);
        writer.Write("\"/> registers: ");
        WriteNameList(writer, thunks);
        writer.WriteLine(".");
        writer.Write("/// ");
        writer.WriteLine(StatusProtocol);
        writer.WriteLine("/// </summary>");
        writer.WriteLine("/// <param name=\"state\">The state to unregister from; the calling thread's.</param>");
        writer.WriteLine("/// <returns>The status of the first failing assignment, or <c>LuaStatus.Ok</c>.</returns>");
        WriteAttributes(writer, memberAttributes);
        WriteMethodOpening(writer, "public static ", UnregisterMethodName);
        foreach (var thunk in thunks)
        {
            writer.Write(StateParameter);
            writer.WriteLine(".PushNil();");
            WriteSetGlobal(writer, thunk);
        }

        WriteMethodClosing(writer);
    }

    // '<modifiers> LuaStatus <name>(LuaState state) {' and the status local.
    private static void WriteMethodOpening(SourceWriter writer, string modifiers, string name)
    {
        writer.Write(modifiers);
        writer.Write(LuaApiNames.LuaStatus);
        writer.Write(' ');
        writer.Write(name);
        writer.Write('(');
        writer.Write(LuaApiNames.LuaState);
        writer.Write(' ');
        writer.Write(StateParameter);
        writer.WriteLine(")");
        writer.OpenBlock();
        writer.Write(LuaApiNames.LuaStatus);
        writer.Write(' ');
        writer.Write(Status);
        writer.WriteLine(";");
    }

    private static void WriteMethodClosing(SourceWriter writer)
    {
        writer.Write("return ");
        writer.Write(LuaApiNames.LuaStatus);
        writer.WriteLine(".Ok;");
        writer.CloseBlock();
    }

    // Push the wrapped closure, then assign it to the global; each step is a protected operation with a status.
    private static void WriteRegistration(SourceWriter writer, LuaThunkModel thunk)
    {
        writer.Write(Status);
        writer.Write(" = ");
        writer.Write(LuaApiNames.LuaRuntime);
        writer.Write(".TryPushGeneratedFunction(");
        writer.Write(StateParameter);
        writer.Write(", new ");
        writer.Write(LuaApiNames.LuaNativeFunction);
        writer.Write("(&");
        writer.Write(thunk.ThunkMethodName);
        writer.WriteLine("));");
        WriteStatusCheck(writer);
        WriteSetGlobal(writer, thunk);
    }

    private static void WriteSetGlobal(SourceWriter writer, LuaThunkModel thunk)
    {
        writer.Write(Status);
        writer.Write(" = ");
        writer.Write(StateParameter);
        writer.Write(".TrySetGlobal(");
        writer.Write(CSharpLiteral.ToUtf8Literal(thunk.LuaName));
        writer.WriteLine(");");
        WriteStatusCheck(writer);
    }

    private static void WriteStatusCheck(SourceWriter writer)
    {
        writer.Write("if (!");
        writer.Write(Status);
        writer.WriteLine(".IsOk)");
        writer.OpenBlock();
        writer.Write("return ");
        writer.Write(Status);
        writer.WriteLine(";");
        writer.CloseBlock();
        writer.WriteLine();
    }

    private static void WriteAttributes(SourceWriter writer, string memberAttributes)
    {
        if (!string.IsNullOrEmpty(memberAttributes)) writer.WriteLine(memberAttributes);
    }

    // The names in a documentation comment: Lua names are identifiers, so no XML escaping is needed.
    private static void WriteNameList(SourceWriter writer, EquatableArray<LuaThunkModel> thunks)
    {
        var first = true;
        foreach (var thunk in thunks)
        {
            if (!first) writer.Write(", ");

            first = false;
            writer.Write("<c>");
            writer.Write(thunk.LuaName);
            writer.Write("</c>");
        }
    }
}

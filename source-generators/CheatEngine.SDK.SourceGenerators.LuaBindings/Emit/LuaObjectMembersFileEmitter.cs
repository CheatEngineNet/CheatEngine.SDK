using System;
using System.Globalization;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;

/// <summary>
///     Emits protected object-method and property bodies. Every method snapshots and restores the Lua stack in a
///     <c>finally</c>; properties delegate to <c>CEObject</c>'s already-protected typed primitives.
/// </summary>
internal static class LuaObjectMembersFileEmitter
{
    private const string State = "__ceState";
    private const string Operation = "__ceOperation";
    private const string Top = "__ceTop";
    private const string Status = "__ceStatus";
    private const string Result = "__ceResult";
    private const int StackCheckThreshold = LuaGlobalCallEmitter.StackCheckThreshold;

    private static readonly string GeneratedCodeAttribute =
        GeneratedCodeText.CreateGeneratedCodeAttribute(typeof(LuaObjectMembersFileEmitter));

    /// <summary>Returns the table's deterministic source hint name.</summary>
    public static string HintName(LuaObjectMembersTableModel table)
    {
        return table.HintName;
    }

    /// <summary>Writes one generated partial type file.</summary>
    public static SourceText Emit(LuaObjectMembersTableModel table)
    {
        SourceWriter writer = new(4096);
        GeneratedCodeText.WriteFileHeader(writer);
        TypeScaffoldEmitter.Open(writer, table.ContainingType);

        foreach (var method in table.Methods)
        {
            writer.WriteLine(GeneratedCodeAttribute);
            EmitMethod(writer, method);
            writer.WriteLine();
        }

        foreach (var property in table.Properties)
        {
            writer.WriteLine(GeneratedCodeAttribute);
            EmitProperty(writer, property);
            writer.WriteLine();
        }

        TypeScaffoldEmitter.Close(writer, table.ContainingType);
        return writer.ToSourceText();
    }

    private static void EmitMethod(SourceWriter writer, LuaObjectMethodModel model)
    {
        WriteMethodDeclaration(writer, model);
        writer.OpenBlock();
        WriteOperationSetup(writer);
        writer.WriteLine("try");
        writer.OpenBlock();
        WriteMethodCall(writer, model);
        writer.CloseBlock();
        WriteTryExceptionCatch(writer, model);
        WriteStackRestore(writer);
        writer.CloseBlock();
    }

    private static void WriteMethodDeclaration(SourceWriter writer, LuaObjectMethodModel model)
    {
        writer.Write(model.Modifiers);
        writer.Write(' ');
        writer.Write(ReturnTypeName(model));
        writer.Write(' ');
        writer.Write(model.MethodName);
        WriteMethodParameters(writer, model);
        writer.WriteLine();
    }

    private static void WriteOperationSetup(SourceWriter writer)
    {
        writer.Write("using ");
        writer.Write(LuaApiNames.LuaRuntimeOperation);
        writer.Write(' ');
        writer.Write(Operation);
        writer.Write(" = ");
        writer.Write(LuaApiNames.AcquireOperation);
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

    private static void WriteTryExceptionCatch(SourceWriter writer, LuaObjectMethodModel model)
    {
        if (model.Form != LuaCallForm.Try) return;

        writer.Write("catch (");
        writer.Write(LuaApiNames.LuaException);
        writer.WriteLine(")");
        writer.OpenBlock();
        foreach (var result in model.Results) WriteDefaultResult(writer, result);

        writer.WriteLine("return false;");
        writer.CloseBlock();
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

    private static void WriteMethodCall(SourceWriter writer, LuaObjectMethodModel model)
    {
        var requiredSlots = Math.Max(2 + model.Arguments.Length, 1 + model.ResultCount);
        if (requiredSlots > StackCheckThreshold) WriteStackCheck(writer, model, requiredSlots);

        writer.Write(LuaApiNames.LuaStatus);
        writer.Write(' ');
        writer.Write(Status);
        writer.Write(" = Handle.TryPushMethodLeavingObject(");
        writer.Write(State);
        writer.Write(", ");
        writer.Write(CSharpLiteral.ToUtf8Literal(model.LuaName));
        writer.WriteLine(");");
        WriteStatusExit(writer, model);

        foreach (var argument in model.Arguments)
        {
            writer.Write(LuaValueKinds.MarshallerTypeName(argument.Kind));
            writer.Write(".Push(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(argument.Name);
            writer.WriteLine(");");
        }

        writer.Write(Status);
        writer.Write(" = ");
        writer.Write(State);
        writer.Write(".TryCall(");
        writer.Write(model.Arguments.Length.ToString(CultureInfo.InvariantCulture));
        writer.Write(", ");
        writer.Write(model.ResultCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine(");");
        WriteStatusExit(writer, model);

        if (model.Form == LuaCallForm.Try)
            WriteTryResults(writer, model);
        else
            WriteThrowingResult(writer, model);

    }

    private static void WriteStackCheck(SourceWriter writer, LuaObjectMethodModel model, int requiredSlots)
    {
        writer.Write("if (!");
        writer.Write(State);
        writer.Write(".TryEnsureStack(");
        writer.Write(requiredSlots.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("))");
        writer.OpenBlock();
        if (model.Form == LuaCallForm.Try)
        {
            for (var i = 1; i < model.Results.Length; i++) WriteDefaultResult(writer, model.Results[i]);

            writer.Write("return ");
            writer.Write(LuaApiNames.LuaCallSupport);
            writer.Write(".Fail(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(Top);
            writer.Write(", out ");
            writer.Write(model.Results[0].Name);
            writer.WriteLine(");");
        }
        else
        {
            writer.Write("throw new ");
            writer.Write(LuaApiNames.LuaException);
            writer.Write('(');
            writer.Write(CSharpLiteral.ToStringLiteral(
                "The Lua stack could not grow by " + requiredSlots.ToString(CultureInfo.InvariantCulture)
                + " slots to call '" + model.LuaName + "'."));
            writer.WriteLine(");");
        }

        writer.CloseBlock();
        writer.WriteLine();
    }

    private static void WriteStatusExit(SourceWriter writer, LuaObjectMethodModel model)
    {
        writer.Write("if (!");
        writer.Write(Status);
        writer.WriteLine(".IsOk)");
        writer.OpenBlock();
        if (model.Form == LuaCallForm.Try)
        {
            for (var i = 1; i < model.Results.Length; i++) WriteDefaultResult(writer, model.Results[i]);

            writer.Write("return ");
            writer.Write(LuaApiNames.LuaCallSupport);
            writer.Write(".Fail(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(Top);
            writer.Write(", out ");
            writer.Write(model.Results[0].Name);
            writer.WriteLine(");");
        }
        else
        {
            writer.Write(LuaApiNames.LuaCallSupport);
            writer.Write(".Throw(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(Top);
            writer.Write(", ");
            writer.Write(Status);
            writer.WriteLine(");");
        }

        writer.CloseBlock();
    }

    private static void WriteTryResults(SourceWriter writer, LuaObjectMethodModel model)
    {
        for (var i = 0; i < model.Results.Length; i++)
        {
            var result = model.Results[i];
            writer.Write("if (!");
            writer.Write(LuaValueKinds.MarshallerTypeName(result.Kind));
            writer.Write(".TryRead(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write((i - model.Results.Length).ToString(CultureInfo.InvariantCulture));
            writer.Write(", out ");
            writer.Write(result.Name);
            writer.WriteLine("))");
            writer.OpenBlock();
            for (var other = 0; other < model.Results.Length; other++)
                if (other != i)
                    WriteDefaultResult(writer, model.Results[other]);

            writer.Write("return ");
            writer.Write(LuaApiNames.LuaCallSupport);
            writer.Write(".Fail(");
            writer.Write(State);
            writer.Write(", ");
            writer.Write(Top);
            writer.Write(", out ");
            writer.Write(result.Name);
            writer.WriteLine(");");
            writer.CloseBlock();
        }

        writer.WriteLine("return true;");
    }

    private static void WriteThrowingResult(SourceWriter writer, LuaObjectMethodModel model)
    {
        if (model.ReturnKind is not LuaValueKind kind)
        {
            writer.WriteLine("return;");
            return;
        }

        writer.Write("if (!");
        writer.Write(LuaValueKinds.MarshallerTypeName(kind));
        writer.Write(".TryRead(");
        writer.Write(State);
        writer.Write(", -1, out ");
        writer.Write(LuaValueKinds.TypeName(kind, true));
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
        writer.Write(CSharpLiteral.ToStringLiteral(model.LuaName));
        writer.Write(", ");
        writer.Write(CSharpLiteral.ToStringLiteral(LuaValueKinds.ExpectedResult(kind)));
        writer.WriteLine(");");
        writer.CloseBlock();
        writer.Write("return ");
        writer.Write(Result);
        writer.WriteLine(";");
    }

    private static void WriteMethodParameters(SourceWriter writer, LuaObjectMethodModel model)
    {
        writer.Write('(');
        var first = true;
        foreach (var argument in model.Arguments)
        {
            if (!first) writer.Write(", ");

            first = false;
            if (argument.IsScoped) writer.Write("scoped ");

            writer.Write(LuaValueKinds.TypeName(argument.Kind, argument.IsNullable));
            writer.Write(' ');
            writer.Write(argument.Name);
        }

        if (model.Form == LuaCallForm.Try)
            foreach (var result in model.Results)
            {
                if (!first) writer.Write(", ");

                first = false;
                writer.Write("out ");
                writer.Write(LuaValueKinds.TypeName(result.Kind, result.IsNullable));
                writer.Write(' ');
                writer.Write(result.Name);
            }

        writer.Write(')');
    }

    private static string ReturnTypeName(LuaObjectMethodModel model)
    {
        return model.Form == LuaCallForm.Try
            ? "bool"
            : model.ReturnKind is LuaValueKind kind
                ? LuaValueKinds.TypeName(kind, model.ReturnIsNullable)
                : "void";
    }

    private static void WriteDefaultResult(SourceWriter writer, LuaResultModel result)
    {
        writer.Write(result.Name);
        writer.WriteLine(LuaValueKinds.IsReferenceType(result.Kind) ? " = default!;" : " = default;");
    }

    private static void EmitProperty(SourceWriter writer, LuaObjectPropertyModel model)
    {
        writer.Write(model.Modifiers);
        writer.Write(' ');
        writer.Write(LuaValueKinds.TypeName(model.Kind, model.IsNullable));
        writer.Write(' ');
        writer.Write(model.PropertyName);
        writer.WriteLine();
        writer.OpenBlock();

        if (model.HasGetter) EmitGetter(writer, model);

        if (model.HasSetter) EmitSetter(writer, model);

        writer.CloseBlock();
    }

    private static void EmitGetter(SourceWriter writer, LuaObjectPropertyModel model)
    {
        WriteAccessorHeader(writer, model.GetterModifiers, "get");
        writer.OpenBlock();
        writer.Write("if (!Handle.TryGetProperty<");
        writer.Write(LuaValueKinds.MarshallerTypeName(model.Kind));
        writer.Write(", ");
        writer.Write(LuaValueKinds.TypeName(model.Kind, model.IsNullable));
        writer.Write(">(");
        writer.Write(CSharpLiteral.ToUtf8Literal(model.LuaName));
        writer.Write(", out ");
        writer.Write(LuaValueKinds.TypeName(model.Kind, true));
        writer.WriteLine(" __ceValue))");
        writer.OpenBlock();
        WritePropertyFailure(writer, model, "read");
        writer.CloseBlock();
        writer.WriteLine("return __ceValue;");
        writer.CloseBlock();
    }

    private static void EmitSetter(SourceWriter writer, LuaObjectPropertyModel model)
    {
        WriteAccessorHeader(writer, model.SetterModifiers, "set");
        writer.OpenBlock();
        writer.Write("if (!Handle.TrySetProperty<");
        writer.Write(LuaValueKinds.MarshallerTypeName(model.Kind));
        writer.Write(", ");
        writer.Write(LuaValueKinds.TypeName(model.Kind, model.IsNullable));
        writer.Write(">(");
        writer.Write(CSharpLiteral.ToUtf8Literal(model.LuaName));
        writer.WriteLine(", value))");
        writer.OpenBlock();
        WritePropertyFailure(writer, model, "write");
        writer.CloseBlock();
        writer.CloseBlock();
    }

    private static void WriteAccessorHeader(SourceWriter writer, string modifiers, string keyword)
    {
        if (modifiers.Length > 0)
        {
            writer.Write(modifiers);
            writer.Write(' ');
        }

        writer.WriteLine(keyword);
    }

    private static void WritePropertyFailure(SourceWriter writer, LuaObjectPropertyModel model, string operation)
    {
        writer.Write("throw new ");
        writer.Write(LuaApiNames.LuaException);
        writer.Write('(');
        writer.Write(CSharpLiteral.ToStringLiteral(
            "The Cheat Engine object property '" + model.LuaName + "' could not be " + operation + "."));
        writer.WriteLine(");");
    }
}

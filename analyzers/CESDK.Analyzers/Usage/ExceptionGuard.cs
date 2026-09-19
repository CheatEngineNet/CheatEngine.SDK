using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace CESDK.Analyzers.Usage;

/// <summary>
///     The structural definition behind CESDK1004: is a method body entirely guarded by a catch-all?
/// </summary>
/// <remarks>
///     <para>A body is guarded when every top-level statement is one of:</para>
///     <list type="bullet">
///         <item>
///             a <b>guard try</b>: a <c>try</c> statement with a clause <c>catch</c> or <c>catch (System.Exception)</c>
///             that has no <c>when</c> filter, and in which no <c>catch</c> block and no <c>finally</c> block contains a
///             <c>throw</c> (statement or expression, rethrow included, anywhere in the block) or a call of a method
///             marked
///             <c>[DoesNotReturn]</c> (<c>ExceptionDispatchInfo.Throw</c>, throw helpers), the members of
///             <c>System.Environment</c> excepted: <c>FailFast</c> and <c>Exit</c> end the process, nothing unwinds;
///         </item>
///         <item>a local declaration without initializer, or whose initializers are trivially non-throwing;</item>
///         <item><c>return;</c> or a <c>return</c> of a trivially non-throwing value;</item>
///         <item>a local function declaration (declaring it runs nothing);</item>
///         <item>an empty statement;</item>
///         <item>
///             a nested block whose statements are all of the above (<c>unsafe</c>, <c>checked</c> and
///             <c>unchecked</c> blocks are plain blocks in <c>IOperation</c> terms and count).
///         </item>
///     </list>
///     <para>
///         Trivially non-throwing values: compile-time constants, <c>default</c>, a local, a parameter, a static field of
///         a
///         core-library primitive (<c>IntPtr.Zero</c>, <c>string.Empty</c>), and, over such values: a built-in conversion
///         from the closed list in <see cref="IsNonThrowingConversion" />, an unchecked built-in unary operator on a
///         primitive or an enum, and a conditional expression. The list is a whitelist: a conversion that runs code
///         (user-defined, <c>dynamic</c>, tuple element conversions, span conversions), allocates (boxing) or can fail
///         (unboxing, casts between reference types, <c>T?</c> to <c>T</c>, anything <c>checked</c>, anything involving
///         <c>decimal</c>) is not on it. An expression body is treated as the block <c>{ return expression; }</c> (or
///         <c>{ expression; }</c> for <c>void</c>), so <c>=> 0</c> passes and <c>=> Work()</c> does not.
///     </para>
///     <para>
///         The definition is deliberately syntactic in spirit: it does not prove that the calls inside a catch or finally
///         block cannot throw (only the explicit ways of throwing listed above are found), and it does not accept a guard
///         hidden behind <c>using</c>, <c>lock</c> or <c>fixed</c>. Predictable beats clever here: the accepted shape is
///         exactly what the generators emit and what the code fix produces.
///     </para>
///     <para>One instance per compilation, created in the compilation-start action; immutable, safe for concurrent use.</para>
/// </remarks>
/// <param name="exceptionType">The resolved <c>System.Exception</c>.</param>
/// <param name="doesNotReturnAttribute">
///     The resolved <c>System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute</c>, or <see langword="null" /> when the
///     compilation has none (or several): calls are then not inspected.
/// </param>
/// <param name="environmentType">The resolved <c>System.Environment</c>, or <see langword="null" />.</param>
internal sealed class ExceptionGuard(
    INamedTypeSymbol exceptionType,
    INamedTypeSymbol? doesNotReturnAttribute,
    INamedTypeSymbol? environmentType)
{
    /// <summary>Whether every statement of <paramref name="body" /> is safe in the sense of the type remarks.</summary>
    /// <param name="body">A block body, or the implicit block of an expression body.</param>
    public bool IsGuarded(IBlockOperation body)
    {
        foreach (var statement in body.Operations)
            if (!IsSafeStatement(statement))
                return false;

        return true;
    }

    private bool IsSafeStatement(IOperation statement)
    {
        return statement switch
        {
            ITryOperation tryOperation => IsGuardTry(tryOperation),
            IVariableDeclarationGroupOperation declarations => AreTrivialDeclarations(declarations),
            IReturnOperation { Kind: OperationKind.Return } returnOperation =>
                returnOperation.ReturnedValue is null || IsTriviallyNonThrowing(returnOperation.ReturnedValue),
            ILocalFunctionOperation => true,
            IEmptyOperation => true,
            IBlockOperation block => IsGuarded(block),
            _ => false
        };
    }

    private bool IsGuardTry(ITryOperation tryOperation)
    {
        var hasCatchAll = false;
        foreach (var catchClause in tryOperation.Catches)
        {
            // A rethrow in ANY clause leaves the try statement: sibling clauses do not catch it.
            if (ContainsThrow(catchClause.Handler)) return false;

            hasCatchAll |= IsCatchAll(catchClause);
        }

        return hasCatchAll && (tryOperation.Finally is null || !ContainsThrow(tryOperation.Finally));
    }

    // 'catch { }' has the exception type System.Object; 'catch (Exception)' names the root of the hierarchy.
    private bool IsCatchAll(ICatchClauseOperation catchClause)
    {
        return catchClause.Filter is null
               && (catchClause.ExceptionType.SpecialType == SpecialType.System_Object
                   || SymbolEqualityComparer.Default.Equals(catchClause.ExceptionType, exceptionType));
    }

    private bool ContainsThrow(IOperation block)
    {
        foreach (var descendant in block.Descendants())
            if (descendant.Kind == OperationKind.Throw
                || (descendant is IInvocationOperation invocation && NeverReturnsByThrowing(invocation.TargetMethod)))
                return true;

        return false;
    }

    // [DoesNotReturn] is how a method says "I always throw": ExceptionDispatchInfo.Throw (the rethrow idiom that
    // keeps the stack trace) and every throw helper carry it. Environment.FailFast and Environment.Exit carry it
    // too, but they end the process in a controlled way; nothing unwinds into native code.
    private bool NeverReturnsByThrowing(IMethodSymbol method)
    {
        if (doesNotReturnAttribute is null
            || SymbolEqualityComparer.Default.Equals(method.ContainingType, environmentType))
            return false;

        foreach (var attribute in method.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, doesNotReturnAttribute))
                return true;

        return false;
    }

    private static bool AreTrivialDeclarations(IVariableDeclarationGroupOperation group)
    {
        foreach (var declaration in group.Declarations)
        foreach (var declarator in declaration.Declarators)
            if (declarator.Initializer is { } initializer && !IsTriviallyNonThrowing(initializer.Value))
                return false;

        return true;
    }

    private static bool IsTriviallyNonThrowing(IOperation value)
    {
        if (value.ConstantValue.HasValue) return true;

        return value switch
        {
            IDefaultValueOperation or ILocalReferenceOperation or IParameterReferenceOperation => true,

            // Reading a static field can run a type initializer, which can throw. The core-library primitives are
            // the exception: 'IntPtr.Zero', 'UIntPtr.Zero', 'string.Empty'.
            IFieldReferenceOperation
            {
                Field: { IsStatic: true, ContainingType.SpecialType: not SpecialType.None }
            } => true,

            IConversionOperation conversion => IsNonThrowingConversion(conversion) &&
                                               IsTriviallyNonThrowing(conversion.Operand),

            // '-x', '+x', '~x', '!x' outside a checked context. 'dynamic' and 'decimal' operands run code, and so does
            // the fifth built-in unary operator: '^x' constructs a System.Index, which rejects negative values.
            IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Minus or UnaryOperatorKind.Plus or UnaryOperatorKind.BitwiseNegation
                or UnaryOperatorKind.Not,
                OperatorMethod: null,
                IsChecked: false
            } unary => IsPrimitiveOrEnum(unary.Operand.Type) && IsTriviallyNonThrowing(unary.Operand),

            IConditionalOperation { IsRef: false, WhenFalse: { } whenFalse } conditional =>
                IsTriviallyNonThrowing(conditional.Condition)
                && IsTriviallyNonThrowing(conditional.WhenTrue)
                && IsTriviallyNonThrowing(whenFalse),

            _ => false
        };
    }

    // A closed list of built-in conversions that run no user code, allocate nothing and cannot fail:
    // identity and the typing of a 'default' or 'null' literal; implicit numeric, reference and pointer
    // conversions; wrapping into (or widening between) nullable primitives and wrapping a value into its own
    // nullable type; and, outside a checked context, explicit conversions among primitives, enums and pointers
    // ('(int)wide', '(int)status', '(nint)pointer').
    private static bool IsNonThrowingConversion(IConversionOperation operation)
    {
        var conversion = operation.GetConversion();
        if (!conversion.Exists || conversion.IsUserDefined || conversion.MethodSymbol is not null ||
            conversion.IsDynamic) return false;

        // 'Guid id = default;' is a default-literal conversion around the default value, 'int? none = null;' a
        // null-literal conversion: both only give the literal its type.
        if (conversion.IsIdentity || conversion.IsDefaultLiteral || conversion.IsNullLiteral) return true;

        var source = operation.Operand.Type;
        var target = operation.Type;
        if (conversion.IsImplicit)
            return conversion.IsNumeric
                   || conversion.IsReference
                   || conversion.IsPointer
                   || (conversion.IsNullable && IsNullableWrapping(source, target));

        return !operation.IsChecked
               && (conversion.IsNumeric || conversion.IsEnumeration || conversion.IsPointer)
               && IsPrimitiveEnumOrPointer(source)
               && IsPrimitiveEnumOrPointer(target);
    }

    // 'int -> int?', 'int -> long?', 'int? -> long?', 'Guid -> Guid?'. An implicit nullable conversion can also
    // carry any other implicit conversion of the underlying types (tuple element conversions with user-defined
    // operators among them), and Roslyn does not expose which: only the two harmless cases are accepted.
    private static bool IsNullableWrapping(ITypeSymbol? source, ITypeSymbol? target)
    {
        var from = UnwrapNullable(source);
        var to = UnwrapNullable(target);
        return from is not null
               && to is not null
               && (SymbolEqualityComparer.Default.Equals(from, to) ||
                   (IsPrimitiveOrEnum(from) && IsPrimitiveOrEnum(to)));
    }

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;
    }

    private static bool IsPrimitiveEnumOrPointer(ITypeSymbol? type)
    {
        return type is { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer } || IsPrimitiveOrEnum(type);
    }

    // 'decimal' is deliberately absent: its operators and conversions are methods of System.Decimal that throw
    // OverflowException whatever the checked context.
    private static bool IsPrimitiveOrEnum(ITypeSymbol? type)
    {
        return type is { TypeKind: TypeKind.Enum }
               || type?.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
                   or SpecialType.System_SByte or SpecialType.System_Byte
                   or SpecialType.System_Int16 or SpecialType.System_UInt16
                   or SpecialType.System_Int32 or SpecialType.System_UInt32
                   or SpecialType.System_Int64 or SpecialType.System_UInt64
                   or SpecialType.System_IntPtr or SpecialType.System_UIntPtr
                   or SpecialType.System_Single or SpecialType.System_Double;
    }
}

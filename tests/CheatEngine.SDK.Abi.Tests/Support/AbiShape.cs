using System.Reflection;
using System.Runtime.CompilerServices;

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     The reflection gate behind <see cref="AssemblyConformanceTests" />: decides whether a structure may be part of
///     the ABI mapping. It looks <b>through</b> everything a field can hide a forbidden type behind: a nested
///     structure, a pointer, and the result and parameters of a function pointer.
/// </summary>
/// <remarks>
///     <para>Rules, applied at any depth:</para>
///     <list type="bullet">
///         <item>structures use sequential layout (never <c>Auto</c> or <c>Explicit</c>) and belong to a trusted assembly;</item>
///         <item>
///             scalars are fixed-width or pointer-sized primitives, never <see langword="bool" /> or
///             <see langword="char" />
///             (with runtime marshalling disabled they cross the boundary as raw 1-byte and 2-byte values, which no field
///             or
///             argument of this interface is);
///         </item>
///         <item>enumerations are 4 bytes wide;</item>
///         <item>
///             function pointers are unmanaged and state exactly one convention, <c>Stdcall</c>; nothing travels by
///             reference (<see langword="ref" />/<see langword="in" />/<see langword="out" />), pointers are used instead;
///         </item>
///         <item><see langword="void" /> only as a result or as a pointee.</item>
///     </list>
///     <para>
///         Neither the compiler nor the interop analyzers (CA1420/CA1421) enforce any of this for function-pointer
///         types: <c>delegate* unmanaged[Cdecl]&lt;bool, char&gt;</c> builds without a diagnostic under
///         <c>[assembly: DisableRuntimeMarshalling]</c>. This gate is the enforcement; <see cref="AbiShapeTests" /> proves
///         each rule against a deliberately wrong fixture.
///     </para>
/// </remarks>
internal static class AbiShape
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Returns the first rule <paramref name="structure" /> breaks, or <see langword="null" />.</summary>
    /// <param name="structure">The structure to inspect.</param>
    /// <param name="trustedAssemblies">
    ///     Assemblies whose structures may appear by value or behind a pointer. Every other non-primitive value type is
    ///     foreign: the mapping references nothing, so such a type is spelled <c>void*</c> instead.
    /// </param>
    public static string? FindViolation(Type structure, params ReadOnlySpan<Assembly> trustedAssemblies)
    {
        return CheckStructure(structure, structure.Name, trustedAssemblies, []);
    }

    private static string? CheckStructure(Type structure, string path, ReadOnlySpan<Assembly> trusted,
        HashSet<Type> visited)
    {
        // Already being inspected further up (a pointer cycle) or already found clean: nothing new to learn.
        if (!visited.Add(structure)) return null;

        if (!structure.IsLayoutSequential)
            return $"{path}: {structure.Name} must use sequential layout, not Auto or Explicit.";

        foreach (var field in structure.GetFields(InstanceFields))
        {
            // The MODIFIED type: the only reflection view that still carries the calling convention of a function pointer.
            var violation = CheckType(field.GetModifiedFieldType(), $"{path}.{field.Name}", false, trusted, visited);
            if (violation is not null) return violation;
        }

        return null;
    }

    private static string? CheckType(Type type, string path, bool allowVoid, ReadOnlySpan<Assembly> trusted,
        HashSet<Type> visited)
    {
        // Identity and classification questions go to the unmodified type; navigation (element type, signature) stays
        // on the modified one so that nested function pointers keep their conventions.
        var plain = type.UnderlyingSystemType;

        if (plain == typeof(void)) return allowVoid ? null : $"{path}: void is only valid as a result or as a pointee.";

        if (plain.IsFunctionPointer) return CheckFunctionPointer(type, path, trusted, visited);

        if (plain.IsPointer)
        {
            var element = type.GetElementType();
            return element is null
                ? $"{path}: pointer without an element type."
                : CheckType(element, path + "*", true, trusted, visited);
        }

        if (plain.IsByRef) return $"{path}: passed by reference (ref/in/out); the ABI uses pointers.";

        if (plain.IsEnum)
            return Enum.GetUnderlyingType(plain) == typeof(int)
                ? null
                : $"{path}: enumeration {plain.Name} is not 4 bytes wide.";

        if (plain.IsPrimitive)
            return plain == typeof(bool) || plain == typeof(char)
                ? $"{path}: {plain.Name} has no fixed ABI width here; use Bool32/Bool8 or a fixed-width integer."
                : null;

        if (plain.IsValueType && !plain.IsGenericType && trusted.Contains(plain.Assembly))
            return CheckStructure(plain, path, trusted, visited);

        return $"{path}: {plain} is a reference, a generic or a foreign value type: not allowed in an ABI layout.";
    }

    private static string? CheckFunctionPointer(Type type, string path, ReadOnlySpan<Assembly> trusted,
        HashSet<Type> visited)
    {
        if (!type.UnderlyingSystemType.IsUnmanagedFunctionPointer)
            return $"{path}: managed function pointer; the host can only call unmanaged ones.";

        var conventions = type.GetFunctionPointerCallingConventions();
        if (conventions.Length != 1 || conventions[0] != typeof(CallConvStdcall))
        {
            var stated = conventions.Length == 0
                ? "none"
                : string.Join(", ", conventions.Select(static convention => convention.Name));
            return $"{path}: calling convention must be exactly Stdcall (stated: {stated}).";
        }

        var violation = CheckType(type.GetFunctionPointerReturnType(), path + "(result)", true, trusted, visited);
        if (violation is not null) return violation;

        var parameters = type.GetFunctionPointerParameterTypes();
        for (var index = 0; index < parameters.Length; index++)
        {
            violation = CheckType(parameters[index], $"{path}(parameter {index})", false, trusted, visited);
            if (violation is not null) return violation;
        }

        return null;
    }
}

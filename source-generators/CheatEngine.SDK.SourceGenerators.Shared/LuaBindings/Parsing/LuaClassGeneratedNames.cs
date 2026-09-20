using System;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>Names reserved by the generated borrowed-handle identity surface.</summary>
internal static class LuaClassGeneratedNames
{
    /// <summary>Whether <paramref name="name" /> is emitted as a generated borrowed-handle member.</summary>
    public static bool IsGeneratedMember(string name)
    {
        return name is "_handle" or "Handle" or "FromHandle" or "Equals" or "GetHashCode" or "Push" or "TryRead";
    }

    /// <summary>Whether <paramref name="name" /> cannot contain the generated borrowed-handle surface.</summary>
    public static bool IsGeneratedType(string name)
    {
        return IsGeneratedMember(name);
    }

    /// <summary>Whether an authored ordinary method would collide with the generated <c>Handle</c> property.</summary>
    public static bool IsGeneratedAccessorCollision(IMethodSymbol method, INamedTypeSymbol? ceObject)
    {
        return method.MethodKind == MethodKind.Ordinary
               && ((string.Equals(method.Name, "get_Handle", StringComparison.Ordinal)
                    && method.Parameters.Length == 0)
                   || (string.Equals(method.Name, "set_Handle", StringComparison.Ordinal)
                       && ceObject is not null
                       && method.Parameters.Length == 1
                       && method.Parameters[0].RefKind == RefKind.None
                       && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, ceObject)));
    }
}

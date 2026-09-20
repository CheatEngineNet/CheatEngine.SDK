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
}

namespace CESDK.SourceGenerators.Shared;

/// <summary>
///     The host-mandated identity of the managed bootstrap that <c>CESDK.SourceGenerators.EntryPoint</c> emits:
///     the namespace and simple name of the <c>CESDK.CESDK</c> type, and the name of its
///     <c>CEPluginInitialize</c> method, exactly as Cheat Engine's hostfxr call looks them up.
/// </summary>
/// <remarks>
///     Restates <c>CESDK.Abi.Managed.ManagedEntryPoint</c> (<see cref="Namespace" />/<see cref="TypeName" /> ↔
///     <c>ManagedEntryPoint.Namespace</c>/<c>TypeName</c>; <see cref="MethodName" /> ↔
///     <c>ManagedEntryPoint.MethodName</c>). No Roslyn component here can reference <c>CESDK.Abi</c> (it targets
///     <c>net10.0</c>; this project is <c>netstandard2.0</c>), so the identity is redeclared here rather than
///     re-spelled ad hoc at each use site, for the same reason and with the same pattern as
///     <see cref="AnnotationsMetadataNames" />. A dotnet test, which can reference both assemblies, keeps the two
///     copies from drifting (<c>ManagedEntryPointNames_matches_CESDK_Abi_ManagedEntryPoint</c> in
///     <c>tests/CESDK.SourceGenerators.EntryPoint.Tests</c>).
/// </remarks>
internal static class ManagedEntryPointNames
{
    /// <summary>Namespace of the bootstrap type, as written into the emitted <c>namespace</c> declaration.</summary>
    public const string Namespace = "CESDK";

    /// <summary>Simple name of the bootstrap type, as written into the emitted <c>class</c> declaration.</summary>
    public const string TypeName = "CESDK";

    /// <summary>Name of the public static bootstrap method the host calls.</summary>
    public const string MethodName = "CEPluginInitialize";
}

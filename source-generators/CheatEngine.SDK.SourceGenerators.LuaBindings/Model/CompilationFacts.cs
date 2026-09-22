using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     The one fact about the compilation the pipeline needs, reduced to a value before it is combined with anything
///     (the <c>Compilation</c> itself is never equal to its predecessor and must not be cached).
/// </summary>
/// <param name="AllowUnsafeBlocks">
///     Whether the consumer compiles with <c>AllowUnsafeBlocks</c>. The registration table takes the address of the
///     <c>[UnmanagedCallersOnly]</c> thunks, which needs it; when it is off the generator emits nothing at all and
///     analyzer rule CESDK2001 tells the author.
/// </param>
internal readonly record struct CompilationFacts(bool AllowUnsafeBlocks)
{
	/// <summary>Reads the facts from <paramref name="compilation" />; a non-C# compilation reads as "unsafe not allowed".</summary>
	public static CompilationFacts From(Compilation compilation)
	{
		return new CompilationFacts(compilation?.Options is CSharpCompilationOptions { AllowUnsafe: true });
	}
}

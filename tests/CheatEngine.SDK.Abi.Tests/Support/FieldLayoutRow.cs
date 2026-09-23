namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>One literal expectation for one instance field of one ABI structure.</summary>
/// <param name="TypeFullName"><see cref="Type.FullName" /> of the structure (nested types use <c>+</c>).</param>
/// <param name="FieldName">Metadata name of the instance field, including compiler-generated backing fields.</param>
/// <param name="Offset">Byte offset of the field on x64.</param>
/// <param name="Width">Byte width of the field on x64 (a pointer or function pointer is 8).</param>
/// <param name="Kind">What the field is; a retyped <c>void*</c> keeps its width but changes its kind.</param>
internal readonly record struct FieldLayoutRow(string TypeFullName, string FieldName, int Offset, int Width, FieldKind Kind);

using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>A bounded copy of one host text value; <see langword="default" /> when nothing was copied.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AutoAssemblerHostTextCopy(string? Text, bool Truncated);

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>What a Markdown link target points at.</summary>
internal enum LinkTargetKind
{
	/// <summary>No target, as in <c>[text]()</c>.</summary>
	Empty,

	/// <summary>A URI with a scheme (<c>https:</c>, <c>mailto:</c>, ...) or a protocol-relative <c>//host</c> target.</summary>
	External,

	/// <summary>A fragment of the same page, as in <c>#heading</c>.</summary>
	SameDocument,

	/// <summary>A path relative to the page, or to the repository root when it starts with <c>/</c>.</summary>
	Relative
}

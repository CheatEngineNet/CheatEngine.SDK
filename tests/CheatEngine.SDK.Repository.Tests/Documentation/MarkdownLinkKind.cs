namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>How a link target is written in Markdown.</summary>
internal enum MarkdownLinkKind
{
	/// <summary>An inline link, <c>[text](target "title")</c>.</summary>
	Inline,

	/// <summary>An inline image, <c>![alt](target)</c>.</summary>
	Image,

	/// <summary>A link reference definition, <c>[label]: target</c>.</summary>
	ReferenceDefinition,

	/// <summary>The <c>href</c> or <c>src</c> attribute of a raw HTML tag.</summary>
	HtmlAttribute
}

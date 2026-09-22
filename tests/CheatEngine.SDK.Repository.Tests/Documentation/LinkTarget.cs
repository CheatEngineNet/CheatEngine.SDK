namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>A link target split into its percent-decoded path and fragment; the query is dropped.</summary>
internal readonly record struct LinkTarget(LinkTargetKind Kind, string Path, string? Fragment)
{
	/// <summary>Classifies and splits a target as written in Markdown.</summary>
	public static LinkTarget Parse(string target)
	{
		ArgumentNullException.ThrowIfNull(target);
		string trimmed = target.Trim();
		if (trimmed.Length == 0)
		{
			return new LinkTarget(LinkTargetKind.Empty, string.Empty, null);
		}

		if (HasScheme(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal))
		{
			return new LinkTarget(LinkTargetKind.External, trimmed, null);
		}

		int hash = trimmed.IndexOf('#', StringComparison.Ordinal);
		string path = hash < 0 ? trimmed : trimmed[..hash];
		string? fragment = hash < 0 ? null : Uri.UnescapeDataString(trimmed[(hash + 1)..]);
		int query = path.IndexOf('?', StringComparison.Ordinal);
		if (query >= 0)
		{
			path = path[..query];
		}

		return path.Length == 0
			? new LinkTarget(LinkTargetKind.SameDocument, string.Empty, fragment)
			: new LinkTarget(LinkTargetKind.Relative, Uri.UnescapeDataString(path), fragment);
	}

	/// <summary>A URI scheme as RFC 3986 defines it; a drive letter such as <c>C:</c> matches too and is reported elsewhere.</summary>
	private static bool HasScheme(string target)
	{
		if (!char.IsAsciiLetter(target[0]))
		{
			return false;
		}

		for (int i = 1; i < target.Length; i++)
		{
			char character = target[i];
			if (character == ':')
			{
				return true;
			}

			if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '.' or '-'))
			{
				return false;
			}
		}

		return false;
	}
}

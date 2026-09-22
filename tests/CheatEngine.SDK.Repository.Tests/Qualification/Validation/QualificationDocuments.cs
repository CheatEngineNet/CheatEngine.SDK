using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>Reads the committed qualification documents and applies the encoding rules of shared-contracts section 2.0.</summary>
internal static class QualificationDocuments
{
	internal const string QualificationDirectory = "docs/qualification";
	internal const string SchemaDirectory = "docs/qualification/schemas";
	internal const string ReceiptDirectory = "docs/qualification/receipts";
	internal const string SupportProfilePath = "docs/qualification/support-profile.json";
	internal const string SupportProfileMarkdownPath = "docs/qualification/support-profile.md";
	internal const string MatrixPath = "docs/qualification/matrix.json";
	internal const string ReadmePath = "docs/qualification/README.md";
	internal const string EventsSuffix = ".events.json";

	private static readonly JsonWriterOptions CanonicalWriterOptions = new()
	{
		Indented = true,
		IndentSize = 2,
		IndentCharacter = ' ',
		NewLine = "\n",
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly Dictionary<string, JsonSchemaSubset> s_schemas = new(StringComparer.Ordinal);
	private static readonly Lock SchemaGate = new();

	/// <summary>Reads a repository file as UTF-8 text with CRLF normalized to LF (the committed blob form).</summary>
	internal static string ReadNormalizedText(string repositoryRelativePath)
	{
		return NormalizeLineEndings(File.ReadAllText(Absolute(repositoryRelativePath), Encoding.UTF8));
	}

	/// <summary>Replaces CRLF with LF: the working tree is CRLF (<c>.gitattributes</c>), git blobs and clones are LF.</summary>
	internal static string NormalizeLineEndings(string text)
	{
		return text.Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	/// <summary>
	///     SHA-256, lowercase hex, of a committed JSON document: its UTF-8 bytes after CRLF is normalized to LF, so the
	///     value is the same on every checkout (A-SQUAL-4).
	/// </summary>
	internal static string CommittedJsonSha256(string repositoryRelativePath)
	{
		return Sha256OfNormalizedText(File.ReadAllText(Absolute(repositoryRelativePath), Encoding.UTF8));
	}

	/// <summary>The same rule applied to text already in memory.</summary>
	internal static string Sha256OfNormalizedText(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(NormalizeLineEndings(text));
		return Convert.ToHexStringLower(SHA256.HashData(bytes));
	}

	/// <summary>SHA-256, lowercase hex, of a repository file's raw bytes (binaries and LF-pinned sources).</summary>
	internal static string RawSha256(string repositoryRelativePath)
	{
		using FileStream stream = File.OpenRead(Absolute(repositoryRelativePath));
		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	/// <summary>Parses a committed JSON document into a standalone element.</summary>
	internal static JsonElement LoadJson(string repositoryRelativePath)
	{
		return ParseJson(ReadNormalizedText(repositoryRelativePath));
	}

	/// <summary>Parses JSON text into a standalone element.</summary>
	internal static JsonElement ParseJson(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	/// <summary>Loads (once) the committed schema of the given file name.</summary>
	internal static JsonSchemaSubset Schema(string schemaFileName)
	{
		lock (SchemaGate)
		{
			if (!s_schemas.TryGetValue(schemaFileName, out JsonSchemaSubset? schema))
			{
				schema = JsonSchemaSubset.Parse(ReadNormalizedText(SchemaDirectory + "/" + schemaFileName),
					schemaFileName);
				s_schemas.Add(schemaFileName, schema);
			}

			return schema;
		}
	}

	/// <summary>
	///     The canonical text of a qualification document: two-space indentation, LF newlines, non-ASCII characters kept
	///     readable, property order as written, and one final newline.
	/// </summary>
	internal static string Canonical(JsonElement document)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream, CanonicalWriterOptions))
		{
			document.WriteTo(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
	}

	/// <summary>Committed receipts (never event logs), repository-relative, sorted.</summary>
	internal static IReadOnlyList<string> CommittedReceipts()
	{
		List<string> receipts = [];
		string directory = Absolute(ReceiptDirectory);
		if (!Directory.Exists(directory))
		{
			return receipts;
		}

		foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
		{
			if (!file.EndsWith(EventsSuffix, StringComparison.OrdinalIgnoreCase))
			{
				receipts.Add(RepositoryRoot.ToRelative(file));
			}
		}

		receipts.Sort(StringComparer.Ordinal);
		return receipts;
	}

	/// <summary>Committed event logs, repository-relative, sorted.</summary>
	internal static IReadOnlyList<string> CommittedEventLogs()
	{
		List<string> logs = [];
		string directory = Absolute(ReceiptDirectory);
		if (!Directory.Exists(directory))
		{
			return logs;
		}

		foreach (string file in Directory.EnumerateFiles(directory, "*" + EventsSuffix, SearchOption.AllDirectories))
		{
			logs.Add(RepositoryRoot.ToRelative(file));
		}

		logs.Sort(StringComparer.Ordinal);
		return logs;
	}

	/// <summary>The projects listed in <c>CheatEngine.SDK.slnx</c>, repository-relative with forward slashes.</summary>
	internal static IReadOnlySet<string> SolutionProjects()
	{
		HashSet<string> projects = new(StringComparer.Ordinal);
		foreach (XElement project in XDocument.Load(RepositoryRoot.SolutionPath).Descendants("Project"))
		{
			string? path = (string?) project.Attribute("Path");
			if (path is not null)
			{
				projects.Add(path.Replace('\\', '/'));
			}
		}

		return projects;
	}

	/// <summary>The <c>Platform Project</c> mapping of a solution project, or <see langword="null" />.</summary>
	internal static string? SolutionPlatform(string projectPath)
	{
		foreach (XElement project in XDocument.Load(RepositoryRoot.SolutionPath).Descendants("Project"))
		{
			if (string.Equals(((string?) project.Attribute("Path"))?.Replace('\\', '/'), projectPath,
					StringComparison.Ordinal))
			{
				return (string?) project.Element("Platform")?.Attribute("Project");
			}
		}

		return null;
	}

	internal static bool Exists(string repositoryRelativePath)
	{
		return File.Exists(Absolute(repositoryRelativePath));
	}

	internal static string Absolute(string repositoryRelativePath)
	{
		return Path.Combine(RepositoryRoot.Path, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}
}

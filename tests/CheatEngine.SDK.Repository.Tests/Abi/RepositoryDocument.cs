using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Abi;

/// <summary>
///     Minimal committed-file helpers for the ABI and Lua-bridge document tests. These documents are test-owned data
///     under this project (never a top-level <c>docs/</c> folder, per the maintainer's no-custom-scripting pivot) and
///     are read directly off disk with no JSON Schema infrastructure: every rule is a plain C# assertion instead.
/// </summary>
internal static class RepositoryDocument
{
	/// <summary>The absolute path of a repository-relative path.</summary>
	public static string Absolute(string repositoryRelativePath)
	{
		return Path.Combine(RepositoryRoot.Path, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>Whether a repository-relative path names a committed file.</summary>
	public static bool Exists(string repositoryRelativePath)
	{
		return File.Exists(Absolute(repositoryRelativePath));
	}

	/// <summary>Whether a repository-relative path names a committed directory.</summary>
	public static bool DirectoryExists(string repositoryRelativePath)
	{
		return Directory.Exists(Absolute(repositoryRelativePath));
	}

	/// <summary>The committed text of a file, with CRLF normalized to LF.</summary>
	public static string ReadNormalizedText(string repositoryRelativePath)
	{
		return File.ReadAllText(Absolute(repositoryRelativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	/// <summary>Parses a committed JSON document.</summary>
	public static JsonElement LoadJson(string repositoryRelativePath)
	{
		return ParseJson(ReadNormalizedText(repositoryRelativePath));
	}

	/// <summary>Parses a JSON string into a standalone (document-independent) element.</summary>
	public static JsonElement ParseJson(string text)
	{
		using JsonDocument document = JsonDocument.Parse(text);
		return document.RootElement.Clone();
	}

	/// <summary>The lowercase hex SHA-256 of the raw (unnormalized) bytes of a committed file.</summary>
	public static string RawSha256(string repositoryRelativePath)
	{
		byte[] bytes = File.ReadAllBytes(Absolute(repositoryRelativePath));
		return Convert.ToHexStringLower(SHA256.HashData(bytes));
	}

	/// <summary>
	///     Reserializes <paramref name="element" /> the way a committed document is formatted: 2-space indent, LF
	///     newlines, relaxed escaping, one trailing newline
	///     (https://learn.microsoft.com/dotnet/api/system.text.json.jsonwriteroptions.newline).
	/// </summary>
	public static string Canonical(JsonElement element)
	{
		JsonWriterOptions writerOptions = new()
		{
			Indented = true,
			IndentSize = 2,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			NewLine = "\n"
		};
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream, writerOptions))
		{
			element.WriteTo(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
	}
}

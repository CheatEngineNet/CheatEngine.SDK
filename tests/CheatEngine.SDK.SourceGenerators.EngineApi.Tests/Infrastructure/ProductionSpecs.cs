using System.Reflection;
using System.Xml.Linq;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     The production spec files of <c>source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/</c> and the
///     <c>CheatEngine.SDK.Engine</c> project file that wires some of them, embedded into this test assembly at build time:
///     the tests read the committed inputs, never a workspace path at run time.
/// </summary>
internal static class ProductionSpecs
{
	private const string SpecPrefix = "CheatEngine.SDK.EngineApi.Tests.Specs.";
	private const string EngineProjectResource = "CheatEngine.SDK.EngineApi.Tests.EngineProject.xml";
	private const string SpecDirectory = "source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/";

	private static readonly Assembly Resources = typeof(ProductionSpecs).Assembly;

	/// <summary>Every embedded production spec: file name to text, ordered by file name.</summary>
	public static IReadOnlyList<(string FileName, string Text)> All
	{
		get
		{
			List<(string FileName, string Text)> specs = [];
			foreach (string name in Resources.GetManifestResourceNames().Order(StringComparer.Ordinal))
			{
				if (name.StartsWith(SpecPrefix, StringComparison.Ordinal))
				{
					specs.Add((name[SpecPrefix.Length..], Read(name)));
				}
			}

			return specs;
		}
	}

	/// <summary>The text of one production spec, by file name.</summary>
	public static string Text(string fileName)
	{
		return Read(SpecPrefix + fileName);
	}

	/// <summary>
	///     The spec file names the Engine project passes to the generator as <c>AdditionalFiles</c> (the wired specs).
	/// </summary>
	public static IReadOnlySet<string> WiredFileNames()
	{
		XDocument project = XDocument.Parse(Read(EngineProjectResource));
		HashSet<string> wired = new(StringComparer.Ordinal);
		foreach (XElement item in project.Descendants("AdditionalFiles"))
		{
			string include = ((string?) item.Attribute("Include") ?? string.Empty).Replace('\\', '/');
			int index = include.IndexOf(SpecDirectory, StringComparison.Ordinal);
			Assert.True(index >= 0, "An Engine AdditionalFiles item is not a production spec: " + include);
			wired.Add(include[(index + SpecDirectory.Length)..]);
		}

		return wired;
	}

	private static string Read(string resourceName)
	{
		using Stream stream = Resources.GetManifestResourceStream(resourceName)
							  ?? throw new InvalidOperationException("Missing embedded resource " + resourceName + ".");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}

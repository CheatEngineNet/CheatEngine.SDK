using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     A committed YAML file (workflow, Dependabot configuration, issue form) read through YamlDotNet's representation
///     model. Every scalar stays the string written in the file: the workflow key <c>on</c> stays <c>"on"</c> (no YAML 1.1
///     boolean resolution) and <c>'true'</c> and <c>true</c> both read as <c>"true"</c>, which is how GitHub compares
///     them.
/// </summary>
internal sealed class YamlDocument
{
	private YamlDocument(string relativePath, string text, YamlMappingNode root)
	{
		RelativePath = relativePath;
		Text = text;
		Root = root;
	}

	/// <summary>The repository-relative path, with forward slashes.</summary>
	public string RelativePath
	{
		get;
	}

	/// <summary>The raw text, for rules about comments or exact spelling.</summary>
	public string Text
	{
		get;
	}

	/// <summary>The top-level mapping.</summary>
	public YamlMappingNode Root
	{
		get;
	}

	/// <summary>The <c>jobs</c> of a workflow, in file order, keyed by job id.</summary>
	public IReadOnlyList<KeyValuePair<string, YamlMappingNode>> Jobs
	{
		get
		{
			List<KeyValuePair<string, YamlMappingNode>> jobs = [];
			if (Child(Root, "jobs") is YamlMappingNode mapping)
			{
				foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
				{
					if (entry.Value is YamlMappingNode job)
					{
						jobs.Add(new KeyValuePair<string, YamlMappingNode>(((YamlScalarNode) entry.Key).Value ?? "",
							job));
					}
				}
			}

			return jobs;
		}
	}

	/// <summary>The event names of the workflow's <c>on</c> key, whether it is a scalar, a sequence or a mapping.</summary>
	public IReadOnlyList<string> Triggers
	{
		get
		{
			YamlNode? on = Child(Root, "on");
			return on switch
			{
				YamlMappingNode mapping => KeysOf(mapping),
				_ => ScalarsOf(on)
			};
		}
	}

	/// <summary>Loads a file below the repository root.</summary>
	public static YamlDocument Load(string relativePath)
	{
		return Parse(relativePath, RepositoryFile.ReadText(relativePath));
	}

	/// <summary>Parses YAML text; the path is only used in messages.</summary>
	public static YamlDocument Parse(string relativePath, string text)
	{
		YamlStream stream = new();
		using (StringReader reader = new(text))
		{
			stream.Load(reader);
		}

		Assert.True(stream.Documents.Count == 1, $"'{relativePath}' must hold exactly one YAML document.");
		YamlMappingNode? root = stream.Documents[0].RootNode as YamlMappingNode;
		Assert.True(root is not null, $"The root of '{relativePath}' must be a mapping.");
		return new YamlDocument(relativePath, text, root);
	}

	/// <summary>The job with the given id; fails the test when it does not exist.</summary>
	public YamlMappingNode Job(string id)
	{
		YamlMappingNode? found = null;
		foreach (KeyValuePair<string, YamlMappingNode> job in Jobs)
		{
			if (string.Equals(job.Key, id, StringComparison.Ordinal))
			{
				found = job.Value;
			}
		}

		Assert.True(found is not null, $"'{RelativePath}' has no job '{id}'.");
		return found;
	}

	/// <summary>The value of an event under <c>on</c>, or <see langword="null" /> (also for a scalar or sequence form).</summary>
	public YamlNode? Trigger(string name)
	{
		return Child(Child(Root, "on"), name);
	}

	/// <summary>The value of <paramref name="key" /> when <paramref name="node" /> is a mapping that holds it.</summary>
	public static YamlNode? Child(YamlNode? node, string key)
	{
		if (node is not YamlMappingNode mapping)
		{
			return null;
		}

		foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
		{
			if (entry.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
			{
				return entry.Value;
			}
		}

		return null;
	}

	/// <summary>The scalar value of <paramref name="key" />, or <see langword="null" /> when it is absent or not a scalar.</summary>
	public static string? Scalar(YamlNode? node, string key)
	{
		return Child(node, key) is YamlScalarNode scalar ? scalar.Value : null;
	}

	/// <summary>The scalar values of <paramref name="key" />: one for a scalar, every scalar item for a sequence.</summary>
	public static IReadOnlyList<string> Scalars(YamlNode? node, string key)
	{
		return ScalarsOf(Child(node, key));
	}

	/// <summary>The scalar items of a scalar or sequence node.</summary>
	public static IReadOnlyList<string> ScalarsOf(YamlNode? node)
	{
		List<string> values = [];
		switch (node)
		{
			case YamlScalarNode scalar when scalar.Value is not null:
				values.Add(scalar.Value);
				break;
			case YamlSequenceNode sequence:
				foreach (YamlNode item in sequence.Children)
				{
					if (item is YamlScalarNode { Value: not null } itemScalar)
					{
						values.Add(itemScalar.Value);
					}
				}

				break;
		}

		return values;
	}

	/// <summary>The mapping items of the sequence at <paramref name="key" />.</summary>
	public static IReadOnlyList<YamlMappingNode> Mappings(YamlNode? node, string key)
	{
		List<YamlMappingNode> mappings = [];
		if (Child(node, key) is YamlSequenceNode sequence)
		{
			foreach (YamlNode item in sequence.Children)
			{
				if (item is YamlMappingNode mapping)
				{
					mappings.Add(mapping);
				}
			}
		}

		return mappings;
	}

	/// <summary>The keys of a mapping, in file order.</summary>
	public static IReadOnlyList<string> KeysOf(YamlNode? node)
	{
		List<string> keys = [];
		if (node is YamlMappingNode mapping)
		{
			foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
			{
				if (entry.Key is YamlScalarNode { Value: not null } scalar)
				{
					keys.Add(scalar.Value);
				}
			}
		}

		return keys;
	}

	/// <summary>The steps of a job.</summary>
	public static IReadOnlyList<YamlMappingNode> Steps(YamlMappingNode job)
	{
		return Mappings(job, "steps");
	}

	/// <summary>
	///     The <c>permissions</c> of a workflow or job as scope → access. <c>{ }</c> gives an empty map; a scalar such as
	///     <c>read-all</c> is returned under the key <c>*</c>; <see langword="null" /> when the key is absent.
	/// </summary>
	public static IReadOnlyDictionary<string, string>? Permissions(YamlNode? owner)
	{
		YamlNode? permissions = Child(owner, "permissions");
		if (permissions is null)
		{
			return null;
		}

		Dictionary<string, string> scopes = new(StringComparer.Ordinal);
		if (permissions is YamlScalarNode scalar)
		{
			scopes["*"] = scalar.Value ?? "";
			return scopes;
		}

		foreach (string scope in KeysOf(permissions))
		{
			scopes[scope] = Scalar(permissions, scope) ?? "";
		}

		return scopes;
	}

	/// <summary>The action reference of a step (<c>uses:</c>), without its trailing comment.</summary>
	public static string? Uses(YamlMappingNode step)
	{
		return Scalar(step, "uses");
	}

	/// <summary>True when the step uses the action at <paramref name="actionPath" /> (for example <c>actions/checkout</c>).</summary>
	public static bool UsesAction(YamlMappingNode step, string actionPath)
	{
		string? uses = Uses(step);
		return uses is not null && uses.StartsWith(actionPath + "@", StringComparison.Ordinal);
	}

	/// <summary>Collapses runs of whitespace, so folded and literal block scalars compare by content.</summary>
	public static string NormalizeWhitespace(string value)
	{
		return string.Join(' ', value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
	}

	/// <summary>The 1-based line of the first occurrence of <paramref name="text" /> in the file, or 0.</summary>
	public int LineOf(string text)
	{
		int index = Text.IndexOf(text, StringComparison.Ordinal);
		if (index < 0)
		{
			return 0;
		}

		int line = 1;
		for (int i = 0; i < index; i++)
		{
			if (Text[i] == '\n')
			{
				line++;
			}
		}

		return line;
	}
}

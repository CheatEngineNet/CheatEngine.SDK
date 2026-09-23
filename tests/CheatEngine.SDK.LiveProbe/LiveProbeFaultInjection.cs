using System.Globalization;
using System.Text.Json;

using CheatEngine.SDK.Hosting.Diagnostics;

namespace LiveProbe;

/// <summary>
///     The Checkpoint-B fault-injection switch (qualification scenarios Q06 and Q08 at C3). A JSON file named
///     <see cref="FileName" /> next to the plugin assembly selects one lifecycle stage that throws a managed exception:
///     <c>{"schema":"ce77-live-probe-fault-v1","throwIn":"None|FactoryCreate|OnEnable|OnDisable"}</c>.
/// </summary>
/// <remarks>
///     The switch is read once per enable, without Lua, and only when <see cref="LiveProbeAuthorization.Evaluate" />
///     allows: without the operator's short-lived authorization the file is never opened. Every decision, including an
///     ignored file, is logged and kept for <c>ce77_live_probe_status_json()</c>. This is a test-harness mechanism only;
///     the SDK has no such switch.
/// </remarks>
internal static class LiveProbeFaultInjection
{
	internal const string FileName = "liveprobe.fault.json";
	internal const string Schema = "ce77-live-probe-fault-v1";
	internal const string InjectedFaultMessagePrefix = "CE 7.7 live probe injected fault at ";

	private const int MaximumFileBytes = 4096;
	private static readonly Lock Gate = new();
	private static LiveProbeFaultDecision s_current = LiveProbeFaultDecision.NotEvaluated;
	private static bool s_evaluatedForPendingEnable;
	private static int s_enableSequence;
	private static readonly List<string> s_injected = [];

	/// <summary>Gets the decision of the most recent enable.</summary>
	internal static LiveProbeFaultDecision Current
	{
		get
		{
			lock (Gate)
			{
				return s_current;
			}
		}
	}

	/// <summary>Gets the stages that actually threw, in order, as <c>stage@enable</c> entries.</summary>
	internal static IReadOnlyList<string> InjectedFaults
	{
		get
		{
			lock (Gate)
			{
				return [.. s_injected];
			}
		}
	}

	/// <summary>
	///     Evaluates the switch. Pure apart from the two injected delegates: the authorization evaluator and the file
	///     reader (which returns <see langword="null" /> when the file does not exist).
	/// </summary>
	/// <param name="evaluateAuthorization">The live-probe authorization gate; the file is read only when it allows.</param>
	/// <param name="pluginDirectory">The directory that holds the plugin assembly.</param>
	/// <param name="readFile">Reads a whole file as UTF-8 text, or returns <see langword="null" /> when it is absent.</param>
	/// <returns>The decision; never throws for a missing, unreadable or malformed file.</returns>
	internal static LiveProbeFaultDecision Evaluate(Func<AuthorizationDecision> evaluateAuthorization,
		string pluginDirectory, Func<string, string?> readFile)
	{
		AuthorizationDecision authorization = evaluateAuthorization();
		if (!authorization.IsAllowed)
		{
			return new LiveProbeFaultDecision(LiveProbeFaultStage.None,
				"Fault switch ignored without reading it: live-probe authorization denied (" + authorization.Reason +
				").", false);
		}

		string path = Path.Combine(pluginDirectory, FileName);
		string? text;
		try
		{
			text = readFile(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
											  or ArgumentException or NotSupportedException)
		{
			return new LiveProbeFaultDecision(LiveProbeFaultStage.None,
				"Fault switch ignored: " + FileName + " could not be read (" + exception.GetType().Name + ").", true);
		}

		if (text is null)
		{
			return new LiveProbeFaultDecision(LiveProbeFaultStage.None,
				"No " + FileName + " next to the plugin: no fault.", false);
		}

		return Parse(text);
	}

	/// <summary>Returns whether <paramref name="decision" /> throws at <paramref name="stage" />.</summary>
	internal static bool ThrowsAt(LiveProbeFaultDecision decision, LiveProbeFaultStage stage)
	{
		return stage is not LiveProbeFaultStage.None && decision.Stage == stage;
	}

	/// <summary>
	///     Production hook of <c>IPluginFactory.Create</c>: the first point of an enable that constructs the plugin.
	///     Evaluates the switch for this enable and throws when it selects <see cref="LiveProbeFaultStage.FactoryCreate" />.
	/// </summary>
	internal static void EnterFactoryCreate()
	{
		LiveProbeFaultDecision decision = EvaluateForEnable();
		lock (Gate)
		{
			s_evaluatedForPendingEnable = true;
		}

		ThrowIfSelected(decision, LiveProbeFaultStage.FactoryCreate);
	}

	/// <summary>
	///     Production hook of <c>OnEnable</c>. Reuses the decision the factory made during this same enable, otherwise
	///     evaluates the switch, then throws when it selects <see cref="LiveProbeFaultStage.OnEnable" />.
	/// </summary>
	internal static void EnterOnEnable()
	{
		LiveProbeFaultDecision decision;
		bool reuse;
		lock (Gate)
		{
			reuse = s_evaluatedForPendingEnable;
			s_evaluatedForPendingEnable = false;
			decision = s_current;
		}

		if (!reuse)
		{
			decision = EvaluateForEnable();
		}

		ThrowIfSelected(decision, LiveProbeFaultStage.OnEnable);
	}

	/// <summary>Production hook of <c>OnDisable</c>: throws when this enable's decision selects that stage.</summary>
	internal static void EnterOnDisable()
	{
		ThrowIfSelected(Current, LiveProbeFaultStage.OnDisable);
	}

	private static LiveProbeFaultDecision EvaluateForEnable()
	{
		string pluginDirectory = Path.GetDirectoryName(typeof(LiveProbeFaultInjection).Assembly.Location) ??
								 AppContext.BaseDirectory;
		LiveProbeFaultDecision decision = Evaluate(LiveProbeAuthorization.Evaluate, pluginDirectory, ReadIfPresent);
		int sequence;
		lock (Gate)
		{
			sequence = ++s_enableSequence;
			s_current = decision;
		}

		HostLog.Write(decision.Stage == LiveProbeFaultStage.None ? HostLogLevel.Information : HostLogLevel.Warning,
			string.Create(CultureInfo.InvariantCulture,
				$"CE 7.7 live probe: fault switch for enable #{sequence} -> {decision.Stage}. {decision.Reason}"));
		return decision;
	}

	private static void ThrowIfSelected(LiveProbeFaultDecision decision, LiveProbeFaultStage stage)
	{
		if (!ThrowsAt(decision, stage))
		{
			return;
		}

		lock (Gate)
		{
			s_injected.Add(string.Create(CultureInfo.InvariantCulture, $"{stage}@{s_enableSequence}"));
		}

		throw new InvalidOperationException(InjectedFaultMessagePrefix + stage + " (" + FileName + ").");
	}

	private static LiveProbeFaultDecision Parse(string text)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.String)
			{
				return Ignored("it has no schema string");
			}

			if (!string.Equals(schema.GetString(), Schema, StringComparison.Ordinal))
			{
				return Ignored("unknown schema '" + schema.GetString() + "', expected '" + Schema + "'");
			}

			if (!root.TryGetProperty("throwIn", out JsonElement throwIn) || throwIn.ValueKind != JsonValueKind.String)
			{
				return Ignored("it has no throwIn string");
			}

			string requested = throwIn.GetString() ?? string.Empty;
			foreach (LiveProbeFaultStage stage in Enum.GetValues<LiveProbeFaultStage>())
			{
				if (string.Equals(stage.ToString(), requested, StringComparison.Ordinal))
				{
					return new LiveProbeFaultDecision(stage,
						"Fault switch selects " + stage + " (" + FileName + ", schema " + Schema + ").", true);
				}
			}

			return Ignored("unknown throwIn '" + requested + "'");
		}
		catch (JsonException exception)
		{
			return Ignored("it is not valid JSON (" + exception.GetType().Name + ")");
		}
	}

	private static LiveProbeFaultDecision Ignored(string why)
	{
		return new LiveProbeFaultDecision(LiveProbeFaultStage.None,
			"Fault switch ignored and reported: " + FileName + " " + why + ".", true);
	}

	private static string? ReadIfPresent(string path)
	{
		FileInfo file = new(path);
		if (!file.Exists)
		{
			return null;
		}

		if (file.Length > MaximumFileBytes)
		{
			throw new IOException("The fault switch is larger than " +
								  MaximumFileBytes.ToString(CultureInfo.InvariantCulture) + " bytes.");
		}

		return File.ReadAllText(path);
	}
}

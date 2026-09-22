using System.Globalization;
using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     The rules of a qualification receipt beyond its schema: it cites a qualifiable profile, encodes its identity,
///     matches the profile's host facts unless it records a NotApplicable preflight mismatch, and carries no local path.
///     Committed receipts additionally sit at their canonical path with an event log whose LF-normalized hash matches.
/// </summary>
internal static class ReceiptRules
{
	/// <summary>Validates one receipt document against the support profile.</summary>
	internal static IReadOnlyList<string> Validate(JsonElement receipt, JsonElement supportProfile)
	{
		List<string> errors =
			[.. QualificationDocuments.Schema(QualificationContract.ReceiptSchemaFile).Validate(receipt)];
		if (errors.Count > 0)
		{
			return errors;
		}

		foreach (string path in TextRules.AbsoluteLocalPaths(receipt))
		{
			errors.Add("contains an absolute local path at " + path);
		}

		string profileId = receipt.GetProperty("profileId").GetString()!;
		JsonElement? profile = SupportProfileRules.Find(supportProfile, profileId);
		if (profile is null)
		{
			errors.Add($"profileId '{profileId}' is not a profile of the support profile.");
		}
		else if (!SupportProfileRules.QualifiableByProfileId(supportProfile)[profileId])
		{
			errors.Add($"profileId '{profileId}' is a documentary profile, which is never qualifiable.");
		}
		else if (!string.Equals(receipt.GetProperty("status").GetString(), "NotApplicable", StringComparison.Ordinal))
		{
			CheckPreflight(receipt.GetProperty("host"), profile.Value, errors);
		}

		CheckIdentity(receipt, errors);
		return errors;
	}

	/// <summary>Validates a committed receipt file, its location and its event log.</summary>
	internal static IReadOnlyList<string> ValidateCommitted(string receiptPath, JsonElement supportProfile)
	{
		JsonElement receipt = QualificationDocuments.LoadJson(receiptPath);
		List<string> errors = [];
		foreach (string error in Validate(receipt, supportProfile))
		{
			errors.Add(receiptPath + ": " + error);
		}

		if (errors.Count > 0)
		{
			return errors;
		}

		string receiptId = receipt.GetProperty("receiptId").GetString()!;
		string expectedPath = QualificationDocuments.ReceiptDirectory + "/" +
							  receipt.GetProperty("qualificationId").GetString() + "/" + receiptId + ".json";
		if (!string.Equals(receiptPath, expectedPath, StringComparison.Ordinal))
		{
			errors.Add($"{receiptPath}: must be committed as {expectedPath}.");
		}

		JsonElement eventLog = receipt.GetProperty("eventLog");
		string eventsPath = receiptPath[..(receiptPath.LastIndexOf('/') + 1)] + eventLog.GetProperty("path").GetString();
		if (!QualificationDocuments.Exists(eventsPath))
		{
			errors.Add($"{receiptPath}: its event log {eventsPath} is not committed.");
			return errors;
		}

		if (!string.Equals(QualificationDocuments.CommittedJsonSha256(eventsPath),
				eventLog.GetProperty("sha256").GetString(), StringComparison.Ordinal))
		{
			errors.Add($"{receiptPath}: eventLog.sha256 is not the LF-normalized SHA-256 of {eventsPath}.");
		}

		JsonElement events = QualificationDocuments.LoadJson(eventsPath);
		foreach (string error in ValidateEventLog(events, receiptId))
		{
			errors.Add(eventsPath + ": " + error);
		}

		return errors;
	}

	/// <summary>Validates an event log: schema, receipt id, no local path and no raw debug output.</summary>
	internal static IReadOnlyList<string> ValidateEventLog(JsonElement events, string receiptId)
	{
		List<string> errors =
			[.. QualificationDocuments.Schema(QualificationContract.EventsSchemaFile).Validate(events)];
		if (errors.Count > 0)
		{
			return errors;
		}

		if (!string.Equals(events.GetProperty("receiptId").GetString(), receiptId, StringComparison.Ordinal))
		{
			errors.Add($"receiptId must be {receiptId}.");
		}

		foreach (string path in TextRules.AbsoluteLocalPaths(events))
		{
			errors.Add("contains an unredacted local path at " + path);
		}

		int index = 0;
		foreach (JsonElement item in events.GetProperty("events").EnumerateArray())
		{
			if (TextRules.ContainsRawDebugOutput(item.GetProperty("message").GetString() ?? string.Empty))
			{
				errors.Add(string.Create(CultureInfo.InvariantCulture,
					$"/events/{index}/message contains raw debug output; record structured values instead."));
			}

			index++;
		}

		return errors;
	}

	/// <summary>
	///     <c>R-yyyyMMddTHHmmssZ-Qid-xxxxxxxx</c>: the start of the run (<c>timings.startedUtc</c>, seconds), the
	///     qualification id and the first eight hex digits of the package SHA-256.
	/// </summary>
	internal static string ExpectedReceiptId(JsonElement receipt)
	{
		DateTimeOffset started = DateTimeOffset.Parse(
			receipt.GetProperty("timings").GetProperty("startedUtc").GetString()!, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
		string package = receipt.GetProperty("package").GetProperty("nupkgSha256").GetString()!;
		return "R-" + started.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" +
			   receipt.GetProperty("qualificationId").GetString() + "-" + package[..8];
	}

	private static void CheckIdentity(JsonElement receipt, List<string> errors)
	{
		string receiptId = receipt.GetProperty("receiptId").GetString()!;
		string expected = ExpectedReceiptId(receipt);
		if (!string.Equals(receiptId, expected, StringComparison.Ordinal))
		{
			errors.Add($"receiptId '{receiptId}' must encode its start time, qualification id and package: '{expected}'.");
		}

		string expectedLog = receiptId + QualificationDocuments.EventsSuffix;
		if (!string.Equals(receipt.GetProperty("eventLog").GetProperty("path").GetString(), expectedLog,
				StringComparison.Ordinal))
		{
			errors.Add($"eventLog.path must be {expectedLog}.");
		}
	}

	private static void CheckPreflight(JsonElement host, JsonElement profile, List<string> errors)
	{
		JsonElement profileHost = profile.GetProperty("host");
		Expect(errors, "host.ceExeName", host, "ceExeName", profileHost.GetProperty("exeName"));
		Expect(errors, "host.ceExeSha256", host, "ceExeSha256", profileHost.GetProperty("exeSha256"));
		Expect(errors, "host.ceFileVersion", host, "ceFileVersion", profileHost.GetProperty("version"));
		Expect(errors, "host.luaDllSha256", host, "luaDllSha256", profile.GetProperty("lua").GetProperty("sha256"));
		Expect(errors, "host.runtimeconfigSha256", host, "runtimeconfigSha256",
			profile.GetProperty("runtime").GetProperty("runtimeconfig").GetProperty("sha256"));
	}

	private static void Expect(List<string> errors, string name, JsonElement host, string property, JsonElement expected)
	{
		string? actual = host.GetProperty(property).GetString();
		if (!string.Equals(actual, expected.GetString(), StringComparison.Ordinal))
		{
			errors.Add($"{name} '{actual}' differs from the profile ('{expected.GetString()}'): a run on another host is NotApplicable with a justification, never Passed or Failed.");
		}
	}
}

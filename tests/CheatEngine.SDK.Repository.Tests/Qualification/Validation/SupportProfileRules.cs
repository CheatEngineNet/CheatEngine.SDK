using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>Reads and checks <c>support-profile.json</c> beyond its schema.</summary>
internal static class SupportProfileRules
{
	/// <summary>Schema validity plus the semantic rules: unique ids, no local path, the two frozen profile ids.</summary>
	internal static IReadOnlyList<string> Validate(JsonElement supportProfile)
	{
		List<string> errors =
			[.. QualificationDocuments.Schema(QualificationContract.SupportProfileSchemaFile).Validate(supportProfile)];
		if (errors.Count > 0)
		{
			return errors;
		}

		CheckUnique(supportProfile, "profiles", "id", errors);
		CheckUnique(supportProfile, "decisions", "id", errors);
		CheckUnique(supportProfile, "unsupportedRoutes", "id", errors);
		foreach (string path in TextRules.AbsoluteLocalPaths(supportProfile))
		{
			errors.Add("contains an absolute local path at " + path);
		}

		Dictionary<string, bool> qualifiable = QualifiableByProfileId(supportProfile);
		if (!qualifiable.TryGetValue(QualificationContract.DocumentaryProfileId, out bool documentary) || documentary)
		{
			errors.Add($"'{QualificationContract.DocumentaryProfileId}' must exist and stay Documentary.");
		}

		if (!qualifiable.TryGetValue(QualificationContract.QualifiableProfileId, out bool isQualifiable) ||
			!isQualifiable)
		{
			errors.Add($"'{QualificationContract.QualifiableProfileId}' must exist and stay Qualifiable.");
		}

		return errors;
	}

	/// <summary>Maps each profile id to whether the profile is qualifiable.</summary>
	internal static Dictionary<string, bool> QualifiableByProfileId(JsonElement supportProfile)
	{
		Dictionary<string, bool> profiles = new(StringComparer.Ordinal);
		foreach (JsonElement profile in supportProfile.GetProperty("profiles").EnumerateArray())
		{
			profiles[profile.GetProperty("id").GetString()!] = profile.GetProperty("qualifiable").GetBoolean() &&
															   string.Equals(profile.GetProperty("kind").GetString(),
																   "Qualifiable", StringComparison.Ordinal);
		}

		return profiles;
	}

	/// <summary>Returns the profile with the given id.</summary>
	internal static JsonElement? Find(JsonElement supportProfile, string id)
	{
		foreach (JsonElement profile in supportProfile.GetProperty("profiles").EnumerateArray())
		{
			if (string.Equals(profile.GetProperty("id").GetString(), id, StringComparison.Ordinal))
			{
				return profile;
			}
		}

		return null;
	}

	private static void CheckUnique(JsonElement document, string arrayName, string idName, List<string> errors)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (JsonElement item in document.GetProperty(arrayName).EnumerateArray())
		{
			string id = item.GetProperty(idName).GetString()!;
			if (!seen.Add(id))
			{
				errors.Add($"{arrayName}: the id '{id}' appears more than once.");
			}
		}
	}
}

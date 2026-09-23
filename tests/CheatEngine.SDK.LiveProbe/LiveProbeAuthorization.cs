using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveProbe;

// The live plugin reads this document itself.  A command-line switch, an IDE launch profile, or a target path alone is
// not authorization: the operator must affirm this exact phrase, supply a short-lived manifest, and keep the declared
// disposable process alive.  This is deliberately an internal test-only contract, not an SDK configuration API.
internal static class LiveProbeAuthorization
{
	internal const string ExactCheatEngineSha256 = "9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D";
	internal const string ExactCheatEngineFileVersion = "7.7.0.10621";
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";
	private const string AcknowledgementVariable = "CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT";
	private const string ManifestVariable = "CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE";

	[SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long",
		Justification =
			"Authorization evaluation keeps all fail-closed checks and diagnostics in one auditable sequence.")]
	internal static AuthorizationDecision Evaluate()
	{
		if (!TryAuthorizeManifest(out AuthorizationManifest manifest, out string manifestFailure))
		{
			return AuthorizationDecision.Denied(manifestFailure);
		}

		if (!TryAuthorizeHost(manifest, out string hostPath, out string hostHash, out string hostFailure))
		{
			return AuthorizationDecision.Denied(hostFailure);
		}

		if (!TryAuthorizeTarget(manifest, out string targetPath, out string targetHash, out string targetFailure))
		{
			return AuthorizationDecision.Denied(targetFailure);
		}

		return AuthorizationDecision.Allowed(hostPath, hostHash, manifest.TargetProcessId, targetPath, targetHash,
			manifest.ExpiresUtc);
	}

	private static bool TryAuthorizeManifest(out AuthorizationManifest manifest, out string failure)
	{
		manifest = default;
		failure = string.Empty;
		if (IntPtr.Size != 8)
		{
			failure = "The current process is not x64.";
			return false;
		}

		string? acknowledgement = Environment.GetEnvironmentVariable(AcknowledgementVariable);
		if (!string.Equals(acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			failure = "The explicit CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT phrase is absent.";
			return false;
		}

		string? manifestPath = Environment.GetEnvironmentVariable(ManifestVariable);
		if (string.IsNullOrWhiteSpace(manifestPath))
		{
			failure = "CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE is absent.";
			return false;
		}

		if (!TryReadManifest(manifestPath, out manifest, out failure))
		{
			return false;
		}

		if (!string.Equals(manifest.Acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			failure = "The authorization manifest has no matching acknowledgement.";
			return false;
		}

		if (!manifest.Disposable)
		{
			failure = "The authorization manifest does not mark the target disposable.";
			return false;
		}

		if (manifest.ExpiresUtc <= DateTimeOffset.UtcNow)
		{
			failure = "The authorization manifest has expired.";
			return false;
		}

		return true;
	}

	private static bool TryAuthorizeHost(AuthorizationManifest manifest, out string hostPath, out string hostHash,
		out string failure)
	{
		hostPath = string.Empty;
		hostHash = string.Empty;
		failure = string.Empty;
		if (!TryGetProcessImage(Environment.ProcessId, out hostPath, out string hostFailure))
		{
			failure = "The CE host image cannot be inspected: " + hostFailure;
			return false;
		}

		if (!IsAmd64Pe(hostPath, out string hostArchitectureFailure))
		{
			failure = "The CE host image is not an AMD64 PE: " + hostArchitectureFailure;
			return false;
		}

		if (!TryHash(hostPath, out hostHash, out string hostHashFailure))
		{
			failure = "The CE host image cannot be hashed: " + hostHashFailure;
			return false;
		}

		if (!string.Equals(hostHash, ExactCheatEngineSha256, StringComparison.Ordinal))
		{
			failure = "The host SHA-256 is not the pinned CE 7.7.0.10621 x64 binary.";
			return false;
		}

		if (!string.Equals(manifest.HostSha256, ExactCheatEngineSha256, StringComparison.Ordinal))
		{
			failure = "The manifest does not pin the CE 7.7.0.10621 x64 SHA-256.";
			return false;
		}

		string? hostVersion = FileVersionInfo.GetVersionInfo(hostPath).FileVersion;
		if (!string.Equals(hostVersion, ExactCheatEngineFileVersion, StringComparison.Ordinal))
		{
			failure = "The pinned CE executable has an unexpected file version: " + hostVersion + ".";
			return false;
		}

		return true;
	}

	private static bool TryAuthorizeTarget(AuthorizationManifest manifest, out string targetPath, out string targetHash,
		out string failure)
	{
		targetPath = string.Empty;
		targetHash = string.Empty;
		failure = string.Empty;
		if (manifest.TargetProcessId == Environment.ProcessId)
		{
			failure = "The declared disposable target is the Cheat Engine host itself.";
			return false;
		}

		if (!TryGetProcessImage(manifest.TargetProcessId, out targetPath, out string targetFailure))
		{
			failure = "The declared disposable target cannot be inspected: " + targetFailure;
			return false;
		}

		if (!TryHash(targetPath, out targetHash, out string targetHashFailure))
		{
			failure = "The declared disposable target cannot be hashed: " + targetHashFailure;
			return false;
		}

		if (!string.Equals(targetHash, manifest.TargetSha256, StringComparison.Ordinal))
		{
			failure = "The declared target SHA-256 differs from its live process image.";
			return false;
		}

		return true;
	}

	private static bool TryReadManifest(string path, out AuthorizationManifest manifest, out string failure)
	{
		manifest = default;
		failure = "The authorization manifest is invalid.";
		try
		{
			string fullPath = Path.GetFullPath(path);
			if (!File.Exists(fullPath))
			{
				failure = "The authorization manifest does not exist.";
				return false;
			}

			using FileStream stream = File.OpenRead(fullPath);
			using JsonDocument document = JsonDocument.Parse(stream);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !TryString(root, "schema", out string schema) ||
			    !string.Equals(schema, "ce77-live-probe-v1", StringComparison.Ordinal) ||
			    !TryString(root, "acknowledgement", out string acknowledgement) ||
			    !TryString(root, "hostSha256", out string hostSha256) ||
			    !TryString(root, "targetSha256", out string targetSha256) ||
			    !TryString(root, "expiresUtc", out string expiresText) ||
			    !root.TryGetProperty("targetProcessId", out JsonElement pid) ||
			    !pid.TryGetInt32(out int targetProcessId) ||
			    !root.TryGetProperty("disposable", out JsonElement disposable) ||
			    disposable.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
			    !DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
				    out DateTimeOffset expiresUtc))
			{
				failure = "The authorization manifest is missing a required ce77-live-probe-v1 field.";
				return false;
			}

			manifest = new AuthorizationManifest(acknowledgement, NormalizeHash(hostSha256), targetProcessId,
				NormalizeHash(targetSha256), disposable.GetBoolean(), expiresUtc);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
			                                  or ArgumentException)
		{
			failure = exception.GetType().Name + ": " + exception.Message;
			return false;
		}
	}

	private static bool TryString(JsonElement parent, string name, out string value)
	{
		value = string.Empty;
		if (!parent.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		value = property.GetString() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(value);
	}

	private static bool TryGetProcessImage(int processId, out string path, out string failure)
	{
		path = string.Empty;
		failure = string.Empty;
		try
		{
			using Process process = Process.GetProcessById(processId);
			path = process.MainModule?.FileName ?? string.Empty;
			if (path.Length == 0)
			{
				failure = "MainModule.FileName is empty.";
				return false;
			}

			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
			                                  or NotSupportedException or Win32Exception)
		{
			failure = exception.GetType().Name + ": " + exception.Message;
			return false;
		}
	}

	private static bool IsAmd64Pe(string path, out string failure)
	{
		failure = string.Empty;
		try
		{
			using FileStream stream = File.OpenRead(path);
			using PEReader reader = new(stream);
			if (!reader.HasMetadata && reader.PEHeaders.PEHeader is null)
			{
				failure = "The file is not a PE image.";
				return false;
			}

			if (reader.PEHeaders.CoffHeader.Machine == Machine.Amd64)
			{
				return true;
			}

			failure = "COFF machine is " + reader.PEHeaders.CoffHeader.Machine + ".";
			return false;
		}
		catch (Exception exception) when (exception is BadImageFormatException or IOException
			                                  or UnauthorizedAccessException)
		{
			failure = exception.GetType().Name + ": " + exception.Message;
			return false;
		}
	}

	private static bool TryHash(string path, out string sha256, out string failure)
	{
		sha256 = string.Empty;
		failure = string.Empty;
		try
		{
			using FileStream stream = File.OpenRead(path);
			sha256 = Convert.ToHexString(SHA256.HashData(stream));
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			failure = exception.GetType().Name + ": " + exception.Message;
			return false;
		}
	}

	private static string NormalizeHash(string hash)
	{
		return hash.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
	}

	private readonly record struct AuthorizationManifest(
		string Acknowledgement,
		string HostSha256,
		int TargetProcessId,
		string TargetSha256,
		bool Disposable,
		DateTimeOffset ExpiresUtc);
}

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
		Justification = "Authorization evaluation keeps all fail-closed checks and diagnostics in one auditable sequence.")]
	internal static AuthorizationDecision Evaluate()
	{
		if (IntPtr.Size != 8)
		{
			return AuthorizationDecision.Denied("The current process is not x64.");
		}

		string? acknowledgement = Environment.GetEnvironmentVariable(AcknowledgementVariable);
		if (!string.Equals(acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The explicit CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT phrase is absent.");
		}

		string? manifestPath = Environment.GetEnvironmentVariable(ManifestVariable);
		if (string.IsNullOrWhiteSpace(manifestPath))
		{
			return AuthorizationDecision.Denied("CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE is absent.");
		}

		if (!TryReadManifest(manifestPath, out AuthorizationManifest manifest, out string manifestFailure))
		{
			return AuthorizationDecision.Denied(manifestFailure);
		}

		if (!string.Equals(manifest.Acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The authorization manifest has no matching acknowledgement.");
		}

		if (!manifest.Disposable)
		{
			return AuthorizationDecision.Denied("The authorization manifest does not mark the target disposable.");
		}

		if (manifest.ExpiresUtc <= DateTimeOffset.UtcNow)
		{
			return AuthorizationDecision.Denied("The authorization manifest has expired.");
		}

		if (!TryGetProcessImage(Environment.ProcessId, out string hostPath, out string hostFailure))
		{
			return AuthorizationDecision.Denied("The CE host image cannot be inspected: " + hostFailure);
		}

		if (!IsAmd64Pe(hostPath, out string hostArchitectureFailure))
		{
			return AuthorizationDecision.Denied("The CE host image is not an AMD64 PE: " + hostArchitectureFailure);
		}

		if (!TryHash(hostPath, out string hostHash, out string hostHashFailure))
		{
			return AuthorizationDecision.Denied("The CE host image cannot be hashed: " + hostHashFailure);
		}

		if (!string.Equals(hostHash, ExactCheatEngineSha256, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The host SHA-256 is not the pinned CE 7.7.0.10621 x64 binary.");
		}

		if (!string.Equals(manifest.HostSha256, ExactCheatEngineSha256, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The manifest does not pin the CE 7.7.0.10621 x64 SHA-256.");
		}

		string? hostVersion = FileVersionInfo.GetVersionInfo(hostPath).FileVersion;
		if (!string.Equals(hostVersion, ExactCheatEngineFileVersion, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The pinned CE executable has an unexpected file version: " +
			                                    hostVersion + ".");
		}

		if (manifest.TargetProcessId == Environment.ProcessId)
		{
			return AuthorizationDecision.Denied("The declared disposable target is the Cheat Engine host itself.");
		}

		if (!TryGetProcessImage(manifest.TargetProcessId, out string targetPath, out string targetFailure))
		{
			return AuthorizationDecision.Denied("The declared disposable target cannot be inspected: " + targetFailure);
		}

		if (!TryHash(targetPath, out string targetHash, out string targetHashFailure))
		{
			return AuthorizationDecision.Denied("The declared disposable target cannot be hashed: " +
			                                    targetHashFailure);
		}

		if (!string.Equals(targetHash, manifest.TargetSha256, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied("The declared target SHA-256 differs from its live process image.");
		}

		return AuthorizationDecision.Allowed(hostPath, hostHash, manifest.TargetProcessId, targetPath, targetHash,
			manifest.ExpiresUtc);
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

using System.Diagnostics;
using System.Text.Json;

using LiveProbe;

namespace CheatEngine.SDK.LiveProbe.Tests;

public sealed class HostProfileObservationTests
{
	[Fact]
	public void InspectFileForIdentity_when_path_is_empty_reports_not_observed_without_opening()
	{
		bool wasOpened = false;

		string outcome = HostProfileObservation.InspectFileForIdentity("   ", _ =>
		{
			wasOpened = true;
			return new MemoryStream();
		});

		Assert.Equal("not-observed", outcome);
		Assert.False(wasOpened);
	}

	[Fact]
	public void InspectFileForIdentity_when_path_is_absent_reports_file_not_found()
	{
		string outcome = HostProfileObservation.InspectFileForIdentity("missing.dll",
			static _ => throw new FileNotFoundException());

		Assert.Equal("file-not-found", outcome);
	}

	[Fact]
	public void InspectFileForIdentity_when_parent_directory_is_absent_reports_file_not_found()
	{
		string outcome = HostProfileObservation.InspectFileForIdentity("missing\\child.dll",
			static _ => throw new DirectoryNotFoundException());

		Assert.Equal("file-not-found", outcome);
	}

	[Fact]
	public void InspectFileForIdentity_when_access_is_denied_reports_typed_unavailable()
	{
		string outcome = HostProfileObservation.InspectFileForIdentity("protected.dll",
			static _ => throw new UnauthorizedAccessException());

		Assert.Equal("unavailable: UnauthorizedAccessException", outcome);
	}

	[Fact]
	public void InspectFileForIdentity_when_inspection_throws_io_exception_reports_typed_unavailable()
	{
		string outcome = HostProfileObservation.InspectFileForIdentity("locked.dll",
			static _ => throw new IOException("Synthetic sharing failure."));

		Assert.Equal("unavailable: IOException", outcome);
	}

	[Fact]
	public void ObserveFileVersion_when_file_is_removed_after_open_reports_typed_unavailable()
	{
		string outcome = HostProfileObservation.ObserveFileVersion("removed.dll",
			static _ => throw new FileNotFoundException());

		Assert.Equal("unavailable: FileNotFoundException", outcome);
	}

	[Fact]
	public void ObserveFileVersion_when_access_is_revoked_after_open_reports_typed_unavailable()
	{
		string outcome = HostProfileObservation.ObserveFileVersion("protected.dll",
			static _ => throw new UnauthorizedAccessException());

		Assert.Equal("unavailable: UnauthorizedAccessException", outcome);
	}

	[Fact]
	public void Host_profile_records_the_loaded_bridge_module_and_never_a_file_next_to_the_application()
	{
		// The test output folder holds a cheatengine-sdk-lua-bridge.dll that this process does not need to load. The
		// record may name a bridge only when the process loaded it; modules stay loaded, so a module listed after the
		// capture was loaded when the record named it.
		string record = HostProfileObservation.Capture(AuthorizationDecision.Denied("test"));
		using JsonDocument document = JsonDocument.Parse(record);
		JsonElement bridge = document.RootElement.GetProperty("bridge");
		string[] loaded = LoadedModulePaths("cheatengine-sdk-lua-bridge.dll");

		if (bridge.TryGetProperty("path", out JsonElement path))
		{
			Assert.Contains(path.GetString()!, loaded, StringComparer.OrdinalIgnoreCase);
		}
		else
		{
			Assert.Equal("not-observed", bridge.GetProperty("outcome").GetString());
		}
	}

	private static string[] LoadedModulePaths(string fileName)
	{
		using Process process = Process.GetCurrentProcess();
		List<string> paths = [];
		foreach (ProcessModule module in process.Modules)
		{
			if (string.Equals(Path.GetFileName(module.FileName), fileName, StringComparison.OrdinalIgnoreCase))
			{
				paths.Add(module.FileName);
			}
		}

		return [.. paths];
	}
}

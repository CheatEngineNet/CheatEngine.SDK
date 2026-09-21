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
}

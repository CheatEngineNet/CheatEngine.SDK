namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>A uniquely named folder under the temporary directory, deleted on dispose.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
	public TemporaryDirectory()
	{
		Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cesdk-governance-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path);
	}

	/// <summary>The absolute path of the folder.</summary>
	public string Path
	{
		get;
	}

	/// <summary>The absolute path of a file inside the folder.</summary>
	public string File(string name)
	{
		return System.IO.Path.Combine(Path, name);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Path, recursive: true);
		}
		catch (IOException)
		{
			// A virus scanner or a just-exited child can hold a file for a moment; the folder lives under TEMP.
		}
		catch (UnauthorizedAccessException)
		{
			// Same as above.
		}
	}
}

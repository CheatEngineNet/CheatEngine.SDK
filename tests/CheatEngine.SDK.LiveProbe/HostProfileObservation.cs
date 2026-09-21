using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Lua.Interop.Loading;

namespace LiveProbe;

// This is evidence capture, not a runtime capability detector. The command that calls it is authorization-gated and
// emits a self-contained JSON record for the operator to retain outside the repository.
internal static class HostProfileObservation
{
    private const string BridgeFileName = "cheatengine-sdk-lua-bridge.dll";

    internal static string Capture(AuthorizationDecision authorization)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "ce77-live-host-profile-v1");
            writer.WriteString("capturedAtUtc", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("catalogRevision", "ce-7.7.0.10621-x64-source-index");

            writer.WriteStartObject("authorization");
            writer.WriteBoolean("allowed", authorization.IsAllowed);
            writer.WriteString("outcome", authorization.Reason);
            writer.WriteString("expiresUtc", authorization.ExpiresUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteEndObject();

            WriteFileIdentity(writer, "host", authorization.HostPath, authorization.HostSha256);
            WriteFileIdentity(writer, "lua", FindLoadedModulePath(LuaModule.CheatEngine64ModuleName), null);
            WriteFileIdentity(writer, "bridge", Path.Combine(AppContext.BaseDirectory, BridgeFileName), null);
            WriteFileIdentity(writer, "plugin", typeof(HostProfileObservation).Assembly.Location, null);

            writer.WriteStartObject("target");
            writer.WriteNumber("processId", authorization.TargetProcessId);
            WriteFileIdentityFields(writer, authorization.TargetPath, authorization.TargetSha256);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteFileIdentity(Utf8JsonWriter writer, string name, string? path, string? knownHash)
    {
        writer.WriteStartObject(name);
        WriteFileIdentityFields(writer, path, knownHash);
        writer.WriteEndObject();
    }

    private static void WriteFileIdentityFields(Utf8JsonWriter writer, string? path, string? knownHash)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            writer.WriteString("outcome", "not-observed");
            return;
        }

        writer.WriteString("path", path);
        if (!File.Exists(path))
        {
            writer.WriteString("outcome", "file-not-found");
            return;
        }

        writer.WriteString("outcome", "observed");
        writer.WriteString("sha256", knownHash ?? HashFile(path));
        try
        {
            writer.WriteString("fileVersion", FileVersionInfo.GetVersionInfo(path).FileVersion ?? "not-present");
        }
        catch (Exception exception) when (exception is ArgumentException or System.ComponentModel.Win32Exception)
        {
            writer.WriteString("fileVersion", "unavailable: " + exception.GetType().Name);
        }

        try
        {
            using var file = File.OpenRead(path);
            using var reader = new PEReader(file);
            writer.WriteString("machine", reader.PEHeaders.CoffHeader.Machine.ToString());
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            writer.WriteString("machine", "unavailable: " + exception.GetType().Name);
        }
    }

    private static string? FindLoadedModulePath(string moduleName)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(Path.GetFileName(module.FileName), moduleName, StringComparison.OrdinalIgnoreCase))
                    return module.FileName;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            HostLog.Write(CheatEngine.SDK.Hosting.Diagnostics.HostLogLevel.Warning,
                "CE 7.7 host-profile probe could not enumerate loaded modules.", exception);
        }

        return null;
    }

    private static string HashFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unavailable: " + exception.GetType().Name;
        }
    }
}

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>How Cheat Engine reaches its selected target: a local process, a file opened as a process, or CEServer.</summary>
/// <remarks>
///     <para>
///         The backends produce different evidence and are separate profiles (audit ADR-11, A12-03). Only
///         <see cref="LocalProcess" /> is qualified by the support profile <c>ce-7.7.0.10621-x64-managed-hostfxr</c>
///         (<c>qualifiedBackends</c>); the member names equal that vocabulary. The SDK recognises the backend as follows:
///     </para>
///     <list type="table">
///         <item>
///             <term><see cref="LocalProcess" /></term>
///             <description><c>isConnectedToCEServer()</c> returned <see langword="false" /> for a selected process identifier.</description>
///         </item>
///         <item>
///             <term><see cref="FileAsProcess" /></term>
///             <description>
///                 <c>getOpenedProcessID()</c> returned the file-as-process sentinel 4294967295 (CE source ec45d5f,
///                 ObservedSource; not yet observed on the 7.7 binary).
///             </description>
///         </item>
///         <item>
///             <term><see cref="CEServer" /></term>
///             <description><c>isConnectedToCEServer()</c> returned <see langword="true" />.</description>
///         </item>
///         <item>
///             <term><see cref="Unknown" /></term>
///             <description>The <c>isConnectedToCEServer</c> global is absent, so the backend cannot be established.</description>
///         </item>
///     </list>
/// </remarks>
public enum TargetBackend : byte
{
	/// <summary>The backend could not be established.</summary>
	Unknown = 0,

	/// <summary>A local operating-system process; the only qualified backend.</summary>
	LocalProcess = 1,

	/// <summary>A file opened as a process with <c>openFileAsProcess</c>; it has no operating-system process.</summary>
	FileAsProcess = 2,

	/// <summary>A target served remotely by CEServer; a local process identifier and creation time do not describe it.</summary>
	CEServer = 3
}

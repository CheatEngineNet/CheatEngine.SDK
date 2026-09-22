using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The reason the package exists at all: a plugin project that only adds
///     <c>PackageReference Include="CheatEngine.SDK"</c> gets the host-mandated <c>CESDK.CESDK.CEPluginInitialize</c>
///     entry point for free. A project that opts out with <c>CheatEngineSdkGenerateEntryPoint=false</c> owns and must
///     provide that exact bootstrap itself; a duplicate declaration would fail if the generator ignored the switch.
///     This proves that the <c>CompilerVisibleProperty</c> declared in the packaged <c>build/CheatEngine.SDK.props</c>
///     really reaches the generator, not just its own internal default.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class EntryPointTests(PackagedUmbrellaFixture fixture)
{
	[Fact]
	public void Default_consumer_gets_the_generated_entry_point_type()
	{
		Assert.True(fixture.DefaultEntryPointTypeExists,
			"CESDK.CESDK was not found in the default consumer's built assembly.");
	}

	[Fact]
	public void Default_consumer_entry_point_declares_CEPluginInitialize()
	{
		Assert.True(fixture.DefaultEntryPointMethodExists,
			"CESDK.CESDK.CEPluginInitialize(object, object) was not found in the default consumer's built assembly.");
	}

	[Fact]
	public void CheatEngineSdkGenerateEntryPoint_false_accepts_the_manual_bootstrap()
	{
		Assert.True(fixture.EntryPointOffTypeExists,
			"The manual CESDK.CESDK bootstrap was not found in the opted-out consumer.");
		Assert.True(fixture.EntryPointOffMethodExists,
			"The manual CESDK.CESDK bootstrap did not declare CEPluginInitialize(System.IntPtr, int).");
	}

	[Fact]
	public void Default_consumer_gets_a_loadable_native_bridge()
	{
		Assert.True(File.Exists(fixture.DefaultNativeBridgePath),
			$"The bridge was not copied to '{fixture.DefaultNativeBridgePath}'.");

		IntPtr module = NativeLibrary.Load(fixture.DefaultNativeBridgePath);
		try
		{
			Assert.True(NativeLibrary.TryGetExport(module, "cheatengine_sdk_lua_protected", out _));
			IntPtr versionAddress = NativeLibrary.GetExport(module, "cheatengine_sdk_lua_bridge_abi_version");
			BridgeVersion version = Marshal.GetDelegateForFunctionPointer<BridgeVersion>(versionAddress);
			Assert.Equal(1u, version());
			IntPtr fingerprintAddress =
				NativeLibrary.GetExport(module, "cheatengine_sdk_lua_bridge_source_fingerprint");
			Assert.False(string.IsNullOrWhiteSpace(Marshal.PtrToStringAnsi(fingerprintAddress)));
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	[Fact]
	public void Published_consumer_keeps_the_native_bridge()
	{
		Assert.True(File.Exists(fixture.DefaultPublishedNativeBridgePath),
			$"The bridge was not published to '{fixture.DefaultPublishedNativeBridgePath}'.");
		string publishDirectory = Path.GetDirectoryName(fixture.DefaultPublishedNativeBridgePath)!;
		Assert.Single(Directory.GetFiles(publishDirectory, "cheatengine-sdk-lua-bridge.dll",
			SearchOption.AllDirectories));
	}

	[Fact]
	public void Packed_direct_consumer_executes_bootstrap_with_an_opaque_second_argument()
	{
		const int recordSize = 36;
		const int opaqueArgument = 0x13579BDF;
		const byte canary = 0xA5;
		string consumerAssemblyPath = Path.Combine(fixture.DefaultDeploymentDirectory, "DefaultConsumer.dll");
		string hostingAssemblyPath = Path.Combine(fixture.DefaultDeploymentDirectory, "CheatEngine.SDK.Hosting.dll");

		PluginAssemblyLoadContext context = new(fixture.DefaultDeploymentDirectory);
		try
		{
			Assembly hostingAssembly = context.LoadFromAssemblyPath(hostingAssemblyPath);
			Assembly consumerAssembly = context.LoadFromAssemblyPath(consumerAssemblyPath);
			Type entryPointType = consumerAssembly.GetType("CESDK.CESDK", true)!;
			MethodInfo initialize =
				entryPointType.GetMethod("CEPluginInitialize", BindingFlags.Public | BindingFlags.Static)
				?? throw new MissingMethodException("CESDK.CESDK", "CEPluginInitialize");

			IntPtr record = Marshal.AllocHGlobal(recordSize + sizeof(int));
			try
			{
				byte[] initialBytes = new byte[recordSize + sizeof(int)];
				Array.Fill(initialBytes, canary);
				Marshal.Copy(initialBytes, 0, record, initialBytes.Length);

				object? result = initialize.Invoke(null, [record, opaqueArgument]);
				Assert.Equal(1, Assert.IsType<int>(result));

				Type pluginHost = hostingAssembly.GetType(
					"CheatEngine.SDK.Hosting.Bootstrap.PluginHost", true)!;
				PropertyInfo lastArgument =
					pluginHost.GetProperty("LastInitRecordArgument", BindingFlags.Public | BindingFlags.Static)
					?? throw new MissingMemberException(pluginHost.FullName, "LastInitRecordArgument");
				Assert.Equal(opaqueArgument, Assert.IsType<int>(lastArgument.GetValue(null)));

				byte[] actualBytes = new byte[recordSize + sizeof(int)];
				Marshal.Copy(record, actualBytes, 0, actualBytes.Length);
				Assert.NotEqual(0L, BitConverter.ToInt64(actualBytes, 0));
				for (int index = recordSize; index < actualBytes.Length; index++)
				{
					Assert.Equal(canary, actualBytes[index]);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(record);
			}
		}
		finally
		{
			context.Unload();
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate uint BridgeVersion();

	private sealed class PluginAssemblyLoadContext(string deploymentDirectory) : AssemblyLoadContext(true)
	{
		protected override Assembly? Load(AssemblyName assemblyName)
		{
			if (string.IsNullOrEmpty(assemblyName.Name))
			{
				return null;
			}

			string candidatePath = Path.Combine(deploymentDirectory, assemblyName.Name + ".dll");
			return File.Exists(candidatePath) ? LoadFromAssemblyPath(candidatePath) : null;
		}
	}
}

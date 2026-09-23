// SPDX-License-Identifier: MIT
//
// C2 evidence only (SDK-COEX-1, F03, Q09): a native hostfxr host that plays the two coexistence plugins' A/B protocol
// through the managed (hostfxr) load path, exactly as documented at
// https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting for the well-documented steps (nethost lookup,
// hostfxr_initialize_for_runtime_config, hostfxr_get_runtime_delegate, hdt_load_assembly_and_get_function_pointer).
// The "default ALC" route (hdt_load_assembly + hdt_get_function_pointer, with APP_PATHS set on the host context) is
// not documented on Learn; its declarations come from the installed SDK's
// Microsoft.NETCore.App.Host.win-x64 pack `hostfxr.h`/`coreclr_delegates.h`, and its behaviour here is a *measured*
// fact, never an assumption (see the README and every "measured, not asserted" comment below).
//
// This program never starts, reads, or references an installed Cheat Engine. It never claims Cheat Engine's actual
// loader behaviour: `evidence=C2-host-emulated-hostfxr-component-route-not-cheat-engine` is stamped on every run.
//
// Exit codes: 0 once the protocol ran (whatever the recorded facts say); non-zero only for an infrastructure failure
// that prevented the protocol from running at all (bad arguments, Lua load failure, hostfxr initialization failure).

#define WIN32_LEAN_AND_MEAN

#include <windows.h>
#include <bcrypt.h>

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "ce_host_emulator_abi.h"
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>

#pragma comment(lib, "bcrypt.lib")

namespace
{
	// ---------------------------------------------------------------------------------------------------------
	// String helpers
	// ---------------------------------------------------------------------------------------------------------

	std::string WideToUtf8(const std::wstring& wide)
	{
		if (wide.empty())
		{
			return std::string();
		}

		int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
		std::string result(static_cast<size_t>(size), '\0');
		WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), result.data(), size, nullptr, nullptr);
		return result;
	}

	std::string ToHex(const unsigned char* bytes, size_t count)
	{
		static const char* digits = "0123456789abcdef";
		std::string hex(count * 2, '0');
		for (size_t index = 0; index < count; ++index)
		{
			hex[index * 2] = digits[(bytes[index] >> 4) & 0xF];
			hex[index * 2 + 1] = digits[bytes[index] & 0xF];
		}

		return hex;
	}

	std::string Trim(const std::string& text)
	{
		size_t begin = text.find_first_not_of(" \t\r\n");
		if (begin == std::string::npos)
		{
			return std::string();
		}

		size_t end = text.find_last_not_of(" \t\r\n");
		return text.substr(begin, end - begin + 1);
	}

	// Redacts every absolute Windows path token ("<drive>:\..." until the next separator) down to "<file:name>".
	// ALC names of isolated component load contexts embed the component's absolute assembly path (pitfall #9 of
	// s-host.md section 5): this keeps the facts file, and any identity text copied into it, path-free.
	std::string RedactAbsolutePaths(const std::string& text)
	{
		std::string redacted;
		redacted.reserve(text.size());

		size_t position = 0;
		while (position < text.size())
		{
			bool isDriveLetter = position + 1 < text.size() && isalpha(static_cast<unsigned char>(text[position])) != 0 &&
				text[position + 1] == ':' && position + 2 < text.size() && (text[position + 2] == '\\' || text[position + 2] == '/');
			if (!isDriveLetter)
			{
				redacted += text[position];
				++position;
				continue;
			}

			size_t tokenEnd = position;
			while (tokenEnd < text.size() && text[tokenEnd] != ';' && text[tokenEnd] != ',' && text[tokenEnd] != ' ' &&
				text[tokenEnd] != '\t' && text[tokenEnd] != '(' && text[tokenEnd] != ')')
			{
				++tokenEnd;
			}

			std::string token = text.substr(position, tokenEnd - position);
			size_t lastSeparator = token.find_last_of("\\/");
			std::string fileName = lastSeparator == std::string::npos ? token : token.substr(lastSeparator + 1);
			redacted += "<file:";
			redacted += fileName;
			redacted += ">";
			position = tokenEnd;
		}

		return redacted;
	}

	// Extracts the value of "Label=" up to the next "; " (or end of string) from a CoexistenceDiagnostics identity
	// line. Returns "unavailable" when the label is not present: the emulator never fails the run over a parsing miss.
	std::string ExtractField(const std::string& text, const std::string& label)
	{
		size_t start = text.find(label);
		if (start == std::string::npos)
		{
			return "unavailable";
		}

		start += label.size();
		size_t end = text.find("; ", start);
		std::string value = end == std::string::npos ? text.substr(start) : text.substr(start, end - start);
		return Trim(value);
	}

	// ---------------------------------------------------------------------------------------------------------
	// Facts sink: an ordered key=value list, written verbatim to --facts. Every value is redacted before it is
	// added, so no absolute path can reach the file regardless of which field it came from.
	// ---------------------------------------------------------------------------------------------------------

	class FactsWriter
	{
	public:
		// A single overload only: a `const char*`/string-literal argument binds more readily to `bool` than to
		// `std::string` in C++ overload resolution (pointer-to-bool is a standard conversion; char* -> std::string is
		// a user-defined one), which would silently turn every "1"/"ok"/"failed" literal into "true". Every boolean
		// fact is spelled out at the call site instead (`AddBool`) so no call can be misresolved this way.
		void Add(const std::string& key, const std::string& value)
		{
			m_entries.emplace_back(key, RedactAbsolutePaths(value));
		}

		void AddBool(const std::string& key, bool value)
		{
			Add(key, value ? std::string("true") : std::string("false"));
		}

		bool Save(const std::wstring& path) const
		{
			std::ofstream file(path, std::ios::binary | std::ios::trunc);
			if (!file)
			{
				return false;
			}

			for (const std::pair<std::string, std::string>& entry : m_entries)
			{
				file << entry.first << "=" << entry.second << "\n";
			}

			return static_cast<bool>(file);
		}

	private:
		std::vector<std::pair<std::string, std::string>> m_entries;
	};

	// ---------------------------------------------------------------------------------------------------------
	// SHA-256 (Windows CNG, no external dependency): used for lua.sha256.
	// ---------------------------------------------------------------------------------------------------------

	std::string Sha256File(const std::wstring& path)
	{
		std::ifstream file(path, std::ios::binary);
		if (!file)
		{
			return "unavailable";
		}

		BCRYPT_ALG_HANDLE algorithm = nullptr;
		if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0)
		{
			return "unavailable";
		}

		BCRYPT_HASH_HANDLE hash = nullptr;
		std::string result = "unavailable";
		if (BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) == 0)
		{
			std::vector<unsigned char> buffer(1 << 16);
			bool hashingFailed = false;
			while (file)
			{
				file.read(reinterpret_cast<char*>(buffer.data()), static_cast<std::streamsize>(buffer.size()));
				std::streamsize read = file.gcount();
				if (read <= 0)
				{
					break;
				}

				if (BCryptHashData(hash, buffer.data(), static_cast<ULONG>(read), 0) != 0)
				{
					hashingFailed = true;
					break;
				}
			}

			if (!hashingFailed)
			{
				unsigned char digest[32];
				if (BCryptFinishHash(hash, digest, sizeof(digest), 0) == 0)
				{
					result = ToHex(digest, sizeof(digest));
				}
			}

			BCryptDestroyHash(hash);
		}

		BCryptCloseAlgorithmProvider(algorithm, 0);
		return result;
	}

	// ---------------------------------------------------------------------------------------------------------
	// Lua 5.3 C API, resolved by ordinal-free GetProcAddress from the module loaded from --lua. Deliberately not
	// `#include <lua.h>`: no Lua headers are vendored into this repository (see native/cheatengine-sdk-lua-bridge's
	// own comment on the same choice). Only the handful of entry points this protocol needs.
	// ---------------------------------------------------------------------------------------------------------

	struct lua_State;
	typedef intptr_t lua_KContext;
	typedef int(__cdecl* lua_KFunction)(lua_State*, int, lua_KContext);

	enum
	{
		CeLuaTNil = 0,
		CeLuaTFunction = 6,
	};

	struct LuaApi
	{
		lua_State* (__cdecl* newstate)() = nullptr;
		void(__cdecl* openlibs)(lua_State*) = nullptr;
		int(__cdecl* getglobal)(lua_State*, const char*) = nullptr;
		int(__cdecl* pcallk)(lua_State*, int, int, int, lua_KContext, lua_KFunction) = nullptr;
		int(__cdecl* type)(lua_State*, int) = nullptr;
		void(__cdecl* settop)(lua_State*, int) = nullptr;
		int(__cdecl* gettop)(lua_State*) = nullptr;
		long long(__cdecl* tointegerx)(lua_State*, int, int*) = nullptr;
		const char* (__cdecl* tolstring)(lua_State*, int, size_t*) = nullptr;
		void(__cdecl* close)(lua_State*) = nullptr;
		void(__cdecl* pushlightuserdata)(lua_State*, void*) = nullptr;

		bool ResolveFrom(HMODULE module)
		{
			newstate = reinterpret_cast<decltype(newstate)>(GetProcAddress(module, "luaL_newstate"));
			openlibs = reinterpret_cast<decltype(openlibs)>(GetProcAddress(module, "luaL_openlibs"));
			getglobal = reinterpret_cast<decltype(getglobal)>(GetProcAddress(module, "lua_getglobal"));
			pcallk = reinterpret_cast<decltype(pcallk)>(GetProcAddress(module, "lua_pcallk"));
			type = reinterpret_cast<decltype(type)>(GetProcAddress(module, "lua_type"));
			settop = reinterpret_cast<decltype(settop)>(GetProcAddress(module, "lua_settop"));
			gettop = reinterpret_cast<decltype(gettop)>(GetProcAddress(module, "lua_gettop"));
			tointegerx = reinterpret_cast<decltype(tointegerx)>(GetProcAddress(module, "lua_tointegerx"));
			tolstring = reinterpret_cast<decltype(tolstring)>(GetProcAddress(module, "lua_tolstring"));
			close = reinterpret_cast<decltype(close)>(GetProcAddress(module, "lua_close"));
			pushlightuserdata = reinterpret_cast<decltype(pushlightuserdata)>(GetProcAddress(module, "lua_pushlightuserdata"));
			return newstate && openlibs && getglobal && pcallk && type && settop && gettop && tointegerx && tolstring && close &&
				pushlightuserdata;
		}
	};

	LuaApi g_lua;
	lua_State* g_luaState = nullptr;

	// Reads the type of a global without leaving it on the stack.
	int GlobalType(const char* name)
	{
		int type = g_lua.getglobal(g_luaState, name);
		g_lua.settop(g_luaState, -2);
		return type;
	}

	// Calls a zero-argument global Lua function. On success, returns true and fills `resultText`/`resultInteger`
	// from the single returned value (a string is read for identity(), an integer for ping()).
	bool CallGlobalFunction(const char* name, std::string& resultText, long long& resultInteger, bool& wasNil)
	{
		int top = g_lua.gettop(g_luaState);
		int type = g_lua.getglobal(g_luaState, name);
		if (type == CeLuaTNil)
		{
			g_lua.settop(g_luaState, top);
			wasNil = true;
			return false;
		}

		wasNil = false;
		int status = g_lua.pcallk(g_luaState, 0, 1, 0, 0, nullptr);
		if (status != 0)
		{
			size_t length = 0;
			const char* message = g_lua.tolstring(g_luaState, -1, &length);
			resultText = message ? std::string(message, length) : "pcall-failed";
			g_lua.settop(g_luaState, top);
			return false;
		}

		size_t length = 0;
		const char* text = g_lua.tolstring(g_luaState, -1, &length);
		if (text)
		{
			resultText = std::string(text, length);
		}

		int isNumber = 0;
		resultInteger = g_lua.tointegerx(g_luaState, -1, &isNumber);
		g_lua.settop(g_luaState, top);
		return true;
	}

	// ---------------------------------------------------------------------------------------------------------
	// The emulator's own implementation of the five ManagedExportedFunctions slots (host -> managed direction).
	// ---------------------------------------------------------------------------------------------------------

	void* CE_STDCALL EmulatorGetLuaState()
	{
		return g_luaState;
	}

	void CE_STDCALL EmulatorLuaPushClassInstance(void* state, void* object)
	{
		if (state)
		{
			g_lua.pushlightuserdata(reinterpret_cast<lua_State*>(state), object);
		}
	}

	void CE_STDCALL EmulatorProcessMessages()
	{
		// No GUI: nothing to pump. Matches ManagedExportedFunctions.ProcessMessages's "no arguments, no result".
	}

	CeBool8 CE_STDCALL EmulatorCheckSynchronize(int32_t)
	{
		// The emulator has no worker threads queuing cross-thread work; always report "at least one call ran".
		return 1;
	}

	// LuaRegister is declared `void*` in ManagedExportedFunctions on purpose ("do not call", see the SDK's own XML
	// docs); the SDK never calls it (it registers Lua globals through the protected bridge instead). If it is ever
	// invoked here, that is itself evidence, so the stub records it rather than crashing.
	volatile LONG g_luaRegisterCalls = 0;

	void CE_STDCALL EmulatorLuaRegisterStub()
	{
		InterlockedIncrement(&g_luaRegisterCalls);
	}

	// ---------------------------------------------------------------------------------------------------------
	// hostfxr, resolved dynamically from the path nethost reports.
	// ---------------------------------------------------------------------------------------------------------

	struct HostfxrApi
	{
		hostfxr_set_error_writer_fn set_error_writer = nullptr;
		hostfxr_initialize_for_runtime_config_fn initialize_for_runtime_config = nullptr;
		hostfxr_get_runtime_delegate_fn get_runtime_delegate = nullptr;
		hostfxr_set_runtime_property_value_fn set_runtime_property_value = nullptr;
		hostfxr_close_fn close = nullptr;

		bool ResolveFrom(HMODULE module)
		{
			set_error_writer =
				reinterpret_cast<hostfxr_set_error_writer_fn>(GetProcAddress(module, "hostfxr_set_error_writer"));
			initialize_for_runtime_config = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
				GetProcAddress(module, "hostfxr_initialize_for_runtime_config"));
			get_runtime_delegate =
				reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(module, "hostfxr_get_runtime_delegate"));
			set_runtime_property_value = reinterpret_cast<hostfxr_set_runtime_property_value_fn>(
				GetProcAddress(module, "hostfxr_set_runtime_property_value"));
			close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(module, "hostfxr_close"));
			return initialize_for_runtime_config && get_runtime_delegate && close;
		}
	};

	std::wstring g_lastHostfxrError;

	void __cdecl HostfxrErrorWriter(const wchar_t* message)
	{
		g_lastHostfxrError += message;
		g_lastHostfxrError += L" ";
	}

	std::string HexResult(int32_t result)
	{
		char buffer[16];
		sprintf_s(buffer, "0x%08X", static_cast<unsigned int>(result));
		return buffer;
	}

	// ---------------------------------------------------------------------------------------------------------
	// Command-line arguments
	// ---------------------------------------------------------------------------------------------------------

	struct Arguments
	{
		std::wstring luaPath;
		std::wstring dotnetRoot;
		std::wstring runtimeConfigPath;
		std::wstring pluginADir;
		std::wstring pluginAAssembly;
		std::wstring pluginBDir;
		std::wstring pluginBAssembly;
		std::wstring alcRoute = L"component";
		std::wstring factsPath;
	};

	bool ParseArguments(int argc, wchar_t* argv[], Arguments& arguments, std::wstring& error)
	{
		auto next = [&](int& index) -> std::wstring
		{
			++index;
			return index < argc ? std::wstring(argv[index]) : std::wstring();
		};

		for (int index = 1; index < argc; ++index)
		{
			std::wstring option = argv[index];
			if (option == L"--lua")
			{
				arguments.luaPath = next(index);
			}
			else if (option == L"--dotnet-root")
			{
				arguments.dotnetRoot = next(index);
			}
			else if (option == L"--runtimeconfig")
			{
				arguments.runtimeConfigPath = next(index);
			}
			else if (option == L"--plugin-a-dir")
			{
				arguments.pluginADir = next(index);
			}
			else if (option == L"--plugin-a-assembly")
			{
				arguments.pluginAAssembly = next(index);
			}
			else if (option == L"--plugin-b-dir")
			{
				arguments.pluginBDir = next(index);
			}
			else if (option == L"--plugin-b-assembly")
			{
				arguments.pluginBAssembly = next(index);
			}
			else if (option == L"--alc")
			{
				arguments.alcRoute = next(index);
			}
			else if (option == L"--facts")
			{
				arguments.factsPath = next(index);
			}
			else
			{
				error = L"Unrecognized argument: " + option;
				return false;
			}
		}

		if (arguments.luaPath.empty() || arguments.dotnetRoot.empty() || arguments.runtimeConfigPath.empty() ||
			arguments.pluginADir.empty() || arguments.pluginAAssembly.empty() || arguments.pluginBDir.empty() ||
			arguments.pluginBAssembly.empty() || arguments.factsPath.empty())
		{
			error = L"Missing a required argument (--lua, --dotnet-root, --runtimeconfig, --plugin-a-dir, "
					L"--plugin-a-assembly, --plugin-b-dir, --plugin-b-assembly, --facts).";
			return false;
		}

		if (arguments.alcRoute != L"component" && arguments.alcRoute != L"default")
		{
			error = L"--alc must be 'component' or 'default'.";
			return false;
		}

		return true;
	}

	// ---------------------------------------------------------------------------------------------------------
	// One plugin's bootstrap + lifecycle state.
	// ---------------------------------------------------------------------------------------------------------

	struct PluginState
	{
		std::wstring label;
		std::wstring dir;
		std::wstring assemblyName;
		uint32_t pluginId = 0;

		CeComponentEntryPointFn entry = nullptr;
		unsigned char guardBuffer[64 + sizeof(CePluginInitRecord) + 64]{};
		CePluginInitRecord* record = nullptr;

		bool entryResolved = false;
		bool bootstrapFirstOk = false;
		bool bootstrapSecondOk = false;
		bool guardIntact = true;
		bool namePointerStable = false;
		bool getVersionOk = false;
		bool enabled = false;

		void* namePointerFirst = nullptr;
	};

	bool GuardIsIntact(const unsigned char* buffer, size_t recordOffset, size_t recordSize)
	{
		for (size_t index = 0; index < 64; ++index)
		{
			if (buffer[index] != 0xCD || buffer[recordOffset + recordSize + index] != 0xCD)
			{
				return false;
			}
		}

		return true;
	}

	CeManagedExportedFunctions BuildExportedFunctions()
	{
		CeManagedExportedFunctions exports{};
		exports.SizeOfExportedFunctions = static_cast<int32_t>(sizeof(CeManagedExportedFunctions));
		exports.GetLuaState = &EmulatorGetLuaState;
		exports.LuaRegister = reinterpret_cast<void*>(&EmulatorLuaRegisterStub);
		exports.LuaPushClassInstance = &EmulatorLuaPushClassInstance;
		exports.ProcessMessages = &EmulatorProcessMessages;
		exports.CheckSynchronize = &EmulatorCheckSynchronize;
		return exports;
	}

	void RunBootstrap(PluginState& plugin)
	{
		plugin.record = reinterpret_cast<CePluginInitRecord*>(plugin.guardBuffer + 64);

		if (!plugin.entryResolved)
		{
			return;
		}

		// First call ("name query").
		memset(plugin.guardBuffer, 0xCD, sizeof(plugin.guardBuffer));
		int32_t firstResult = plugin.entry(plugin.record, static_cast<int32_t>(sizeof(CePluginInitRecord)));
		plugin.bootstrapFirstOk = firstResult != 0;
		plugin.guardIntact = plugin.guardIntact && GuardIsIntact(plugin.guardBuffer, 64, sizeof(CePluginInitRecord));
		plugin.namePointerFirst = plugin.bootstrapFirstOk ? plugin.record->Name : nullptr;

		// Second call ("load").
		int32_t secondResult = plugin.entry(plugin.record, static_cast<int32_t>(sizeof(CePluginInitRecord)));
		plugin.bootstrapSecondOk = secondResult != 0;
		plugin.guardIntact = plugin.guardIntact && GuardIsIntact(plugin.guardBuffer, 64, sizeof(CePluginInitRecord));

		if (plugin.bootstrapFirstOk && plugin.bootstrapSecondOk)
		{
			plugin.namePointerStable = plugin.namePointerFirst == plugin.record->Name;
		}

		if (!plugin.bootstrapSecondOk)
		{
			return;
		}

		CePluginVersion version{};
		CeBool32 versionResult = plugin.record->GetVersion(&version, static_cast<int32_t>(sizeof(CePluginVersion)));
		plugin.getVersionOk = versionResult != 0;

		CeManagedExportedFunctions exports = BuildExportedFunctions();
		CeBool32 enableResult = plugin.record->EnablePlugin(&exports, plugin.pluginId);
		plugin.enabled = enableResult != 0;
	}

	void RecordIdentity(FactsWriter& facts, const std::string& label, bool enabled, const std::string& key)
	{
		std::string identityText;
		std::string pingText;
		long long pingValue = 0;
		bool identityNil = true;
		bool pingNil = true;

		if (enabled)
		{
			long long ignored = 0;
			bool wasNil = false;
			CallGlobalFunction(("cheatengine_sdk_coexistence_" + label + "_identity").c_str(), identityText, ignored, wasNil);
			identityNil = wasNil;
			bool pingWasNil = false;
			CallGlobalFunction(("cheatengine_sdk_coexistence_" + label + "_ping").c_str(), pingText, pingValue, pingWasNil);
			pingNil = pingWasNil;
		}

		facts.Add(key + ".plugin_mvid", identityNil ? "unavailable" : ExtractField(identityText, "PluginMvid="));
		facts.Add(key + ".hosting_mvid", identityNil ? "unavailable" : ExtractField(identityText, "HostingMvid="));
		facts.Add(key + ".plugin_alc", identityNil ? "unavailable" : ExtractField(identityText, "PluginALC="));
		facts.Add(key + ".hosting_alc", identityNil ? "unavailable" : ExtractField(identityText, "HostingALC="));
		facts.Add(key + ".same_alc", identityNil ? "unavailable" : ExtractField(identityText, "SameALC="));
		facts.Add(key + ".hosting_type_handle", identityNil ? "unavailable" : ExtractField(identityText, "HostingTypeHandle="));
		facts.Add(key + ".epoch", identityNil ? "unavailable" : ExtractField(identityText, "Epoch="));
	}
}

int wmain(int argc, wchar_t* argv[])
{
	Arguments arguments;
	std::wstring parseError;
	if (!ParseArguments(argc, argv, arguments, parseError))
	{
		fwprintf(stderr, L"native-host-emulator: %ls\n", parseError.c_str());
		return 1;
	}

	FactsWriter facts;
	facts.Add("emulator.schema", "1");
	facts.Add("evidence", "C2-host-emulated-hostfxr-component-route-not-cheat-engine");

	bool sameDirectory = _wcsicmp(arguments.pluginADir.c_str(), arguments.pluginBDir.c_str()) == 0;
	facts.Add("layout", sameDirectory ? "shared" : "separate");
	facts.Add("alc.route", WideToUtf8(arguments.alcRoute));

	// 1. Lua: resolvable by module name (LoadLibraryW with a full path also registers the module under its base
	//    name, which is what the plugin's own dependent load of "lua53-64.dll" will find).
	std::string luaSha256 = Sha256File(arguments.luaPath);
	facts.Add("lua.sha256", luaSha256);

	HMODULE luaModule = LoadLibraryW(arguments.luaPath.c_str());
	if (!luaModule || !g_lua.ResolveFrom(luaModule))
	{
		facts.Add("runtime.init", "failed:lua-load");
		facts.Save(arguments.factsPath);
		fwprintf(stderr, L"native-host-emulator: failed to load or resolve the Lua module at '%ls'.\n", arguments.luaPath.c_str());
		return 2;
	}

	g_luaState = g_lua.newstate();
	if (!g_luaState)
	{
		facts.Add("runtime.init", "failed:lua-newstate");
		facts.Save(arguments.factsPath);
		return 2;
	}

	g_lua.openlibs(g_luaState);

	// bridge.fingerprint: read once, from whichever plugin directory the caller supplied first. Both directories
	// carry an identical bridge in every scenario this emulator runs (the consumer test verifies that before
	// invoking the emulator).
	std::string bridgeFingerprint = "unavailable";
	{
		std::wstring bridgePath = arguments.pluginADir + L"\\cheatengine-sdk-lua-bridge.dll";
		HMODULE bridgeModule = LoadLibraryW(bridgePath.c_str());
		if (bridgeModule)
		{
			const char* exported =
				reinterpret_cast<const char*>(GetProcAddress(bridgeModule, "cheatengine_sdk_lua_bridge_source_fingerprint"));
			if (exported)
			{
				size_t length = strnlen_s(exported, 200);
				bridgeFingerprint = std::string(exported, length);
			}

			FreeLibrary(bridgeModule);
		}
	}
	facts.Add("bridge.fingerprint", bridgeFingerprint);

	// 2. nethost: locate hostfxr under the explicit --dotnet-root. Never consult the environment or the registry.
	get_hostfxr_parameters hostfxrParameters{};
	hostfxrParameters.size = sizeof(hostfxrParameters);
	hostfxrParameters.assembly_path = nullptr;
	hostfxrParameters.dotnet_root = arguments.dotnetRoot.c_str();

	wchar_t hostfxrPath[MAX_PATH];
	size_t hostfxrPathSize = MAX_PATH;
	int32_t hostfxrLookup = get_hostfxr_path(hostfxrPath, &hostfxrPathSize, &hostfxrParameters);
	if (hostfxrLookup != 0)
	{
		facts.Add("runtime.init", "failed:nethost-" + HexResult(hostfxrLookup));
		facts.Save(arguments.factsPath);
		return 3;
	}

	HMODULE hostfxrModule = LoadLibraryW(hostfxrPath);
	HostfxrApi hostfxr;
	if (!hostfxrModule || !hostfxr.ResolveFrom(hostfxrModule))
	{
		facts.Add("runtime.init", "failed:hostfxr-load");
		facts.Save(arguments.factsPath);
		return 3;
	}

	if (hostfxr.set_error_writer)
	{
		hostfxr.set_error_writer(&HostfxrErrorWriter);
	}

	// 3. hostfxr_initialize_for_runtime_config: one runtime for the whole process (pitfall #10 of s-host.md
	//    section 5). A missing shared framework (WindowsDesktop.App / AspNetCore.App) fails here with
	//    0x80008096; that failure is reported, never silently downgraded to a smaller runtimeconfig.
	hostfxr_handle context = nullptr;
	int32_t initResult = hostfxr.initialize_for_runtime_config(arguments.runtimeConfigPath.c_str(), nullptr, &context);
	if (initResult < 0 || context == nullptr)
	{
		facts.Add("runtime.init", "failed:" + HexResult(initResult));
		facts.Save(arguments.factsPath);
		fwprintf(stderr, L"native-host-emulator: hostfxr_initialize_for_runtime_config failed (%ls).\n",
			g_lastHostfxrError.empty() ? L"no detail" : g_lastHostfxrError.c_str());
		return 3;
	}

	facts.Add("runtime.init", "ok");

	// From here on, whatever happens next is protocol evidence, not an infrastructure failure: the run always
	// exits 0 after this point.
	bool useDefaultAlc = arguments.alcRoute == L"default";

	load_assembly_and_get_function_pointer_fn loadAssemblyAndGetFunctionPointer = nullptr;
	load_assembly_fn loadAssembly = nullptr;
	get_function_pointer_fn getFunctionPointer = nullptr;

	if (useDefaultAlc)
	{
		// Not documented on Microsoft Learn (s-host.md WI-7): APP_PATHS must be set before the first runtime
		// delegate is obtained, because that first call is what loads CoreCLR. It lets the default
		// AssemblyLoadContext resolve a component assembly's managed dependencies (CheatEngine.SDK.Hosting.dll and
		// the rest) that are not part of this host's own runtimeconfig/deps.json.
		if (hostfxr.set_runtime_property_value)
		{
			hostfxr.set_runtime_property_value(context, L"APP_PATHS", arguments.pluginADir.c_str());
		}

		hostfxr.get_runtime_delegate(context, hdt_load_assembly, reinterpret_cast<void**>(&loadAssembly));
		hostfxr.get_runtime_delegate(context, hdt_get_function_pointer, reinterpret_cast<void**>(&getFunctionPointer));
	}
	else
	{
		hostfxr.get_runtime_delegate(
			context, hdt_load_assembly_and_get_function_pointer, reinterpret_cast<void**>(&loadAssemblyAndGetFunctionPointer));
	}

	auto resolveEntry = [&](PluginState& plugin)
	{
		std::wstring assemblyPath = plugin.dir + L"\\" + plugin.assemblyName + L".dll";
		std::wstring typeName = L"CESDK.CESDK, " + plugin.assemblyName;
		const wchar_t* methodName = L"CEPluginInitialize";

		if (useDefaultAlc)
		{
			if (!loadAssembly || !getFunctionPointer)
			{
				return;
			}

			int32_t loadResult = loadAssembly(assemblyPath.c_str(), nullptr, nullptr);
			if (loadResult != 0)
			{
				// A load failure here (for example, the second assembly cannot be found) is itself protocol
				// evidence for the default-ALC scenario, not an infrastructure error: leave entryResolved false
				// and let the bootstrap facts show "failed".
				return;
			}

			int32_t getResult =
				getFunctionPointer(typeName.c_str(), methodName, nullptr, nullptr, nullptr, reinterpret_cast<void**>(&plugin.entry));
			plugin.entryResolved = getResult == 0 && plugin.entry != nullptr;
		}
		else
		{
			if (!loadAssemblyAndGetFunctionPointer)
			{
				return;
			}

			int32_t getResult = loadAssemblyAndGetFunctionPointer(
				assemblyPath.c_str(), typeName.c_str(), methodName, nullptr, nullptr, reinterpret_cast<void**>(&plugin.entry));
			plugin.entryResolved = getResult == 0 && plugin.entry != nullptr;
		}
	};

	PluginState pluginA;
	pluginA.label = L"A";
	pluginA.dir = arguments.pluginADir;
	pluginA.assemblyName = arguments.pluginAAssembly;
	pluginA.pluginId = 1;

	PluginState pluginB;
	pluginB.label = L"B";
	pluginB.dir = arguments.pluginBDir;
	pluginB.assemblyName = arguments.pluginBAssembly;
	pluginB.pluginId = 2;

	// Protocol: enable A, then B.
	resolveEntry(pluginA);
	RunBootstrap(pluginA);
	resolveEntry(pluginB);
	RunBootstrap(pluginB);

	auto boolText = [](bool ok, bool attempted)
	{
		if (!attempted)
		{
			return std::string("skipped");
		}

		return std::string(ok ? "ok" : "failed");
	};

	facts.Add("a.bootstrap.first", boolText(pluginA.bootstrapFirstOk, pluginA.entryResolved));
	facts.Add("a.bootstrap.second", boolText(pluginA.bootstrapSecondOk, pluginA.entryResolved));
	facts.Add("a.bootstrap.guard", pluginA.guardIntact ? "intact" : "corrupted");
	facts.AddBool("a.bootstrap.name_pointer_stable", pluginA.namePointerStable);
	facts.Add("a.getversion", boolText(pluginA.getVersionOk, pluginA.bootstrapSecondOk));
	facts.Add("a.enable.1", boolText(pluginA.enabled, pluginA.bootstrapSecondOk));

	facts.Add("b.bootstrap.first", boolText(pluginB.bootstrapFirstOk, pluginB.entryResolved));
	facts.Add("b.bootstrap.second", boolText(pluginB.bootstrapSecondOk, pluginB.entryResolved));
	facts.Add("b.bootstrap.guard", pluginB.guardIntact ? "intact" : "corrupted");
	facts.AddBool("b.bootstrap.name_pointer_stable", pluginB.namePointerStable);
	facts.Add("b.getversion", boolText(pluginB.getVersionOk, pluginB.bootstrapSecondOk));
	facts.Add("b.enable.1", boolText(pluginB.enabled, pluginB.bootstrapSecondOk));

	RecordIdentity(facts, "a", pluginA.enabled, "a.identity");
	RecordIdentity(facts, "b", pluginB.enabled, "b.identity");

	// ab.hosting_mvid_equal / ab.hosting_type_handle_distinct need both identity strings side by side, so the
	// identity functions are called once more here rather than threading their result out of RecordIdentity.
	std::string identityA;
	std::string identityB;
	{
		long long ignoredInt = 0;
		bool wasNil = false;
		if (pluginA.enabled)
		{
			CallGlobalFunction("cheatengine_sdk_coexistence_a_identity", identityA, ignoredInt, wasNil);
		}

		if (pluginB.enabled)
		{
			CallGlobalFunction("cheatengine_sdk_coexistence_b_identity", identityB, ignoredInt, wasNil);
		}
	}

	std::string hostingMvidA = pluginA.enabled ? ExtractField(identityA, "HostingMvid=") : "unavailable";
	std::string hostingMvidB = pluginB.enabled ? ExtractField(identityB, "HostingMvid=") : "unavailable";
	std::string typeHandleA = pluginA.enabled ? ExtractField(identityA, "HostingTypeHandle=") : "unavailable";
	std::string typeHandleB = pluginB.enabled ? ExtractField(identityB, "HostingTypeHandle=") : "unavailable";

	bool bothIdentitiesKnown = pluginA.enabled && pluginB.enabled;
	facts.Add("ab.hosting_mvid_equal", bothIdentitiesKnown ? (hostingMvidA == hostingMvidB ? "true" : "false") : "unknown");
	facts.Add("ab.hosting_type_handle_distinct",
		bothIdentitiesKnown ? (typeHandleA != typeHandleB ? "true" : "false") : "unknown");

	// Disable A; record that A's own globals go nil while B (if enabled) keeps answering.
	if (pluginA.bootstrapSecondOk)
	{
		pluginA.record->DisablePlugin();
	}

	facts.Add("after_disable_a.a_identity_type",
		GlobalType("cheatengine_sdk_coexistence_a_identity") == CeLuaTNil ? "nil" : "present");
	facts.Add("after_disable_a.a_ping_type", GlobalType("cheatengine_sdk_coexistence_a_ping") == CeLuaTNil ? "nil" : "present");

	if (pluginB.enabled)
	{
		std::string identityAfter;
		long long ignoredInt = 0;
		bool wasNil = false;
		bool called = CallGlobalFunction("cheatengine_sdk_coexistence_b_identity", identityAfter, ignoredInt, wasNil);
		facts.Add("after_disable_a.b_identity_call", wasNil ? "nil" : (called ? "ok" : "failed"));

		std::string pingText;
		long long pingValue = 0;
		bool pingWasNil = false;
		bool pingCalled = CallGlobalFunction("cheatengine_sdk_coexistence_b_ping", pingText, pingValue, pingWasNil);
		char pingBuffer[32];
		sprintf_s(pingBuffer, "%lld", pingValue);
		facts.Add("after_disable_a.b_ping", pingWasNil ? "nil" : (pingCalled ? pingBuffer : "failed"));
	}
	else
	{
		facts.Add("after_disable_a.b_identity_call", "skipped");
		facts.Add("after_disable_a.b_ping", "skipped");
	}

	// Re-enable A: same loaded assembly instance, a fresh EnablePlugin call. A new fact not in the brief's minimal
	// list (a.identity.epoch_after_reenable) is added so "answers with a new epoch" (Q05) has something concrete
	// to assert on; see the README and the final report for this documented addition.
	bool reenableAttempted = pluginA.bootstrapSecondOk;
	bool reenabled = false;
	if (reenableAttempted)
	{
		CeManagedExportedFunctions exports = BuildExportedFunctions();
		CeBool32 enableResult = pluginA.record->EnablePlugin(&exports, pluginA.pluginId);
		reenabled = enableResult != 0;
	}

	facts.Add("a.enable.2", boolText(reenabled, reenableAttempted));

	std::string epochAfterReenable = "unavailable";
	if (reenabled)
	{
		std::string identityAfterReenable;
		long long ignoredInt = 0;
		bool wasNil = false;
		CallGlobalFunction("cheatengine_sdk_coexistence_a_identity", identityAfterReenable, ignoredInt, wasNil);
		if (!wasNil)
		{
			epochAfterReenable = ExtractField(identityAfterReenable, "Epoch=");
		}
	}

	facts.Add("a.identity.epoch_after_reenable", epochAfterReenable);

	// Disable B, then A: every global should now answer nil.
	if (pluginB.bootstrapSecondOk)
	{
		pluginB.record->DisablePlugin();
	}

	if (reenabled)
	{
		pluginA.record->DisablePlugin();
	}
	else if (pluginA.bootstrapSecondOk)
	{
		pluginA.record->DisablePlugin();
	}

	bool aIdentityNil = GlobalType("cheatengine_sdk_coexistence_a_identity") == CeLuaTNil;
	bool aPingNil = GlobalType("cheatengine_sdk_coexistence_a_ping") == CeLuaTNil;
	bool bIdentityNil = GlobalType("cheatengine_sdk_coexistence_b_identity") == CeLuaTNil;
	bool bPingNil = GlobalType("cheatengine_sdk_coexistence_b_ping") == CeLuaTNil;

	facts.Add("final.a_globals", aIdentityNil && aPingNil ? "nil" : "present");
	facts.Add("final.b_globals", bIdentityNil && bPingNil ? "nil" : "present");

	facts.Add("result", "completed");

	bool saved = facts.Save(arguments.factsPath);
	if (hostfxr.close)
	{
		hostfxr.close(context);
	}

	g_lua.close(g_luaState);
	return saved ? 0 : 4;
}

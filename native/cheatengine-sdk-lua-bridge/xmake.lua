add_rules("mode.debug", "mode.release")

target("cheatengine-sdk-lua-bridge")
    local source_fingerprint = hash.sha256(path.join(os.scriptdir(), "cheatengine_sdk_lua_bridge.c")) .. ":" ..
                               hash.sha256(path.join(os.scriptdir(), "xmake.lua"))
    set_kind("shared")
    set_languages("c11")
    set_warnings("allextra", "error")
    add_defines('CHEATENGINE_SDK_LUA_BRIDGE_SOURCE_FINGERPRINT="' .. source_fingerprint .. '"')
    add_files("cheatengine_sdk_lua_bridge.c")
    set_targetdir("$(builddir)")
    set_objectdir("$(builddir)/.objs")
    set_dependir("$(builddir)/.deps")
    if is_plat("windows") then
        -- This bridge is deliberately a small Windows x64 C11 DLL. Pin the toolchain and runtime
        -- rather than accepting a caller's ambient /MD default: the plugin must not acquire a second
        -- VC runtime dependency beside Cheat Engine's host process.
        set_toolchains("msvc")
        set_policy("build.c++.msvc.runtime", "MT")
        set_runtimes("MT")
        add_cflags("/MT", {tools = "cl", force = true})
        add_shflags("/Brepro", {tools = "link", force = true})
    end

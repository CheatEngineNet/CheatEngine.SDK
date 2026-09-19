using System;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// Constants of lua.h, lauxlib.h, lualib.h and the parts of luaconf.h that are visible through the API.
// Values are those of a default 64-bit Lua 5.3 build, which is also what Cheat Engine's SDK headers declare.
public static unsafe partial class LuaApi
{
    // ---- version -------------------------------------------------------------------------------------------------

    /// <summary><c>LUA_VERSION_NUM</c>: the value <see cref="lua_version" /> points at for every 5.3.x library.</summary>
    public const int LUA_VERSION_NUM = 503;

    /// <summary>
    ///     <c>LUAL_NUMSIZES</c>: the size fingerprint <c>luaL_checkversion</c> compares,
    ///     <c>sizeof(lua_Integer) * 16 + sizeof(lua_Number)</c>.
    /// </summary>
    public const int LUAL_NUMSIZES = sizeof(lua_Integer) * 16 + sizeof(lua_Number);

    // ---- limits and pseudo-indices (luaconf.h) -------------------------------------------------------------------

    /// <summary><c>LUAI_MAXSTACK</c>: the stack size limit of a state, which also anchors the pseudo-indices.</summary>
    public const int LUAI_MAXSTACK = 1000000;

    /// <summary><c>LUAI_FIRSTPSEUDOIDX</c>: first index that does not denote a stack slot.</summary>
    public const int LUAI_FIRSTPSEUDOIDX = -LUAI_MAXSTACK - 1000;

    /// <summary>
    ///     <c>LUA_REGISTRYINDEX</c> (-1001000): pseudo-index of the registry. It is a compile-time constant of the native
    ///     library, so a library built with another <c>LUAI_MAXSTACK</c> would silently disagree; the conformance tests
    ///     check it against the real module.
    /// </summary>
    public const int LUA_REGISTRYINDEX = LUAI_FIRSTPSEUDOIDX;

    /// <summary><c>LUA_MINSTACK</c>: free stack slots a C function can rely on when it is called.</summary>
    public const int LUA_MINSTACK = 20;

    /// <summary><c>LUA_IDSIZE</c>: size of <see cref="lua_Debug.short_src" />.</summary>
    public const int LUA_IDSIZE = 60;

    /// <summary><c>LUA_MAXINTEGER</c>: largest <c>lua_Integer</c>.</summary>
    public const lua_Integer LUA_MAXINTEGER = lua_Integer.MaxValue;

    /// <summary><c>LUA_MININTEGER</c>: smallest <c>lua_Integer</c>.</summary>
    public const lua_Integer LUA_MININTEGER = lua_Integer.MinValue;

    // ---- call and reference markers ------------------------------------------------------------------------------

    /// <summary><c>LUA_MULTRET</c>: "keep all results" as the <c>nresults</c> argument of a call.</summary>
    public const int LUA_MULTRET = -1;

    /// <summary><c>LUA_NOREF</c>: a reference value that <see cref="luaL_ref" /> never returns; the "no reference" marker.</summary>
    public const int LUA_NOREF = -2;

    /// <summary><c>LUA_REFNIL</c>: what <see cref="luaL_ref" /> returns for a nil value.</summary>
    public const int LUA_REFNIL = -1;

    // ---- thread status / call results ----------------------------------------------------------------------------

    /// <summary><c>LUA_OK</c>: success.</summary>
    public const int LUA_OK = 0;

    /// <summary><c>LUA_YIELD</c>: the coroutine yielded.</summary>
    public const int LUA_YIELD = 1;

    /// <summary><c>LUA_ERRRUN</c>: runtime error.</summary>
    public const int LUA_ERRRUN = 2;

    /// <summary><c>LUA_ERRSYNTAX</c>: syntax error while loading a chunk.</summary>
    public const int LUA_ERRSYNTAX = 3;

    /// <summary><c>LUA_ERRMEM</c>: allocation failure; the message handler is not called.</summary>
    public const int LUA_ERRMEM = 4;

    /// <summary><c>LUA_ERRGCMM</c>: error inside a <c>__gc</c> metamethod.</summary>
    public const int LUA_ERRGCMM = 5;

    /// <summary><c>LUA_ERRERR</c>: error inside the message handler.</summary>
    public const int LUA_ERRERR = 6;

    /// <summary><c>LUA_ERRFILE</c> (lauxlib): the file of <see cref="luaL_loadfilex" /> could not be opened or read.</summary>
    public const int LUA_ERRFILE = LUA_ERRERR + 1;

    // ---- type tags -----------------------------------------------------------------------------------------------

    /// <summary><c>LUA_TNONE</c>: the index is not a valid (but acceptable) stack slot.</summary>
    public const int LUA_TNONE = -1;

    /// <summary><c>LUA_TNIL</c>.</summary>
    public const int LUA_TNIL = 0;

    /// <summary><c>LUA_TBOOLEAN</c>.</summary>
    public const int LUA_TBOOLEAN = 1;

    /// <summary><c>LUA_TLIGHTUSERDATA</c>: a bare pointer value.</summary>
    public const int LUA_TLIGHTUSERDATA = 2;

    /// <summary><c>LUA_TNUMBER</c>: integer or float subtype, see <see cref="lua_isinteger" />.</summary>
    public const int LUA_TNUMBER = 3;

    /// <summary><c>LUA_TSTRING</c>.</summary>
    public const int LUA_TSTRING = 4;

    /// <summary><c>LUA_TTABLE</c>.</summary>
    public const int LUA_TTABLE = 5;

    /// <summary><c>LUA_TFUNCTION</c>: Lua or C function.</summary>
    public const int LUA_TFUNCTION = 6;

    /// <summary><c>LUA_TUSERDATA</c>: full userdata (a block of memory owned by Lua). Cheat Engine objects are of this type.</summary>
    public const int LUA_TUSERDATA = 7;

    /// <summary><c>LUA_TTHREAD</c>: coroutine.</summary>
    public const int LUA_TTHREAD = 8;

    /// <summary><c>LUA_NUMTAGS</c>: number of type tags.</summary>
    public const int LUA_NUMTAGS = 9;

    // ---- registry slots ------------------------------------------------------------------------------------------

    /// <summary><c>LUA_RIDX_MAINTHREAD</c>: integer key of the main thread in the registry.</summary>
    public const int LUA_RIDX_MAINTHREAD = 1;

    /// <summary><c>LUA_RIDX_GLOBALS</c>: integer key of the globals table in the registry.</summary>
    public const int LUA_RIDX_GLOBALS = 2;

    /// <summary><c>LUA_RIDX_LAST</c>: last predefined registry key; <see cref="luaL_ref" /> hands out larger ones.</summary>
    public const int LUA_RIDX_LAST = LUA_RIDX_GLOBALS;

    // ---- lua_arith operators -------------------------------------------------------------------------------------

    /// <summary><c>LUA_OPADD</c>: <c>+</c>.</summary>
    public const int LUA_OPADD = 0;

    /// <summary><c>LUA_OPSUB</c>: binary <c>-</c>.</summary>
    public const int LUA_OPSUB = 1;

    /// <summary><c>LUA_OPMUL</c>: <c>*</c>.</summary>
    public const int LUA_OPMUL = 2;

    /// <summary><c>LUA_OPMOD</c>: <c>%</c>.</summary>
    public const int LUA_OPMOD = 3;

    /// <summary><c>LUA_OPPOW</c>: <c>^</c>.</summary>
    public const int LUA_OPPOW = 4;

    /// <summary><c>LUA_OPDIV</c>: float division <c>/</c>.</summary>
    public const int LUA_OPDIV = 5;

    /// <summary><c>LUA_OPIDIV</c>: floor division <c>//</c>.</summary>
    public const int LUA_OPIDIV = 6;

    /// <summary><c>LUA_OPBAND</c>: bitwise and.</summary>
    public const int LUA_OPBAND = 7;

    /// <summary><c>LUA_OPBOR</c>: bitwise or.</summary>
    public const int LUA_OPBOR = 8;

    /// <summary><c>LUA_OPBXOR</c>: bitwise exclusive or.</summary>
    public const int LUA_OPBXOR = 9;

    /// <summary><c>LUA_OPSHL</c>: shift left.</summary>
    public const int LUA_OPSHL = 10;

    /// <summary><c>LUA_OPSHR</c>: shift right.</summary>
    public const int LUA_OPSHR = 11;

    /// <summary><c>LUA_OPUNM</c>: unary minus (one operand).</summary>
    public const int LUA_OPUNM = 12;

    /// <summary><c>LUA_OPBNOT</c>: bitwise not (one operand).</summary>
    public const int LUA_OPBNOT = 13;

    // ---- lua_compare operators -----------------------------------------------------------------------------------

    /// <summary><c>LUA_OPEQ</c>: <c>==</c>.</summary>
    public const int LUA_OPEQ = 0;

    /// <summary><c>LUA_OPLT</c>: <c>&lt;</c>.</summary>
    public const int LUA_OPLT = 1;

    /// <summary><c>LUA_OPLE</c>: <c>&lt;=</c>.</summary>
    public const int LUA_OPLE = 2;

    // ---- lua_gc commands -----------------------------------------------------------------------------------------

    /// <summary><c>LUA_GCSTOP</c>: stop the collector.</summary>
    public const int LUA_GCSTOP = 0;

    /// <summary><c>LUA_GCRESTART</c>: restart the collector.</summary>
    public const int LUA_GCRESTART = 1;

    /// <summary><c>LUA_GCCOLLECT</c>: run a full cycle.</summary>
    public const int LUA_GCCOLLECT = 2;

    /// <summary><c>LUA_GCCOUNT</c>: memory in use, in KiB.</summary>
    public const int LUA_GCCOUNT = 3;

    /// <summary><c>LUA_GCCOUNTB</c>: remainder of the memory in use, in bytes (0..1023).</summary>
    public const int LUA_GCCOUNTB = 4;

    /// <summary><c>LUA_GCSTEP</c>: run one incremental step.</summary>
    public const int LUA_GCSTEP = 5;

    /// <summary><c>LUA_GCSETPAUSE</c>: set the pause, returns the previous value.</summary>
    public const int LUA_GCSETPAUSE = 6;

    /// <summary><c>LUA_GCSETSTEPMUL</c>: set the step multiplier, returns the previous value.</summary>
    public const int LUA_GCSETSTEPMUL = 7;

    /// <summary><c>LUA_GCISRUNNING</c>: whether the collector is running (there is no command 8 in 5.3).</summary>
    public const int LUA_GCISRUNNING = 9;

    // ---- debug hooks ---------------------------------------------------------------------------------------------

    /// <summary><c>LUA_HOOKCALL</c>: event code, a function is being called.</summary>
    public const int LUA_HOOKCALL = 0;

    /// <summary><c>LUA_HOOKRET</c>: event code, a function is returning.</summary>
    public const int LUA_HOOKRET = 1;

    /// <summary><c>LUA_HOOKLINE</c>: event code, a new line is about to run.</summary>
    public const int LUA_HOOKLINE = 2;

    /// <summary><c>LUA_HOOKCOUNT</c>: event code, the instruction count elapsed.</summary>
    public const int LUA_HOOKCOUNT = 3;

    /// <summary><c>LUA_HOOKTAILCALL</c>: event code, a function is being tail-called.</summary>
    public const int LUA_HOOKTAILCALL = 4;

    /// <summary><c>LUA_MASKCALL</c>: hook mask bit for call events.</summary>
    public const int LUA_MASKCALL = 1 << LUA_HOOKCALL;

    /// <summary><c>LUA_MASKRET</c>: hook mask bit for return events.</summary>
    public const int LUA_MASKRET = 1 << LUA_HOOKRET;

    /// <summary><c>LUA_MASKLINE</c>: hook mask bit for line events.</summary>
    public const int LUA_MASKLINE = 1 << LUA_HOOKLINE;

    /// <summary><c>LUA_MASKCOUNT</c>: hook mask bit for count events.</summary>
    public const int LUA_MASKCOUNT = 1 << LUA_HOOKCOUNT;

    /// <summary><c>LUA_EXTRASPACE</c>: bytes of user scratch memory in front of every <see cref="lua_State" /> (one pointer).</summary>
    public static int LUA_EXTRASPACE => sizeof(void*);

    // ---- byte-string constants (NUL-terminated static data, usable directly as const char*) ------------------------

    /// <summary><c>LUA_SIGNATURE</c>: first bytes of a precompiled chunk.</summary>
    public static ReadOnlySpan<byte> LUA_SIGNATURE => "\u001BLua"u8;

    /// <summary><c>LUA_COLIBNAME</c>: module name of the coroutine library.</summary>
    public static ReadOnlySpan<byte> LUA_COLIBNAME => "coroutine"u8;

    /// <summary><c>LUA_TABLIBNAME</c>: module name of the table library.</summary>
    public static ReadOnlySpan<byte> LUA_TABLIBNAME => "table"u8;

    /// <summary><c>LUA_IOLIBNAME</c>: module name of the I/O library.</summary>
    public static ReadOnlySpan<byte> LUA_IOLIBNAME => "io"u8;

    /// <summary><c>LUA_OSLIBNAME</c>: module name of the operating system library.</summary>
    public static ReadOnlySpan<byte> LUA_OSLIBNAME => "os"u8;

    /// <summary><c>LUA_STRLIBNAME</c>: module name of the string library.</summary>
    public static ReadOnlySpan<byte> LUA_STRLIBNAME => "string"u8;

    /// <summary><c>LUA_UTF8LIBNAME</c>: module name of the UTF-8 library.</summary>
    public static ReadOnlySpan<byte> LUA_UTF8LIBNAME => "utf8"u8;

    /// <summary><c>LUA_MATHLIBNAME</c>: module name of the math library.</summary>
    public static ReadOnlySpan<byte> LUA_MATHLIBNAME => "math"u8;

    /// <summary><c>LUA_DBLIBNAME</c>: module name of the debug library.</summary>
    public static ReadOnlySpan<byte> LUA_DBLIBNAME => "debug"u8;

    /// <summary><c>LUA_LOADLIBNAME</c>: module name of the package library.</summary>
    public static ReadOnlySpan<byte> LUA_LOADLIBNAME => "package"u8;
}

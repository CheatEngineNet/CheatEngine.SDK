#include <limits.h>
#include <stddef.h>
#include <stdint.h>

#if !defined(_WIN32) || !defined(_WIN64)
#error The Lua protection bridge supports Windows x64 only.
#endif

#ifndef CHEATENGINE_SDK_LUA_BRIDGE_SOURCE_FINGERPRINT
#error The bridge source fingerprint must be supplied by the reproducible build.
#endif

#define CHEATENGINE_SDK_EXPORT __declspec(dllexport)
#define CHEATENGINE_SDK_CALL __cdecl
#define CHEATENGINE_SDK_HOST_CALL __stdcall
#define CHEATENGINE_SDK_THREAD_LOCAL __declspec(thread)

/* This file deliberately does not include lua.h. The host owns Lua; callers provide
 * addresses resolved from that host module, so the bridge can never load a second Lua. */
typedef struct lua_State lua_State;
typedef int (CHEATENGINE_SDK_CALL *lua_CFunction)(lua_State *);
typedef int64_t lua_Integer;
typedef intptr_t lua_KContext;
typedef int (CHEATENGINE_SDK_CALL *lua_KFunction)(lua_State *, int, lua_KContext);
typedef void (CHEATENGINE_SDK_HOST_CALL *cheatengine_sdk_host_object_pusher)(lua_State *, void *);
typedef struct cheatengine_sdk_lua_exports {
    int (CHEATENGINE_SDK_CALL *gettop)(lua_State *);
    void (CHEATENGINE_SDK_CALL *settop)(lua_State *, int);
    int (CHEATENGINE_SDK_CALL *checkstack)(lua_State *, int);
    void (CHEATENGINE_SDK_CALL *rotate)(lua_State *, int, int);
    const char *(CHEATENGINE_SDK_CALL *pushlstring)(lua_State *, const char *, size_t);
    void (CHEATENGINE_SDK_CALL *pushinteger)(lua_State *, lua_Integer);
    void (CHEATENGINE_SDK_CALL *createtable)(lua_State *, int, int);
    void *(CHEATENGINE_SDK_CALL *newuserdata)(lua_State *, size_t);
    void (CHEATENGINE_SDK_CALL *pushcclosure)(lua_State *, lua_CFunction, int);
    void (CHEATENGINE_SDK_CALL *pushlightuserdata)(lua_State *, void *);
    void (CHEATENGINE_SDK_CALL *rawset)(lua_State *, int);
    void (CHEATENGINE_SDK_CALL *rawseti)(lua_State *, int, lua_Integer);
    void (CHEATENGINE_SDK_CALL *rawsetp)(lua_State *, int, const void *);
    int (CHEATENGINE_SDK_CALL *rawgetp)(lua_State *, int, const void *);
    int (CHEATENGINE_SDK_CALL *rawgeti)(lua_State *, int, lua_Integer);
    int (CHEATENGINE_SDK_CALL *type)(lua_State *, int);
    int (CHEATENGINE_SDK_CALL *pcallk)(lua_State *, int, int, int, lua_KContext, lua_KFunction);
    int (CHEATENGINE_SDK_CALL *error)(lua_State *);
    int (CHEATENGINE_SDK_CALL *l_ref)(lua_State *, int);
    void (CHEATENGINE_SDK_CALL *l_unref)(lua_State *, int, int);
} cheatengine_sdk_lua_exports;

typedef struct cheatengine_sdk_lua_bridge_contract {
    uint32_t magic;
    uint32_t contract_size;
    uint64_t supported_operations;
    uint32_t export_table_size;
    uint16_t abi_major;
    uint16_t abi_minor;
    uint8_t pointer_size;
    uint8_t lua_integer_size;
    uint8_t size_t_size;
    uint8_t reserved;
    uint8_t reserved_padding[4];
} cheatengine_sdk_lua_bridge_contract;

enum { LUA_OK = 0, LUA_MULTRET = -1, LUA_TTABLE = 5, LUA_REGISTRYINDEX = -1001000, CHEATENGINE_SDK_NO_ERROR = -100 };
enum {
    OP_PUSH_BYTES = 0,
    OP_CREATE_TABLE = 1,
    OP_NEW_USERDATA = 2,
    OP_PUSH_CLOSURE = 3,
    OP_RAWSET = 4,
    OP_RAWSETI = 5,
    OP_RAWSETP = 6,
    OP_REF = 7,
    OP_PUSH_REF = 8,
    OP_UNREF = 9,
    OP_PUSH_HOST_OBJECT = 10,
    OP_PUSH_BYTE_TABLE = 11,
    CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT = 12
};
enum { CHEATENGINE_SDK_LUA_BRIDGE_LEGACY_ABI_VERSION = 1, CHEATENGINE_SDK_LUA_BRIDGE_ABI_MAJOR = 1, CHEATENGINE_SDK_LUA_BRIDGE_ABI_MINOR = 1 };
enum { CHEATENGINE_SDK_LUA_BRIDGE_MAGIC = 0x4345534B };
#define CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_MASK \
    ((UINT64_C(1) << OP_PUSH_BYTES) | (UINT64_C(1) << OP_CREATE_TABLE) | \
     (UINT64_C(1) << OP_NEW_USERDATA) | (UINT64_C(1) << OP_PUSH_CLOSURE) | \
     (UINT64_C(1) << OP_RAWSET) | (UINT64_C(1) << OP_RAWSETI) | \
     (UINT64_C(1) << OP_RAWSETP) | (UINT64_C(1) << OP_REF) | \
     (UINT64_C(1) << OP_PUSH_REF) | (UINT64_C(1) << OP_UNREF) | \
     (UINT64_C(1) << OP_PUSH_HOST_OBJECT) | (UINT64_C(1) << OP_PUSH_BYTE_TABLE))

_Static_assert(CHAR_BIT == 8, "The Lua bridge requires eight-bit bytes.");
_Static_assert(sizeof(int) == 4, "The Lua bridge ABI requires a 32-bit int.");
_Static_assert(sizeof(void *) == 8, "The Lua bridge supports Windows x64 only.");
_Static_assert(sizeof(size_t) == sizeof(void *), "size_t must be pointer-sized.");
_Static_assert(sizeof(lua_Integer) == 8, "The supported Lua build uses a 64-bit lua_Integer.");
_Static_assert(sizeof(lua_KContext) == sizeof(void *), "lua_KContext must be pointer-sized.");
_Static_assert(CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT <= 64, "The operation bitmap has no remaining bit.");
_Static_assert(sizeof(cheatengine_sdk_lua_exports) == 20 * sizeof(void *), "The Lua export table layout changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, gettop) == 0 * sizeof(void *), "lua_gettop has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, settop) == 1 * sizeof(void *), "lua_settop has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, checkstack) == 2 * sizeof(void *), "lua_checkstack has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rotate) == 3 * sizeof(void *), "lua_rotate has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, pushlstring) == 4 * sizeof(void *), "lua_pushlstring has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, pushinteger) == 5 * sizeof(void *), "lua_pushinteger has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, createtable) == 6 * sizeof(void *), "lua_createtable has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, newuserdata) == 7 * sizeof(void *), "lua_newuserdata has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, pushcclosure) == 8 * sizeof(void *), "lua_pushcclosure has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, pushlightuserdata) == 9 * sizeof(void *), "lua_pushlightuserdata has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rawset) == 10 * sizeof(void *), "lua_rawset has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rawseti) == 11 * sizeof(void *), "lua_rawseti has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rawsetp) == 12 * sizeof(void *), "lua_rawsetp has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rawgetp) == 13 * sizeof(void *), "lua_rawgetp has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, rawgeti) == 14 * sizeof(void *), "lua_rawgeti has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, type) == 15 * sizeof(void *), "lua_type has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, pcallk) == 16 * sizeof(void *), "lua_pcallk has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, error) == 17 * sizeof(void *), "lua_error has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, l_ref) == 18 * sizeof(void *), "luaL_ref has an unexpected export-table offset.");
_Static_assert(offsetof(cheatengine_sdk_lua_exports, l_unref) == 19 * sizeof(void *), "luaL_unref has an unexpected export-table offset.");
_Static_assert(sizeof(cheatengine_sdk_lua_bridge_contract) == 32, "The bridge contract layout changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, magic) == 0, "The bridge contract magic offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, contract_size) == 4, "The bridge contract size offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, supported_operations) == 8, "The bridge contract operation bitmap offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, export_table_size) == 16, "The bridge contract table-size offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, abi_major) == 20, "The bridge contract major-version offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, abi_minor) == 22, "The bridge contract minor-version offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, pointer_size) == 24, "The bridge contract pointer-size offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, lua_integer_size) == 25, "The bridge contract lua_Integer-size offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, size_t_size) == 26, "The bridge contract size_t-size offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, reserved) == 27, "The bridge contract reserved offset changed.");
_Static_assert(offsetof(cheatengine_sdk_lua_bridge_contract, reserved_padding) == 28, "The bridge contract padding offset changed.");

typedef struct call_context {
    const cheatengine_sdk_lua_exports *api;
    int op;
    int input_count;
    const void *data;
    size_t size;
    intptr_t a;
    intptr_t b;
} call_context;
static CHEATENGINE_SDK_THREAD_LOCAL call_context *s_context;

CHEATENGINE_SDK_EXPORT const char cheatengine_sdk_lua_bridge_source_fingerprint[] = CHEATENGINE_SDK_LUA_BRIDGE_SOURCE_FINGERPRINT;

CHEATENGINE_SDK_EXPORT uint32_t CHEATENGINE_SDK_CALL cheatengine_sdk_lua_bridge_abi_version(void) {
    /* Retained for already-published managed clients. New clients consume the full contract below. */
    return CHEATENGINE_SDK_LUA_BRIDGE_LEGACY_ABI_VERSION;
}

CHEATENGINE_SDK_EXPORT int CHEATENGINE_SDK_CALL cheatengine_sdk_lua_bridge_get_contract(
    cheatengine_sdk_lua_bridge_contract *contract,
    size_t contract_size) {
    const cheatengine_sdk_lua_bridge_contract value = {
        CHEATENGINE_SDK_LUA_BRIDGE_MAGIC,
        (uint32_t)sizeof(cheatengine_sdk_lua_bridge_contract),
        CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_MASK,
        (uint32_t)sizeof(cheatengine_sdk_lua_exports),
        CHEATENGINE_SDK_LUA_BRIDGE_ABI_MAJOR,
        CHEATENGINE_SDK_LUA_BRIDGE_ABI_MINOR,
        (uint8_t)sizeof(void *),
        (uint8_t)sizeof(lua_Integer),
        (uint8_t)sizeof(size_t),
        0,
        { 0 }
    };

    if (!contract || contract_size != sizeof(value)) return 0;
    *contract = value;
    return 1;
}

static int fail(lua_State *L, const char *message) {
    size_t length = 0; while (message[length]) ++length;
    s_context->api->pushlstring(L, message, length);
    return s_context->api->error(L);
}

static int lua_exports_are_complete(const cheatengine_sdk_lua_exports *api) {
    return api && api->gettop && api->settop && api->checkstack && api->rotate &&
        api->pushlstring && api->pushinteger && api->createtable && api->newuserdata &&
        api->pushcclosure && api->pushlightuserdata && api->rawset && api->rawseti &&
        api->rawsetp && api->rawgetp && api->rawgeti && api->type && api->pcallk &&
        api->error && api->l_ref && api->l_unref;
}

static int is_nonnegative_int(intptr_t value) {
    return value >= 0 && value <= INT_MAX;
}

static int is_int(intptr_t value) {
    return value >= INT_MIN && value <= INT_MAX;
}

static int CHEATENGINE_SDK_CALL operation(lua_State *L) {
    call_context *c = s_context;
    const cheatengine_sdk_lua_exports *a = c->api;

    switch (c->op) {
    case OP_PUSH_BYTES: {
        static const char empty = 0;
        if (c->input_count != 0 || (c->size != 0 && !c->data))
            return fail(L, "CheatEngine.SDK protected byte push has invalid arguments");
        a->pushlstring(L, c->size ? (const char *)c->data : &empty, c->size);
        return 1;
    }
    case OP_PUSH_BYTE_TABLE: {
        const uint8_t *bytes = (const uint8_t *)c->data;
        size_t index;
        if (c->input_count != 0 || c->size > INT_MAX || (c->size != 0 && !bytes))
            return fail(L, "CheatEngine.SDK protected byte-table push has invalid arguments");
        if (!a->checkstack(L, 2))
            return fail(L, "CheatEngine.SDK protected byte-table push could not reserve stack slots");
        a->createtable(L, (int)c->size, 0);
        for (index = 0; index < c->size; ++index) {
            a->pushinteger(L, (lua_Integer)bytes[index]);
            a->rawseti(L, -2, (lua_Integer)(index + 1));
        }
        return 1;
    }
    case OP_CREATE_TABLE:
        if (c->input_count != 0 || !is_nonnegative_int(c->a) || !is_nonnegative_int(c->b))
            return fail(L, "CheatEngine.SDK protected table creation has invalid capacities");
        a->createtable(L, (int)c->a, (int)c->b);
        return 1;
    case OP_NEW_USERDATA:
        if (c->input_count != 0)
            return fail(L, "CheatEngine.SDK protected userdata allocation has invalid inputs");
        a->newuserdata(L, c->size);
        return 1;
    case OP_PUSH_CLOSURE:
        if (!c->data || !is_nonnegative_int(c->a) || c->input_count != (int)c->a)
            return fail(L, "CheatEngine.SDK protected closure push has invalid upvalues");
        a->pushcclosure(L, (lua_CFunction)c->data, (int)c->a);
        return 1;
    /* The managed adapter supplies the target table as argument one, followed by the values to consume. */
    case OP_RAWSET:
        if (c->input_count != 3 || c->a != 1)
            return fail(L, "CheatEngine.SDK protected raw set has invalid inputs");
        a->rawset(L, 1);
        return 0;
    case OP_RAWSETI:
        if (c->input_count != 2 || c->a != 1)
            return fail(L, "CheatEngine.SDK protected indexed raw set has invalid inputs");
        a->rawseti(L, 1, (lua_Integer)c->b);
        return 0;
    case OP_RAWSETP:
        if (c->input_count != 2 || c->a != 1)
            return fail(L, "CheatEngine.SDK protected pointer raw set has invalid inputs");
        a->rawsetp(L, 1, c->data);
        return 0;
    case OP_REF: {
        /* Argument one is the value. Get/create an SDK-private registry table keyed by stableKey. */
        if (c->input_count != 1)
            return fail(L, "CheatEngine.SDK protected reference creation has invalid inputs");
        a->rawgetp(L, LUA_REGISTRYINDEX, c->data);
        if (a->type(L, -1) != LUA_TTABLE) { a->settop(L, -2); a->createtable(L, 0, 0); a->pushlightuserdata(L, (void *)c->data); a->rotate(L, -2, 1); a->rawset(L, LUA_REGISTRYINDEX); a->rawgetp(L, LUA_REGISTRYINDEX, c->data); }
        a->rotate(L, 1, 1); /* table, value */
        a->pushinteger(L, (lua_Integer)a->l_ref(L, 1));
        return 1;
    }
    case OP_PUSH_REF:
        if (c->input_count != 0)
            return fail(L, "CheatEngine.SDK protected reference push has invalid inputs");
        a->rawgetp(L, LUA_REGISTRYINDEX, c->data);
        if (a->type(L, -1) != LUA_TTABLE)
            return fail(L, "CheatEngine.SDK private reference table is unavailable");
        a->rawgeti(L, -1, (lua_Integer)c->a);
        return 1;
    case OP_UNREF:
        if (c->input_count != 0 || !is_int(c->a))
            return fail(L, "CheatEngine.SDK protected reference release has invalid inputs");
        a->rawgetp(L, LUA_REGISTRYINDEX, c->data);
        if (a->type(L, -1) != LUA_TTABLE)
            return fail(L, "CheatEngine.SDK private reference table is unavailable");
        a->l_unref(L, -1, (int)c->a);
        return 0;
    case OP_PUSH_HOST_OBJECT:
        if (c->input_count != 0 || !c->data)
            return fail(L, "CheatEngine.SDK host-object pusher is unavailable");
        ((cheatengine_sdk_host_object_pusher)c->data)(L, (void *)c->a);
        return 1;
    default:
        return fail(L, "CheatEngine.SDK protected Lua operation is invalid");
    }
}

/* Inputs already sit at top. A light C function needs no allocation, then pcall moves all
 * allocating/raising work below Lua's protected boundary. */
CHEATENGINE_SDK_EXPORT int CHEATENGINE_SDK_CALL cheatengine_sdk_lua_protected(lua_State *L, const cheatengine_sdk_lua_exports *api, int op, int input_count, const void *data, size_t size, intptr_t a, intptr_t b) {
    int top;
    call_context context;
    call_context *previous;
    int status;

    if (!L || !lua_exports_are_complete(api) || input_count < 0 || input_count == INT_MAX)
        return CHEATENGINE_SDK_NO_ERROR;

    top = api->gettop(L);
    if (top < input_count || !api->checkstack(L, 1))
        return CHEATENGINE_SDK_NO_ERROR;

    context.api = api;
    context.op = op;
    context.input_count = input_count;
    context.data = data;
    context.size = size;
    context.a = a;
    context.b = b;
    previous = s_context;
    s_context = &context;

    /* Lua 5.3 represents this zero-upvalue closure as a light C function. After the successful
     * lua_checkstack reservation above, it does not allocate; the protected operation begins at pcallk. */
    api->pushcclosure(L, operation, 0);
    if (input_count) api->rotate(L, -(input_count + 1), 1);
    status = api->pcallk(L, input_count, LUA_MULTRET, 0, 0, 0);
    s_context = previous;
    return status;
}

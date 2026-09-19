#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define CHEATENGINE_SDK_EXPORT __declspec(dllexport)
#define CHEATENGINE_SDK_CALL __cdecl
#define CHEATENGINE_SDK_THREAD_LOCAL __declspec(thread)
#else
#define CHEATENGINE_SDK_EXPORT __attribute__((visibility("default")))
#define CHEATENGINE_SDK_CALL
#define CHEATENGINE_SDK_THREAD_LOCAL _Thread_local
#endif

/* This file deliberately does not include lua.h. The host owns Lua; callers provide
 * addresses resolved from that host module, so the bridge can never load a second Lua. */
typedef struct lua_State lua_State;
typedef int (CHEATENGINE_SDK_CALL *lua_CFunction)(lua_State *);
typedef int64_t lua_Integer;
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
    int (CHEATENGINE_SDK_CALL *pcallk)(lua_State *, int, int, int, intptr_t, void *);
    int (CHEATENGINE_SDK_CALL *error)(lua_State *);
    int (CHEATENGINE_SDK_CALL *l_ref)(lua_State *, int);
    void (CHEATENGINE_SDK_CALL *l_unref)(lua_State *, int, int);
} cheatengine_sdk_lua_exports;

enum { LUA_OK = 0, LUA_MULTRET = -1, LUA_TTABLE = 5, LUA_REGISTRYINDEX = -1001000, CHEATENGINE_SDK_NO_ERROR = -100 };
enum { OP_PUSH_BYTES, OP_CREATE_TABLE, OP_NEW_USERDATA, OP_PUSH_CLOSURE, OP_RAWSET, OP_RAWSETI, OP_RAWSETP, OP_REF, OP_PUSH_REF, OP_UNREF };
enum { CHEATENGINE_SDK_LUA_BRIDGE_ABI_VERSION = 1 };
typedef struct call_context { const cheatengine_sdk_lua_exports *api; int op; const void *data; size_t size; intptr_t a; intptr_t b; } call_context;
static CHEATENGINE_SDK_THREAD_LOCAL call_context *s_context;

CHEATENGINE_SDK_EXPORT const char cheatengine_sdk_lua_bridge_source_fingerprint[] = CHEATENGINE_SDK_LUA_BRIDGE_SOURCE_FINGERPRINT;

CHEATENGINE_SDK_EXPORT uint32_t CHEATENGINE_SDK_CALL cheatengine_sdk_lua_bridge_abi_version(void) {
    return CHEATENGINE_SDK_LUA_BRIDGE_ABI_VERSION;
}

static int fail(lua_State *L, const char *message) {
    size_t length = 0; while (message[length]) ++length;
    s_context->api->pushlstring(L, message, length);
    return s_context->api->error(L);
}

static int CHEATENGINE_SDK_CALL operation(lua_State *L) {
    call_context *c = s_context; const cheatengine_sdk_lua_exports *a = c->api;
    switch (c->op) {
    case OP_PUSH_BYTES: { static const char empty = 0; a->pushlstring(L, c->size ? (const char *)c->data : &empty, c->size); return 1; }
    case OP_CREATE_TABLE: a->createtable(L, (int)c->a, (int)c->b); return 1;
    case OP_NEW_USERDATA: a->newuserdata(L, c->size); return 1;
    case OP_PUSH_CLOSURE: a->pushcclosure(L, (lua_CFunction)c->data, (int)c->a); return 1;
    /* The managed adapter supplies the target table as argument one, followed by the values to consume. */
    case OP_RAWSET: a->rawset(L, 1); return 0;
    case OP_RAWSETI: a->rawseti(L, 1, (lua_Integer)c->b); return 0;
    case OP_RAWSETP: a->rawsetp(L, 1, c->data); return 0;
    case OP_REF: {
        /* Argument one is the value. Get/create an SDK-private registry table keyed by stableKey. */
        a->rawgetp(L, LUA_REGISTRYINDEX, c->data);
        if (a->type(L, -1) != LUA_TTABLE) { a->settop(L, -2); a->createtable(L, 0, 0); a->pushlightuserdata(L, (void *)c->data); a->rotate(L, -2, 1); a->rawset(L, LUA_REGISTRYINDEX); a->rawgetp(L, LUA_REGISTRYINDEX, c->data); }
        a->rotate(L, 1, 1); /* table, value */
        a->pushinteger(L, (lua_Integer)a->l_ref(L, 1));
        return 1;
    }
    case OP_PUSH_REF: a->rawgetp(L, LUA_REGISTRYINDEX, c->data); if (a->type(L, -1) != LUA_TTABLE) return fail(L, "CheatEngine.SDK private reference table is unavailable"); a->rawgeti(L, -1, (lua_Integer)c->a); return 1;
    case OP_UNREF: a->rawgetp(L, LUA_REGISTRYINDEX, c->data); if (a->type(L, -1) != LUA_TTABLE) return fail(L, "CheatEngine.SDK private reference table is unavailable"); a->l_unref(L, -1, (int)c->a); return 0;
    default: return fail(L, "CheatEngine.SDK protected Lua operation is invalid");
    }
}

/* Inputs already sit at top. A light C function needs no allocation, then pcall moves all
 * allocating/raising work below Lua's protected boundary. */
CHEATENGINE_SDK_EXPORT int CHEATENGINE_SDK_CALL cheatengine_sdk_lua_protected(lua_State *L, const cheatengine_sdk_lua_exports *api, int op, int input_count, const void *data, size_t size, intptr_t a, intptr_t b) {
    if (!L || !api || input_count < 0 || !api->checkstack || !api->gettop || !api->pcallk || !api->checkstack(L, 1)) return CHEATENGINE_SDK_NO_ERROR;
    call_context context = { api, op, data, size, a, b }; call_context *previous = s_context; s_context = &context;
    api->pushcclosure(L, operation, 0);
    if (input_count) api->rotate(L, -(input_count + 1), 1);
    int status = api->pcallk(L, input_count, LUA_MULTRET, 0, 0, 0);
    s_context = previous;
    return status;
}

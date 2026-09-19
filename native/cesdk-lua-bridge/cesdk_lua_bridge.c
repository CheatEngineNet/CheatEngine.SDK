#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define CESDK_EXPORT __declspec(dllexport)
#define CESDK_CALL __cdecl
#define CESDK_THREAD_LOCAL __declspec(thread)
#else
#define CESDK_EXPORT __attribute__((visibility("default")))
#define CESDK_CALL
#define CESDK_THREAD_LOCAL _Thread_local
#endif

/* This file deliberately does not include lua.h. The host owns Lua; callers provide
 * addresses resolved from that host module, so the bridge can never load a second Lua. */
typedef struct lua_State lua_State;
typedef int (CESDK_CALL *lua_CFunction)(lua_State *);
typedef int64_t lua_Integer;
typedef struct cesdk_lua_exports {
    int (CESDK_CALL *gettop)(lua_State *);
    void (CESDK_CALL *settop)(lua_State *, int);
    int (CESDK_CALL *checkstack)(lua_State *, int);
    void (CESDK_CALL *rotate)(lua_State *, int, int);
    const char *(CESDK_CALL *pushlstring)(lua_State *, const char *, size_t);
    void (CESDK_CALL *pushinteger)(lua_State *, lua_Integer);
    void (CESDK_CALL *createtable)(lua_State *, int, int);
    void *(CESDK_CALL *newuserdata)(lua_State *, size_t);
    void (CESDK_CALL *pushcclosure)(lua_State *, lua_CFunction, int);
    void (CESDK_CALL *pushlightuserdata)(lua_State *, void *);
    void (CESDK_CALL *rawset)(lua_State *, int);
    void (CESDK_CALL *rawseti)(lua_State *, int, lua_Integer);
    void (CESDK_CALL *rawsetp)(lua_State *, int, const void *);
    int (CESDK_CALL *rawgetp)(lua_State *, int, const void *);
    int (CESDK_CALL *rawgeti)(lua_State *, int, lua_Integer);
    int (CESDK_CALL *type)(lua_State *, int);
    int (CESDK_CALL *pcallk)(lua_State *, int, int, int, intptr_t, void *);
    int (CESDK_CALL *error)(lua_State *);
    int (CESDK_CALL *l_ref)(lua_State *, int);
    void (CESDK_CALL *l_unref)(lua_State *, int, int);
} cesdk_lua_exports;

enum { LUA_OK = 0, LUA_MULTRET = -1, LUA_TTABLE = 5, LUA_REGISTRYINDEX = -1001000, CESDK_NO_ERROR = -100 };
enum { OP_PUSH_BYTES, OP_CREATE_TABLE, OP_NEW_USERDATA, OP_PUSH_CLOSURE, OP_RAWSET, OP_RAWSETI, OP_RAWSETP, OP_REF, OP_PUSH_REF, OP_UNREF };
enum { CESDK_LUA_BRIDGE_ABI_VERSION = 1 };
typedef struct call_context { const cesdk_lua_exports *api; int op; const void *data; size_t size; intptr_t a; intptr_t b; } call_context;
static CESDK_THREAD_LOCAL call_context *s_context;

CESDK_EXPORT const char cesdk_lua_bridge_source_fingerprint[] = CESDK_LUA_BRIDGE_SOURCE_FINGERPRINT;

CESDK_EXPORT uint32_t CESDK_CALL cesdk_lua_bridge_abi_version(void) {
    return CESDK_LUA_BRIDGE_ABI_VERSION;
}

static int fail(lua_State *L, const char *message) {
    size_t length = 0; while (message[length]) ++length;
    s_context->api->pushlstring(L, message, length);
    return s_context->api->error(L);
}

static int CESDK_CALL operation(lua_State *L) {
    call_context *c = s_context; const cesdk_lua_exports *a = c->api;
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
    case OP_PUSH_REF: a->rawgetp(L, LUA_REGISTRYINDEX, c->data); if (a->type(L, -1) != LUA_TTABLE) return fail(L, "CESDK private reference table is unavailable"); a->rawgeti(L, -1, (lua_Integer)c->a); return 1;
    case OP_UNREF: a->rawgetp(L, LUA_REGISTRYINDEX, c->data); if (a->type(L, -1) != LUA_TTABLE) return fail(L, "CESDK private reference table is unavailable"); a->l_unref(L, -1, (int)c->a); return 0;
    default: return fail(L, "CESDK protected Lua operation is invalid");
    }
}

/* Inputs already sit at top. A light C function needs no allocation, then pcall moves all
 * allocating/raising work below Lua's protected boundary. */
CESDK_EXPORT int CESDK_CALL cesdk_lua_protected(lua_State *L, const cesdk_lua_exports *api, int op, int input_count, const void *data, size_t size, intptr_t a, intptr_t b) {
    if (!L || !api || input_count < 0 || !api->checkstack || !api->gettop || !api->pcallk || !api->checkstack(L, 1)) return CESDK_NO_ERROR;
    call_context context = { api, op, data, size, a, b }; call_context *previous = s_context; s_context = &context;
    api->pushcclosure(L, operation, 0);
    if (input_count) api->rotate(L, -(input_count + 1), 1);
    int status = api->pcallk(L, input_count, LUA_MULTRET, 0, 0, 0);
    s_context = previous;
    return status;
}

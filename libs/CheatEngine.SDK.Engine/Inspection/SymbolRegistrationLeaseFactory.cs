using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Internal lease-construction seam used only to test managed publication failures after CE registration.</summary>
internal delegate SymbolRegistrationLease SymbolRegistrationLeaseFactory(SymbolName name,
	SymbolRegistrationOptions options, LuaStateIdentity identity);

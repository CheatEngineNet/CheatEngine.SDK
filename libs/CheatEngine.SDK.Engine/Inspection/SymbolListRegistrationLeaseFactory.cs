using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Internal lease-construction seam used only to test a managed failure after a successful <c>register()</c>.</summary>
internal delegate SymbolListRegistrationLease SymbolListRegistrationLeaseFactory(Owned<SymbolList> list,
	bool registrationConfirmed);

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Internal lease-publication seam used only to test managed publication failures after CE registration.</summary>
internal delegate void SymbolRegistrationLeasePublisher(SymbolName name, SymbolRegistrationLease lease);

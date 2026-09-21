using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>Internal owner-publication seam used to qualify post-allocation compensation.</summary>
internal delegate AllocatedRegion AllocatedRegionFactory(ITargetBoundMemoryAllocationOperations operations,
    Address address, TargetAllocationSize size, TargetProcessIncarnation targetIncarnation);

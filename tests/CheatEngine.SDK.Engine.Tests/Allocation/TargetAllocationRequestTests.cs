using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     Pure managed validation of the allocation request's typed size, target address, and optional initial protection.
/// </summary>
public sealed class TargetAllocationRequestTests
{
    [Fact]
    public void Constructor_with_valid_input_preserves_the_target_request()
    {
        TargetAllocationSize size = new(4096);
        Address preferred = new(0x7FF6_2000_0000);
        TargetAllocationRequest request = new(size, preferred, MemoryProtection.ExecuteReadWrite);

        Assert.Equal(size, request.Size);
        Assert.Equal(preferred, request.PreferredBaseAddress);
        Assert.Equal(MemoryProtection.ExecuteReadWrite, request.Protection);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void TargetAllocationSize_with_a_nonpositive_value_throws(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TargetAllocationSize(value));
    }

    [Fact]
    public void Allocator_with_the_default_request_rejects_it_before_the_binding()
    {
        AllocationOperationsFake operations = new();
        TargetMemoryAllocator allocator = new(operations);

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(default));
        Assert.Equal(0, operations.AllocateCalls);
    }
}

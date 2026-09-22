using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>Tests the copied observation layout and the intentionally lossy telemetry handoff.</summary>
public sealed unsafe class DebugEventObservationBufferTests
{
	[Fact]
	public void Observation_on_64_bit_has_the_documented_scalar_layout()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		DebugEventObservation observation = default;
		void* origin = &observation;

		Assert.Equal(24, Layout.SizeOf<DebugEventObservation>());
		Assert.Equal(0, Layout.OffsetOf(origin, &observation.SequenceNumber));
		Assert.Equal(8, Layout.OffsetOf(origin, &observation.EventCode));
		Assert.Equal(12, Layout.OffsetOf(origin, &observation.ProcessId));
		Assert.Equal(16, Layout.OffsetOf(origin, &observation.ThreadId));
	}

	[Fact]
	public void Drop_newest_keeps_the_existing_copy_and_reports_the_loss()
	{
		BoundedDebugEventObservationBuffer buffer = new(1, DebugEventObservationOverflowPolicy.DropNewest);
		DebugEventObservation first = new(1, 10, 20, 30);
		DebugEventObservation second = new(2, 11, 21, 31);

		Assert.True(buffer.TryPublish(in first));
		Assert.False(buffer.TryPublish(in second));
		Assert.Equal(1, buffer.DroppedObservationCount);
		Assert.True(buffer.TryRead(out DebugEventObservation retained));
		Assert.Equal(first.SequenceNumber, retained.SequenceNumber);
		Assert.False(buffer.TryRead(out _));
	}

	[Fact]
	public void Drop_oldest_keeps_the_newest_copy_and_reports_the_loss()
	{
		BoundedDebugEventObservationBuffer buffer = new(1, DebugEventObservationOverflowPolicy.DropOldest);
		DebugEventObservation first = new(1, 10, 20, 30);
		DebugEventObservation second = new(2, 11, 21, 31);

		Assert.True(buffer.TryPublish(in first));
		Assert.True(buffer.TryPublish(in second));
		Assert.Equal(1, buffer.DroppedObservationCount);
		Assert.True(buffer.TryRead(out DebugEventObservation retained));
		Assert.Equal(second.SequenceNumber, retained.SequenceNumber);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Buffer_rejects_a_non_positive_capacity(int capacity)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_ = new BoundedDebugEventObservationBuffer(capacity, DebugEventObservationOverflowPolicy.DropNewest));
	}

	[Fact]
	public void Buffer_rejects_an_unknown_overflow_policy()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_ = new BoundedDebugEventObservationBuffer(1, (DebugEventObservationOverflowPolicy) 99));
	}
}

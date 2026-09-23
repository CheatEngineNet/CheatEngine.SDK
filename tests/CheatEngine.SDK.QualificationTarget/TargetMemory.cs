using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QualificationTarget;

/// <summary>
///     The heap side of the target: pinned arrays, so every address in the ready record stays valid for the whole run.
///     Allocated once at start; nothing is freed before exit.
/// </summary>
internal sealed unsafe class TargetMemory
{
	private readonly byte[] _heapMarkers;
	private readonly byte[] _repeated;
	private readonly long[] _values;

	internal TargetMemory(int heapCopies, int repetitions)
	{
		HeapCopies = heapCopies;
		Repetitions = repetitions;
		RepeatedPattern = TargetLayout.CreateRepeatedPattern();

		_heapMarkers = GC.AllocateArray<byte>(heapCopies * TargetLayout.HeapCopyStride, true);
		for (int copy = 0; copy < heapCopies; copy++)
		{
			TargetLayout.Marker.CopyTo(_heapMarkers.AsSpan(copy * TargetLayout.HeapCopyStride));
		}

		_repeated = GC.AllocateArray<byte>(repetitions * TargetLayout.RepeatedPatternLength, true);
		for (int repetition = 0; repetition < repetitions; repetition++)
		{
			RepeatedPattern.CopyTo(_repeated.AsSpan(repetition * TargetLayout.RepeatedPatternLength));
		}

		// Slot 0 holds the Int32 cell in its low four bytes, slot 1 the Int64 cell; both stay naturally aligned.
		_values = GC.AllocateArray<long>(2, true);
		Int32 = TargetLayout.InitialInt32;
		Int64 = TargetLayout.InitialInt64;
	}

	internal int HeapCopies
	{
		get;
	}

	internal int Repetitions
	{
		get;
	}

	internal byte[] RepeatedPattern
	{
		get;
	}

	internal ReadOnlySpan<byte> HeapMarkers => _heapMarkers;

	internal ReadOnlySpan<byte> Repeated => _repeated;

	internal nint HeapMarkersAddress => (nint) Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_heapMarkers));

	internal nint RepeatedAddress => (nint) Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_repeated));

	internal nint Int32Address => (nint) Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_values));

	internal nint Int64Address => Int32Address + sizeof(long);

	internal int Int32
	{
		get => Volatile.Read(ref Unsafe.As<long, int>(ref _values[0]));
		private set => Volatile.Write(ref Unsafe.As<long, int>(ref _values[0]), value);
	}

	internal long Int64
	{
		get => Volatile.Read(ref _values[1]);
		private set => Volatile.Write(ref _values[1], value);
	}

	/// <summary>Advances both value cells by one, the change a next scan looks for.</summary>
	internal void Step()
	{
		Int32 = unchecked(Int32 + 1);
		Int64 = unchecked(Int64 + 1);
	}
}

using System.Reflection;

using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;

using EngineAddressList = CheatEngine.SDK.Engine.AddressList.AddressList;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>Pure value and ownership-shape tests for the address-list handles.</summary>
public sealed class AddressListValueTests
{
	[Fact]
	public void Borrowed_handles_wrap_identity_without_becoming_owners()
	{
		CEObject raw = new(0x1234);
		EngineAddressList list = new(raw);
		MemoryRecord record = new(raw);

		Assert.Equal(raw, list.Handle);
		Assert.Equal(raw, record.Handle);
		Assert.Equal(list, EngineAddressList.FromHandle(raw));
		Assert.Equal(record, MemoryRecord.FromHandle(raw));
		Assert.Equal("AddressList(CEObject@0x1234)", list.ToString());
		Assert.Equal("MemoryRecord(CEObject@0x1234)", record.ToString());
		Assert.False(typeof(EngineAddressList).IsAssignableTo(typeof(IDisposable)));
		Assert.False(typeof(MemoryRecord).IsAssignableTo(typeof(IDisposable)));
	}

	[Fact]
	public void Default_handles_are_null_and_compare_by_native_identity()
	{
		EngineAddressList first = new(new CEObject(0x1));
		EngineAddressList same = new(new CEObject(0x1));
		EngineAddressList other = new(new CEObject(0x2));

		Assert.True(default(EngineAddressList).IsNull);
		Assert.True(default(MemoryRecord).IsNull);
		Assert.Equal("AddressList(null)", EngineAddressList.Null.ToString());
		Assert.Equal("MemoryRecord(null)", MemoryRecord.Null.ToString());
		Assert.True(first == same);
		Assert.True(first != other);
	}

	[Fact]
	public void Memory_record_id_keeps_identity_distinct_from_a_list_index()
	{
		MemoryRecordId id = new(-12);

		Assert.Equal(-12, id.Value);
		Assert.Equal("-12", id.ToString());
		Assert.True(id == new MemoryRecordId(-12));
		Assert.True(id != new MemoryRecordId(0));
		Assert.True(id.CompareTo(new MemoryRecordId(4)) < 0);
	}

	[Fact]
	public void Gui_handle_members_do_not_claim_main_thread_affinity_before_the_live_dispatcher_probe()
	{
		Assert.False(HasMainThreadOnly(typeof(AddressListAccess).GetMethod(nameof(AddressListAccess.TryGetCurrent))));
		Assert.False(HasMainThreadOnly(typeof(EngineAddressList).GetMethod(nameof(EngineAddressList.TryGetCount))));
		Assert.False(HasMainThreadOnly(typeof(MemoryRecord).GetMethod(nameof(MemoryRecord.TryGetId))));
		Assert.Null(typeof(MemScan).GetMethod("TryGetFoundCount"));
		Assert.False(HasMainThreadOnly(typeof(FoundList)
			.GetMethod(nameof(FoundList.TryGetCount))));
	}

	private static bool HasMainThreadOnly(MethodInfo? method)
	{
		Assert.NotNull(method);
		return Attribute.IsDefined(method!, typeof(MainThreadOnlyAttribute));
	}
}

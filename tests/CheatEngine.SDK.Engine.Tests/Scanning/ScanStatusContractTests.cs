using System.Globalization;

using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>
///     Numeric contracts of the SDK-owned scan status and outcome enums: the zero value is <c>Unknown</c>, so a
///     default value never reads as success, and every member keeps the value these literals pin. The CE-mirroring
///     enums (<c>VariableType</c>, <c>ScanOption</c>, ...) are frozen elsewhere and are not listed here.
/// </summary>
public sealed class ScanStatusContractTests
{
	/// <summary>Every pinned member of every SDK-owned scan status/outcome enum, as (type, name, value).</summary>
	public static TheoryData<Type, string, long> PinnedMembers => new()
	{
		{ typeof(AobScanStatus), "Unknown", 0 },
		{ typeof(AobScanStatus), "Success", 1 },
		{ typeof(AobScanStatus), "GlobalUnavailable", 2 },
		{ typeof(AobScanStatus), "LuaFailure", 3 },
		{ typeof(AobScanStatus), "NoResult", 4 },
		{ typeof(AobScanStatus), "InvalidResult", 5 },
		{ typeof(MemoryScanCreationStatus), "Unknown", 0 },
		{ typeof(MemoryScanCreationStatus), "Success", 1 },
		{ typeof(MemoryScanCreationStatus), "GlobalUnavailable", 2 },
		{ typeof(MemoryScanCreationStatus), "LuaFailure", 3 },
		{ typeof(MemoryScanCreationStatus), "NoScannerResult", 4 },
		{ typeof(MemoryScanCreationStatus), "InvalidScannerResult", 5 },
		{ typeof(MemoryScanCreationStatus), "NoFoundListResult", 6 },
		{ typeof(MemoryScanCreationStatus), "InvalidFoundListResult", 7 },
		{ typeof(MemoryScanCreationStatus), "AliasedFoundList", 8 },
		{ typeof(MemoryScanCreationStatus), "RollbackUnconfirmed", 9 },
		{ typeof(MemoryScanCreationStatus), "TargetIdentityUnavailable", 10 },
		{ typeof(MemoryScanMaterializationStatus), "Unknown", 0 },
		{ typeof(MemoryScanMaterializationStatus), "Success", 1 },
		{ typeof(MemoryScanMaterializationStatus), "NoResults", 2 },
		{ typeof(MemoryScanMaterializationStatus), "DestinationTooSmall", 3 },
		{ typeof(MemoryScanMaterializationStatus), "Cancelled", 4 },
		{ typeof(MemoryScanMaterializationStatus), "RuntimeInvalidated", 5 },
		{ typeof(MemoryScanMaterializationStatus), "TargetIdentityUnavailable", 6 },
		{ typeof(MemoryScanMaterializationStatus), "TargetIdentityMismatch", 7 },
		{ typeof(MemoryScanMaterializationStatus), "LuaFailure", 8 },
		{ typeof(MemoryScanMaterializationStatus), "InvalidResult", 9 },
		{ typeof(MemoryScanMaterializationStatus), "PageStartOutOfRange", 10 },
		{ typeof(MemoryScanWaitStatus), "Unknown", 0 },
		{ typeof(MemoryScanWaitStatus), "Completed", 1 },
		{ typeof(MemoryScanWaitStatus), "TimedOut", 2 },
		{ typeof(MemoryScanWaitStatus), "LuaFailure", 3 },
		{ typeof(MemoryScanWaitStatus), "InvalidResult", 4 },
		{ typeof(MemoryScanWaitStatus), "InitializationFailed", 5 },
		{ typeof(MemoryScanWaitStatus), "RuntimeInvalidated", 6 },
		{ typeof(MemoryScanWaitStatus), "TargetIdentityUnavailable", 7 },
		{ typeof(MemoryScanWaitStatus), "TargetIdentityMismatch", 8 },
		{ typeof(MemoryScanTerminationStatus), "Unknown", 0 },
		{ typeof(MemoryScanTerminationStatus), "NotRequired", 1 },
		{ typeof(MemoryScanTerminationStatus), "Confirmed", 2 },
		{ typeof(MemoryScanTerminationStatus), "WaitTimedOut", 3 },
		{ typeof(MemoryScanTerminationStatus), "TerminateFailed", 4 },
		{ typeof(MemoryScanTerminationStatus), "WaitFailed", 5 },
		{ typeof(MemoryScanTerminationStatus), "NotInvoked", 6 },
		{ typeof(MemoryScanInvalidationReason), "None", 0 },
		{ typeof(MemoryScanInvalidationReason), "ProtectedLuaFailure", 1 },
		{ typeof(MemoryScanInvalidationReason), "RuntimeIdentityChanged", 2 },
		{ typeof(MemoryScanInvalidationReason), "TargetChanged", 3 },
		{ typeof(MemoryScanInvalidationReason), "TargetProcessReused", 4 },
		{ typeof(MemoryScanInvalidationReason), "ScanTerminated", 5 }
	};

	[Fact]
	public void AobScanStatus_default_is_unknown_and_never_success()
	{
		AobScanStatus status = default;

		Assert.Equal(AobScanStatus.Unknown, status);
		Assert.NotEqual(AobScanStatus.Success, status);
		Assert.Equal("Unknown", Enum.GetName(status));
	}

	[Fact]
	public void MemoryScanCreationStatus_default_is_unknown_and_never_success()
	{
		MemoryScanCreationStatus status = default;

		Assert.Equal(MemoryScanCreationStatus.Unknown, status);
		Assert.NotEqual(MemoryScanCreationStatus.Success, status);
		Assert.Equal("Unknown", Enum.GetName(status));
	}

	[Fact]
	public void MemoryScanMaterializationStatus_default_is_unknown_and_never_success()
	{
		MemoryScanMaterializationStatus status = default;

		Assert.Equal(MemoryScanMaterializationStatus.Unknown, status);
		Assert.NotEqual(MemoryScanMaterializationStatus.Success, status);
		Assert.Equal("Unknown", Enum.GetName(status));
	}

	[Fact]
	public void MemoryScanWaitStatus_and_MemoryScanTerminationStatus_defaults_are_unknown_and_never_success()
	{
		MemoryScanWaitStatus wait = default;
		MemoryScanTerminationStatus termination = default;

		Assert.Equal(MemoryScanWaitStatus.Unknown, wait);
		Assert.NotEqual(MemoryScanWaitStatus.Completed, wait);
		Assert.Equal(MemoryScanTerminationStatus.Unknown, termination);
		Assert.NotEqual(MemoryScanTerminationStatus.Confirmed, termination);
		Assert.NotEqual(MemoryScanTerminationStatus.NotRequired, termination);
	}

	[Fact]
	public void MemoryScanCreationOutcome_default_status_is_unknown()
	{
		MemoryScanCreationOutcome outcome = default;

		Assert.Equal(MemoryScanCreationStatus.Unknown, outcome.Status);
		Assert.False(outcome.TargetObservation.IsQualified);
	}

	[Theory]
	[MemberData(nameof(PinnedMembers))]
	public void Scan_status_enums_pin_their_numeric_values(Type enumType, string name, long value)
	{
		Assert.True(Enum.IsDefined(enumType, name), $"{enumType.Name}.{name} is not declared.");

		long actual = Convert.ToInt64(Enum.Parse(enumType, name), CultureInfo.InvariantCulture);

		Assert.Equal(value, actual);
	}

	[Fact]
	public void Scan_status_enums_declare_exactly_the_pinned_members()
	{
		Dictionary<Type, SortedSet<string>> pinned = [];
		foreach (TheoryDataRow<Type, string, long> row in PinnedMembers)
		{
			(Type enumType, string name, _) = row.Data;
			if (!pinned.TryGetValue(enumType, out SortedSet<string>? names))
			{
				names = new SortedSet<string>(StringComparer.Ordinal);
				pinned.Add(enumType, names);
			}

			names.Add(name);
		}

		foreach ((Type enumType, SortedSet<string> names) in pinned)
		{
			Assert.Equal(names, Enum.GetNames(enumType).Order(StringComparer.Ordinal), StringComparer.Ordinal);
		}
	}
}

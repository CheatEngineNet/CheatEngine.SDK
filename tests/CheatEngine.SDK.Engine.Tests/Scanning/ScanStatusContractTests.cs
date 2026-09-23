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
		{ typeof(MemoryScanMaterializationStatus), "PageStartOutOfRange", 10 }
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

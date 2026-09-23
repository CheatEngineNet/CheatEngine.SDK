namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>
///     Enum contracts, read from the PublicAPI files (an enum member is the line <c>T.M = &lt;int&gt; -&gt; T</c>). With
///     RS0016/RS0017 enforced by the build, a PublicAPI line cannot drift from the code, so these text checks freeze
///     values: enums that mirror Cheat Engine constants or appear in public Client signatures keep their 1.0.0 members,
///     every enum added since 1.0.0 is classified, and SDK-owned status/outcome enums do not read as success when a value
///     was never assigned (<c>Unknown = 0</c> hygiene, audit A11-20).
/// </summary>
public sealed class EnumContractTests
{
	/// <summary>
	///     Frozen at their 1.0.0 members. The nine <c>Enums/*</c> types mirror Cheat Engine constants (their values are
	///     also pinned by <c>tests/CheatEngine.SDK.Engine.Tests/Enums/EnumValueTests.cs</c>); <c>CheatEngineArchitecture</c>
	///     and <c>TargetAbi</c> are SDK decodings of Cheat Engine codes that appear in public Client signatures and that no
	///     other test pins numerically.
	/// </summary>
	private static readonly SortedSet<string> s_frozenEnums = new(StringComparer.Ordinal)
	{
		"CheatEngine.SDK.Engine.Enums.BreakpointMethod",
		"CheatEngine.SDK.Engine.Enums.BreakpointTrigger",
		"CheatEngine.SDK.Engine.Enums.ContinueMethod",
		"CheatEngine.SDK.Engine.Enums.DuplicateHandling",
		"CheatEngine.SDK.Engine.Enums.FastScanMethod",
		"CheatEngine.SDK.Engine.Enums.MemoryProtection",
		"CheatEngine.SDK.Engine.Enums.RoundingType",
		"CheatEngine.SDK.Engine.Enums.ScanOption",
		"CheatEngine.SDK.Engine.Enums.VariableType",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineArchitecture",
		"CheatEngine.SDK.Engine.Runtime.TargetAbi"
	};

	/// <summary>Reviewed additions to a frozen enum (for example a new Cheat Engine constant), as "Type.Member". Empty.</summary>
	private static readonly HashSet<string> s_reviewedFrozenEnumAdditions = new(StringComparer.Ordinal);

	/// <summary>Every enum type declared since 1.0.0, with its category. A new enum must be added here.</summary>
	private static readonly Dictionary<string, EnumCategory> s_addedEnums = new(StringComparer.Ordinal)
	{
		["CheatEngine.SDK.Abi.Native.DebugEventDecision"] = EnumCategory.AbiDecision,
		["CheatEngine.SDK.Abi.Native.DebugEventObservationOverflowPolicy"] = EnumCategory.Policy,
		["CheatEngine.SDK.Engine.AddressList.MemoryRecordMutationEffect"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.AddressList.MemoryRecordMutationProblem"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Allocation.TargetMemoryOperationOutcomeKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Assembly.InstructionOperationStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Inspection.SymbolRegistrationReleaseKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Processes.ProcessOperationStatusKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Runtime.CheatEngineOperatingSystem"] = EnumCategory.ReasonOrEvidence,
		["CheatEngine.SDK.Engine.Runtime.TargetBackend"] = EnumCategory.ReasonOrEvidence,
		["CheatEngine.SDK.Engine.Scanning.Aob.AobScanOutcomeKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Scanning.Aob.AobScanStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanCancellationMilestone"] = EnumCategory.ReasonOrEvidence,
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanCreationStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanInvalidationReason"] = EnumCategory.ReasonOrEvidence,
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanMaterializationStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Targets.TargetIdentityCheckKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Targets.TargetIdentityEvidence"] = EnumCategory.ReasonOrEvidence,
		["CheatEngine.SDK.Engine.Targets.TargetReleaseStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Engine.Targets.TargetSelectionObservationStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Lua.Calls.LuaOperationStatusKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Lua.CompilerServices.LuaGlobalPushStatus"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy"] = EnumCategory.Policy,
		["CheatEngine.SDK.Lua.Registration.LuaRegistrationReleaseKind"] = EnumCategory.StatusOrOutcome,
		["CheatEngine.SDK.Lua.Registration.LuaRegistrationResultKind"] = EnumCategory.StatusOrOutcome
	};

	/// <summary>
	///     Status/outcome enums whose zero member still reads as success, with the lot that renumbers them (a break,
	///     declared through CompatibilitySuppressions.xml when the enum shipped, and a PublicAPI change). The list only
	///     shrinks: <see cref="Pending_zero_value_fixes_are_still_needed" /> fails once an entry is fixed.
	/// </summary>
	private static readonly Dictionary<string, string> s_pendingZeroValueFixes = new(StringComparer.Ordinal)
	{
		["CheatEngine.SDK.Engine.Inspection.SymbolRegistrationReleaseKind"] = "S-RES",
		["CheatEngine.SDK.Engine.Scanning.Aob.AobScanStatus"] = "S-SCAN",
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanCreationStatus"] = "S-SCAN",
		["CheatEngine.SDK.Engine.Scanning.Values.MemoryScanMaterializationStatus"] = "S-SCAN"
	};

	/// <summary>Zero-member names that read as "it worked" (or, for a failure-kind enum, "no failure").</summary>
	private static readonly HashSet<string> s_successLikeNames = new(StringComparer.Ordinal)
	{
		"Complete", "Completed", "Done", "None", "Ok", "Released", "Succeeded", "Success", "Successful"
	};

	/// <summary>
	///     Neutral zero members for a status/outcome enum: new enums use <c>Unknown</c>; the other names are tolerated
	///     because enums already use them with the same "nothing established yet" meaning.
	/// </summary>
	private static readonly HashSet<string> s_neutralZeroNames = new(StringComparer.Ordinal)
	{
		"Unknown", "Unspecified", "Uninitialized", "NotAttempted"
	};

	private enum EnumCategory
	{
		Unknown = 0,
		StatusOrOutcome,
		Policy,
		ReasonOrEvidence,
		AbiDecision
	}

	[Fact]
	public void Enums_mirroring_cheat_engine_constants_or_client_signatures_keep_their_1_0_0_members()
	{
		Dictionary<string, SortedDictionary<string, long>> shipped = EnumMembers(static l => l.Shipped);
		foreach (string frozen in s_frozenEnums)
		{
			Assert.True(shipped.ContainsKey(frozen), $"{frozen} is not an enum of the 1.0.0 surface.");
		}

		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			foreach (string removed in library.Removed)
			{
				Assert.False(IsMemberOfFrozenEnum(removed, out string enumType),
					$"{library.UnshippedPath}: '*REMOVED*{removed}' changes the frozen enum {enumType}.");
			}

			foreach (string added in library.Added)
			{
				if (IsMemberOfFrozenEnum(added, out string enumType)
					&& PublicApiDeclarations.TryParseEnumMember(added, out _, out string member, out _))
				{
					Assert.True(s_reviewedFrozenEnumAdditions.Contains($"{enumType}.{member}"),
						$"{library.UnshippedPath}: '{added}' adds a member to the frozen enum {enumType} without a reviewed entry.");
				}
			}
		}
	}

	[Fact]
	public void Every_enum_added_after_1_0_0_is_classified()
	{
		Dictionary<string, SortedDictionary<string, long>> shipped = EnumMembers(static l => l.Shipped);
		SortedSet<string> added = new(StringComparer.Ordinal);
		foreach (string enumType in EnumMembers(static l => l.Declared).Keys)
		{
			if (!shipped.ContainsKey(enumType))
			{
				added.Add(enumType);
			}
		}

		SortedSet<string> classified = new(s_addedEnums.Keys, StringComparer.Ordinal);
		Assert.True(added.SetEquals(classified),
			$"Classify every enum declared since 1.0.0 in {nameof(s_addedEnums)}. Missing: {string.Join(", ", added.Except(classified, StringComparer.Ordinal))}. Stale: {string.Join(", ", classified.Except(added, StringComparer.Ordinal))}.");
		Assert.DoesNotContain(EnumCategory.Unknown, s_addedEnums.Values);
	}

	[Fact]
	public void Status_and_outcome_enums_added_after_1_0_0_do_not_default_to_success()
	{
		Dictionary<string, SortedDictionary<string, long>> declared = EnumMembers(static l => l.Declared);
		foreach ((string enumType, EnumCategory category) in s_addedEnums)
		{
			if (category != EnumCategory.StatusOrOutcome || s_pendingZeroValueFixes.ContainsKey(enumType))
			{
				continue;
			}

			string? zero = ZeroMember(declared[enumType]);
			Assert.True(zero is not null, $"{enumType} has no member with value 0, so default({SimpleName(enumType)}) has no name.");
			Assert.False(s_successLikeNames.Contains(zero), $"{enumType}.{zero} = 0 reads as success.");
			Assert.True(s_neutralZeroNames.Contains(zero),
				$"{enumType}.{zero} = 0: a status/outcome enum starts with Unknown = 0 (tolerated: {string.Join(", ", s_neutralZeroNames)}).");
		}
	}

	[Fact]
	public void Pending_zero_value_fixes_are_still_needed()
	{
		Dictionary<string, SortedDictionary<string, long>> declared = EnumMembers(static l => l.Declared);
		foreach ((string enumType, string owner) in s_pendingZeroValueFixes)
		{
			Assert.True(s_addedEnums.TryGetValue(enumType, out EnumCategory category) && category == EnumCategory.StatusOrOutcome,
				$"{enumType} is pending but not classified as {EnumCategory.StatusOrOutcome}.");
			Assert.False(string.IsNullOrWhiteSpace(owner));
			string? zero = declared.TryGetValue(enumType, out SortedDictionary<string, long>? members) ? ZeroMember(members) : null;
			Assert.True(zero is not null && s_successLikeNames.Contains(zero),
				$"{enumType} no longer starts with a success-like member ({zero ?? "no zero member"}): {owner} fixed it, so remove it from {nameof(s_pendingZeroValueFixes)}.");
		}
	}

	private static bool IsMemberOfFrozenEnum(string line, out string enumType)
	{
		if (PublicApiDeclarations.TryParseEnumMember(line, out enumType, out _, out _))
		{
			return s_frozenEnums.Contains(enumType);
		}

		enumType = "";
		return false;
	}

	private static Dictionary<string, SortedDictionary<string, long>> EnumMembers(
		Func<PublicApiLibrary, IEnumerable<string>> lines)
	{
		Dictionary<string, SortedDictionary<string, long>> enums = new(StringComparer.Ordinal);
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			foreach (string line in lines(library))
			{
				if (PublicApiDeclarations.TryParseEnumMember(line, out string enumType, out string member, out long value))
				{
					if (!enums.TryGetValue(enumType, out SortedDictionary<string, long>? members))
					{
						members = new SortedDictionary<string, long>(StringComparer.Ordinal);
						enums.Add(enumType, members);
					}

					members[member] = value;
				}
			}
		}

		return enums;
	}

	private static string? ZeroMember(SortedDictionary<string, long> members)
	{
		foreach ((string member, long value) in members)
		{
			if (value == 0)
			{
				return member;
			}
		}

		return null;
	}

	private static string SimpleName(string typeName)
	{
		return typeName[(typeName.LastIndexOf('.') + 1)..];
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     A bounded, copied view of the disable information Cheat Engine returned for an applied Auto Assembler script:
///     its allocations, registered symbols, exception ranges and symbols.
/// </summary>
/// <remarks>
///     <para>
///         The snapshot is diagnostic data. The SDK never frees, unregisters or destroys anything from it: the patch's
///         rooted disable-info table stays the only disable authority, passed back unchanged to Cheat Engine's
///         <c>[DISABLE]</c> handling. In particular <c>ccodesymbols</c> is a symbol list that Cheat Engine registered:
///         it is reported by <see cref="HasCCodeSymbolList" /> only and is never owned, destroyed or unregistered by the
///         SDK.
///     </para>
///     <para>
///         The copy is bounded by <see cref="AutoAssemblerOptions.MaxDisableInfoEntries" /> entries per section and
///         <see cref="AutoAssemblerOptions.MaxDisableInfoNameBytes" /> bytes per name, reads keys only when they are
///         strings (a numeric key is never converted), reads fields without metamethods, and traverses dictionaries with
///         a protected <c>next</c>. Because the traversal order of a Lua table is unspecified, <see cref="Allocations" />
///         and <see cref="Symbols" /> are sorted by name with ordinal comparison; the indexed sections
///         <see cref="RegisteredSymbols" /> and <see cref="ExceptionRanges" /> keep Cheat Engine's index order. An entry
///         with an unexpected shape is skipped and makes <see cref="Status" />
///         <see cref="AutoAssemblerDisableInfoSnapshotStatus.Malformed" />; a reached limit makes it
///         <see cref="AutoAssemblerDisableInfoSnapshotStatus.Truncated" />. Neither fails the activation (a deliberate
///         choice: a diagnostic copy that failed must not force a compensating disable of a patch Cheat Engine applied).
///     </para>
/// </remarks>
public sealed class AutoAssemblerDisableInfoSnapshot
{
	private AutoAssemblerDisableInfoSnapshot(IReadOnlyList<AutoAssemblerAllocationInfo> allocations,
		IReadOnlyList<string> registeredSymbols, IReadOnlyList<Address> exceptionRanges,
		IReadOnlyList<AutoAssemblerSymbolInfo> symbols, bool hasCCodeSymbolList,
		AutoAssemblerDisableInfoSnapshotStatus status)
	{
		Allocations = allocations;
		RegisteredSymbols = registeredSymbols;
		ExceptionRanges = exceptionRanges;
		Symbols = symbols;
		HasCCodeSymbolList = hasCCodeSymbolList;
		Status = status;
	}

	/// <summary>Gets the copied <c>allocs</c> entries, sorted by ordinal name.</summary>
	public IReadOnlyList<AutoAssemblerAllocationInfo> Allocations
	{
		get;
	}

	/// <summary>Gets the copied <c>registeredsymbols</c> names, in Cheat Engine's index order.</summary>
	public IReadOnlyList<string> RegisteredSymbols
	{
		get;
	}

	/// <summary>Gets the copied <c>exceptionlist</c> start addresses, in Cheat Engine's index order.</summary>
	public IReadOnlyList<Address> ExceptionRanges
	{
		get;
	}

	/// <summary>Gets the copied <c>symbols</c> entries (symbols and labels of the script), sorted by ordinal name.</summary>
	public IReadOnlyList<AutoAssemblerSymbolInfo> Symbols
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine reported a <c>ccodesymbols</c> symbol list. That list belongs to Cheat Engine: the SDK
	///     never owns, destroys or unregisters it; the <c>[DISABLE]</c> handling of the rooted table does.
	/// </summary>
	public bool HasCCodeSymbolList
	{
		get;
	}

	/// <summary>Gets how completely the table was copied.</summary>
	public AutoAssemblerDisableInfoSnapshotStatus Status
	{
		get;
	}

	/// <summary>
	///     Copies the disable-info table at <paramref name="tableIndex" />. Never raises a Lua error and restores the stack.
	/// </summary>
	internal static AutoAssemblerDisableInfoSnapshot Read(LuaState state, int tableIndex,
		AutoAssemblerOptions options)
	{
		int table = state.AbsoluteIndex(tableIndex);
		Reader reader = new(state, table, options);
		List<AutoAssemblerAllocationInfo> allocations = reader.ReadAllocations();
		List<string> registeredSymbols = reader.ReadRegisteredSymbols();
		List<Address> exceptionRanges = reader.ReadExceptionRanges();
		List<AutoAssemblerSymbolInfo> symbols = reader.ReadSymbols();
		bool hasCCodeSymbolList = reader.ReadHasCCodeSymbolList();

		allocations.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
		symbols.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
		return new AutoAssemblerDisableInfoSnapshot(allocations.AsReadOnly(), registeredSymbols.AsReadOnly(),
			exceptionRanges.AsReadOnly(), symbols.AsReadOnly(), hasCCodeSymbolList, reader.Status);
	}

	private sealed class Reader
	{
		private readonly AutoAssemblerOptions _options;
		private readonly LuaState _state;
		private readonly int _table;
		private bool _malformed;
		private bool _truncated;

		public Reader(LuaState state, int table, AutoAssemblerOptions options)
		{
			_state = state;
			_table = table;
			_options = options;
		}

		public AutoAssemblerDisableInfoSnapshotStatus Status => _malformed
			? AutoAssemblerDisableInfoSnapshotStatus.Malformed
			: _truncated
				? AutoAssemblerDisableInfoSnapshotStatus.Truncated
				: AutoAssemblerDisableInfoSnapshotStatus.Complete;

		public List<AutoAssemblerAllocationInfo> ReadAllocations()
		{
			List<AutoAssemblerAllocationInfo> allocations = [];
			using LuaFrame frame = new(_state);
			if (!TryPushSection("allocs"u8, out int section))
			{
				return allocations;
			}

			ReadDictionary(section, allocations, static (reader, name, value, list) =>
			{
				if (reader.TryReadAllocation(name, value, out AutoAssemblerAllocationInfo allocation))
				{
					list.Add(allocation);
				}
			});
			return allocations;
		}

		public List<AutoAssemblerSymbolInfo> ReadSymbols()
		{
			List<AutoAssemblerSymbolInfo> symbols = [];
			using LuaFrame frame = new(_state);
			if (!TryPushSection("symbols"u8, out int section))
			{
				return symbols;
			}

			ReadDictionary(section, symbols, static (reader, name, value, list) =>
			{
				if (Address.TryRead(reader._state, value, out Address address))
				{
					list.Add(new AutoAssemblerSymbolInfo(name, address));
				}
				else
				{
					reader._malformed = true;
				}
			});
			return symbols;
		}

		public List<string> ReadRegisteredSymbols()
		{
			List<string> names = [];
			using LuaFrame frame = new(_state);
			if (!TryPushSection("registeredsymbols"u8, out int section))
			{
				return names;
			}

			ReadSequence(section, names, static (reader, element, list) =>
			{
				if (reader.TryReadName(element, out string? name))
				{
					list.Add(name);
				}
			});
			return names;
		}

		public List<Address> ReadExceptionRanges()
		{
			List<Address> ranges = [];
			using LuaFrame frame = new(_state);
			if (!TryPushSection("exceptionlist"u8, out int section))
			{
				return ranges;
			}

			ReadSequence(section, ranges, static (reader, element, list) =>
			{
				if (Address.TryRead(reader._state, element, out Address address))
				{
					list.Add(address);
				}
				else
				{
					reader._malformed = true;
				}
			});
			return ranges;
		}

		public bool ReadHasCCodeSymbolList()
		{
			using LuaFrame frame = new(_state);
			_state.PushString("ccodesymbols"u8);
			LuaType type = _state.RawGet(_table);
			if (type == LuaType.Nil)
			{
				return false;
			}

			if (CEObject.TryRead(_state, -1, out _))
			{
				return true;
			}

			_malformed = true;
			return false;
		}

		// Pushes t[name] without metamethods. Absent is an empty section; any non-table value is malformed.
		private bool TryPushSection(ReadOnlySpan<byte> name, out int section)
		{
			_state.PushString(name);
			LuaType type = _state.RawGet(_table);
			section = _state.Top;
			if (type == LuaType.Table)
			{
				return true;
			}

			if (type != LuaType.Nil)
			{
				_malformed = true;
			}

			return false;
		}

		// Protected next-traversal of a name-keyed section, bounded by visited entries. The caller's frame restores the
		// stack, including when the traversal stops with a key and a value still pushed.
		private void ReadDictionary<T>(int section, List<T> list, Action<Reader, string, int, List<T>> readEntry)
		{
			int visited = 0;
			_state.PushNil();
			while (true)
			{
				LuaStatus status = _state.TryNext(section, out bool hasNext);
				if (!status.IsOk)
				{
					_malformed = true;
					return;
				}

				if (!hasNext)
				{
					return;
				}

				if (visited == _options.MaxDisableInfoEntries)
				{
					_truncated = true;
					return;
				}

				visited++;
				int value = _state.Top;
				if (TryReadName(value - 1, out string? name))
				{
					readEntry(this, name, value, list);
				}

				_state.Pop(1);
			}
		}

		// Raw reads of t[1..n] with n = the raw length, bounded by the entry limit.
		private void ReadSequence<T>(int section, List<T> list, Action<Reader, int, List<T>> readElement)
		{
			ulong length = _state.RawLength(section);
			ulong limit = (ulong) _options.MaxDisableInfoEntries;
			if (length > limit)
			{
				_truncated = true;
				length = limit;
			}

			for (long index = 1; (ulong) index <= length; index++)
			{
				_ = _state.RawGetIndex(section, index);
				if (_state.IsNil(-1))
				{
					_malformed = true;
				}
				else
				{
					readElement(this, _state.Top, list);
				}

				_state.Pop(1);
			}
		}

		private bool TryReadName(int index, [NotNullWhen(true)] out string? name)
		{
			// Only a real string: a numeric key is never converted in place during a traversal.
			if (!_state.TryReadUtf8(index, out ReadOnlySpan<byte> utf8))
			{
				_malformed = true;
				name = null;
				return false;
			}

			if (utf8.Length > _options.MaxDisableInfoNameBytes)
			{
				_truncated = true;
				name = null;
				return false;
			}

			name = Encoding.UTF8.GetString(utf8);
			return true;
		}

		private bool TryReadAllocation(string name, int value, out AutoAssemblerAllocationInfo allocation)
		{
			allocation = default;
			if (!_state.IsTable(value))
			{
				_malformed = true;
				return false;
			}

			using LuaFrame frame = new(_state);
			_state.PushString("address"u8);
			_ = _state.RawGet(value);
			if (!Address.TryRead(_state, -1, out Address address))
			{
				_malformed = true;
				return false;
			}

			_state.PushString("size"u8);
			_ = _state.RawGet(value);
			if (!_state.IsInteger(-1) || !_state.TryReadInteger(-1, out long size) || size < 0)
			{
				_malformed = true;
				return false;
			}

			_state.PushString("prefered"u8);
			LuaType preferredType = _state.RawGet(value);
			Address? preferred = null;
			if (preferredType != LuaType.Nil)
			{
				if (!Address.TryRead(_state, -1, out Address preferredAddress))
				{
					_malformed = true;
					return false;
				}

				preferred = preferredAddress;
			}

			allocation = new AutoAssemblerAllocationInfo(name, address, size, preferred);
			return true;
		}
	}
}

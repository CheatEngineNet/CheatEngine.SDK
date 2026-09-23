using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     The per-field layout gate behind <c>FieldLayout.FieldLayoutContractTests</c>: compares every instance field of a
///     structure with its literal <see cref="FieldLayoutRow" /> (offset, width and kind) in both directions.
/// </summary>
/// <remarks>
///     <para>
///         <b>Offset</b> is measured, not computed: a <see cref="DynamicMethod" /> applies the IL instruction
///         <c>ldflda</c> for the field to the address of a zeroed native buffer of the structure's size and subtracts the
///         buffer address. That is exactly the arithmetic the JIT performs for <c>&amp;record-&gt;Field</c> in a plugin,
///         it
///         works for private and compiler-generated fields, and it does not go through
///         <see cref="Marshal.OffsetOf(Type, string)" />, whose <i>unmanaged</i> view is what CA1421 warns about in an
///         assembly with runtime marshalling disabled. The field is never read or written.
///         <c>FieldLayoutContractTests.Reflected_offsets_agree_with_address_of_offsets_for_the_packed_init_record_and_the_managed_exports</c>
///         re-measures the four most important records with C# address-of arithmetic.
///     </para>
///     <para>
///         <b>Width</b> is <see cref="IntPtr.Size" /> for a data or function pointer and
///         <see cref="RuntimeHelpers.SizeOf(RuntimeTypeHandle)" /> otherwise. <b>Kind</b> is derived from the modified
///         field type, the only reflection view that still distinguishes a function pointer from a data pointer.
///     </para>
///     <para>Pure: no state, no I/O; the tests exercise it against wrong-on-purpose structures.</para>
/// </remarks>
internal static class FieldLayoutGate
{
	/// <summary>Every instance field, public or not, including compiler-generated backing fields.</summary>
	public const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	/// <summary>
	///     Returns one message per violation of <paramref name="structure" /> against the rows of
	///     <paramref name="expectations" /> that name it: a field without a row, a row without a field, a duplicate row,
	///     or a wrong offset, width or kind. Empty when the structure matches.
	/// </summary>
	public static IReadOnlyList<string> FindViolations(Type structure, IReadOnlyList<FieldLayoutRow> expectations)
	{
		ArgumentNullException.ThrowIfNull(structure);
		ArgumentNullException.ThrowIfNull(expectations);

		string typeName = structure.FullName ?? structure.Name;
		Dictionary<string, FieldLayoutRow> rows = new(StringComparer.Ordinal);
		List<string> violations = [];
		foreach (FieldLayoutRow row in expectations)
		{
			if (string.Equals(row.TypeFullName, typeName, StringComparison.Ordinal) && !rows.TryAdd(row.FieldName, row))
			{
				violations.Add($"{typeName}.{row.FieldName}: duplicate layout row.");
			}
		}

		foreach (FieldInfo field in structure.GetFields(InstanceFields))
		{
			if (!rows.Remove(field.Name, out FieldLayoutRow row))
			{
				violations.Add($"{typeName}.{field.Name}: missing layout row for this field.");
				continue;
			}

			int offset = OffsetOf(field);
			if (offset != row.Offset)
			{
				violations.Add($"{typeName}.{field.Name}: offset {Text(offset)}, expected {Text(row.Offset)}.");
			}

			int width = WidthOf(field);
			if (width != row.Width)
			{
				violations.Add($"{typeName}.{field.Name}: width {Text(width)}, expected {Text(row.Width)}.");
			}

			FieldKind kind = KindOf(field);
			if (kind != row.Kind)
			{
				violations.Add($"{typeName}.{field.Name}: kind {kind}, expected {row.Kind}.");
			}
		}

		foreach (string extra in rows.Keys.Order(StringComparer.Ordinal))
		{
			violations.Add($"{typeName}.{extra}: extra layout row names no field of the structure.");
		}

		return violations;
	}

	/// <summary>Offset of <paramref name="field" /> in its declaring structure, see the class remarks.</summary>
	public static unsafe int OffsetOf(FieldInfo field)
	{
		ArgumentNullException.ThrowIfNull(field);
		Type declaringType = field.DeclaringType ??
							 throw new ArgumentException("A field has a declaring type.", nameof(field));
		if (!declaringType.IsValueType || field.IsStatic)
		{
			throw new ArgumentException("Only instance fields of structures have a layout offset.", nameof(field));
		}

		// (nint) &((T*) base)->Field - base, with the managed pointer converted before the subtraction.
		DynamicMethod method = new("FieldOffset", typeof(nint), [typeof(nint)], typeof(FieldLayoutGate).Module, true);
		ILGenerator il = method.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldflda, field);
		il.Emit(OpCodes.Conv_U);
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Sub);
		il.Emit(OpCodes.Ret);
		Func<nint, nint> measure = method.CreateDelegate<Func<nint, nint>>();

		nuint size = (nuint) Math.Max(1, RuntimeHelpers.SizeOf(declaringType.TypeHandle));
		void* buffer = NativeMemory.AllocZeroed(size);
		try
		{
			return checked((int) measure((nint) buffer));
		}
		finally
		{
			NativeMemory.Free(buffer);
		}
	}

	/// <summary>Byte width of <paramref name="field" />: pointer size for any pointer, the managed size otherwise.</summary>
	public static int WidthOf(FieldInfo field)
	{
		ArgumentNullException.ThrowIfNull(field);
		Type plain = field.GetModifiedFieldType().UnderlyingSystemType;
		return plain.IsPointer || plain.IsFunctionPointer
			? IntPtr.Size
			: RuntimeHelpers.SizeOf(field.FieldType.TypeHandle);
	}

	/// <summary>Classifies <paramref name="field" /> into a <see cref="FieldKind" />.</summary>
	public static FieldKind KindOf(FieldInfo field)
	{
		ArgumentNullException.ThrowIfNull(field);
		Type plain = field.GetModifiedFieldType().UnderlyingSystemType;
		if (plain.IsFunctionPointer)
		{
			return FieldKind.FunctionPointer;
		}

		if (plain.IsPointer)
		{
			return plain.GetElementType() == typeof(void) ? FieldKind.OpaquePointer : FieldKind.Pointer;
		}

		if (plain == typeof(Bool32) || plain == typeof(Bool8))
		{
			return FieldKind.AbiBoolean;
		}

		return plain.IsPrimitive || plain.IsEnum ? FieldKind.Integer : FieldKind.Struct;
	}

	private static string Text(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}

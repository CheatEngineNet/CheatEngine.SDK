using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CheatEngine.SDK.SourceGenerators.Shared;

/// <summary>
///     An immutable array with <b>value</b> equality, for use inside incremental-pipeline models.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ImmutableArray{T}" />, arrays and lists compare by reference, so a record that holds one is never
///         equal
///         to the record produced by the next generator run and the pipeline re-emits on every keystroke. Wrapping the
///         array restores element-wise equality, which is what lets a step report <c>Unchanged</c>/<c>Cached</c>.
///     </para>
///     <para>
///         <c>default(EquatableArray&lt;T&gt;)</c> is a valid empty array and equals <see cref="Empty" />. The element
///         type
///         must itself be value-equatable (a record, a string, a primitive, another <see cref="EquatableArray{T}" />);
///         never an <c>ISymbol</c>, a <c>SyntaxNode</c> or a <c>Location</c>.
///     </para>
/// </remarks>
/// <typeparam name="T">Element type, compared with <see cref="EqualityComparer{T}.Default" />.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
	where T : IEquatable<T>
{
	/// <summary>The empty array.</summary>
	public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

	// May be 'default' (null underlying array) when the struct itself is default-initialised: every member
	// goes through Items, which normalises that case to the empty array.
	private readonly ImmutableArray<T> _items;

	/// <summary>Wraps <paramref name="items" /> without copying. A default array is treated as empty.</summary>
	public EquatableArray(ImmutableArray<T> items)
	{
		_items = items;
	}

	/// <summary>Number of elements.</summary>
	public int Length => Items.Length;

	/// <summary><see langword="true" /> when the array has no element.</summary>
	public bool IsEmpty => Items.IsEmpty;

	/// <summary>Element at <paramref name="index" />.</summary>
	public T this[int index] => Items[index];

	private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

	public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
	{
		return !left.Equals(right);
	}

	public static implicit operator EquatableArray<T>(ImmutableArray<T> items)
	{
		return new EquatableArray<T>(items);
	}

	/// <summary>The underlying array (never default). No copy.</summary>
	public ImmutableArray<T> AsImmutableArray()
	{
		return Items;
	}

	/// <summary>A span over the elements. No copy.</summary>
	public ReadOnlySpan<T> AsSpan()
	{
		return Items.AsSpan();
	}

	/// <summary>Allocation-free enumerator picked by <see langword="foreach" />.</summary>
	public ImmutableArray<T>.Enumerator GetEnumerator()
	{
		return Items.GetEnumerator();
	}

	/// <inheritdoc />
	public bool Equals(EquatableArray<T> other)
	{
		ImmutableArray<T> left = Items;
		ImmutableArray<T> right = other.Items;

		// Same backing array: the usual case when an upstream step handed the previous array back.
		if (left == right)
		{
			return true;
		}

		if (left.Length != right.Length)
		{
			return false;
		}

		EqualityComparer<T> comparer = EqualityComparer<T>.Default;
		for (int i = 0; i < left.Length; i++)
		{
			if (!comparer.Equals(left[i], right[i]))
			{
				return false;
			}
		}

		return true;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is EquatableArray<T> other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		// System.HashCode does not exist on netstandard2.0 and a package reference is not an option for a Roslyn
		// component: order-dependent multiplicative combine (FNV offset basis, prime 31).
		ImmutableArray<T> items = Items;
		EqualityComparer<T> comparer = EqualityComparer<T>.Default;
		int hash = unchecked((int) 2166136261);
		for (int i = 0; i < items.Length; i++)
		{
			T? item = items[i];
			hash = unchecked((hash * 31) + (item is null ? 0 : comparer.GetHashCode(item)));
		}

		return hash;
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return ((IEnumerable<T>) Items).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable) Items).GetEnumerator();
	}
}

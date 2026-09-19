using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.SharedCode;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Equals_same_elements_in_different_arrays_is_true()
    {
        EquatableArray<string> left = new(["a", "b", "c"]);
        EquatableArray<string> right = new(["a", "b", "c"]);

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_different_order_is_false()
    {
        EquatableArray<string> left = new(["a", "b"]);
        EquatableArray<string> right = new(["b", "a"]);

        Assert.False(left.Equals(right));
        Assert.True(left != right);
        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_different_length_is_false()
    {
        EquatableArray<int> left = new([1, 2]);
        EquatableArray<int> right = new([1, 2, 3]);

        Assert.False(left.Equals(right));
        Assert.False(right.Equals(left));
    }

    [Fact]
    public void Equals_other_type_is_false()
    {
        EquatableArray<int> array = new([1]);

        Assert.False(array.Equals("not an array"));
        Assert.False(array.Equals(null));
    }

    [Fact]
    public void Default_value_behaves_as_the_empty_array()
    {
        EquatableArray<string> defaulted = default;

        Assert.Equal(0, defaulted.Length);
        Assert.True(defaulted.IsEmpty);
        Assert.True(defaulted.Equals(EquatableArray<string>.Empty));
        Assert.True(defaulted.Equals(new EquatableArray<string>([])));
        Assert.Equal(EquatableArray<string>.Empty.GetHashCode(), defaulted.GetHashCode());
        Assert.False(defaulted.AsImmutableArray().IsDefault);
        Assert.True(defaulted.AsSpan().IsEmpty);
        Assert.Empty(defaulted);
    }

    [Fact]
    public void Equals_null_elements_are_compared_without_throwing()
    {
        // The constraint asks for non-nullable elements; a null that slips through must still not crash the pipeline.
        EquatableArray<string> left = new(["a", null!]);
        EquatableArray<string> right = new(["a", null!]);
        EquatableArray<string> other = new([null!, "a"]);

        Assert.True(left.Equals(right));
        Assert.False(left.Equals(other));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Indexer_length_span_and_enumeration_expose_the_elements_in_order()
    {
        ImmutableArray<int> source = [3, 1, 2];
        EquatableArray<int> array = source;

        Assert.Equal(3, array.Length);
        Assert.False(array.IsEmpty);
        Assert.Equal(1, array[1]);
        Assert.Equal([3, 1, 2], array.AsSpan().ToArray());
        Assert.Equal(source, array.AsImmutableArray());

        List<int> enumerated = [];
        foreach (var item in array) enumerated.Add(item);

        Assert.Equal([3, 1, 2], enumerated);
        Assert.Equal([3, 1, 2], array.ToList());
    }

    [Fact]
    public void Record_holding_an_array_gets_value_equality()
    {
        // The reason the type exists: a record with an ImmutableArray member would compare by reference here.
        Holder left = new("name", new EquatableArray<string>(["x", "y"]));
        Holder right = new("name", new EquatableArray<string>(["x", "y"]));
        Holder different = new("name", new EquatableArray<string>(["x", "z"]));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, different);
    }

    [Fact]
    public void Nested_arrays_compare_by_value()
    {
        EquatableArray<EquatableArray<int>> left = new([new EquatableArray<int>([1, 2]), new EquatableArray<int>([3])]);
        EquatableArray<EquatableArray<int>>
            right = new([new EquatableArray<int>([1, 2]), new EquatableArray<int>([3])]);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    private sealed record Holder(string Name, EquatableArray<string> Items);
}

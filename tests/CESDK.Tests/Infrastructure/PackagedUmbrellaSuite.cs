namespace CESDK.Tests.Infrastructure;

/// <summary>
///     The test collection every class in <c>Packaging/</c> joins, so all of them share the one
///     <see cref="PackagedUmbrellaFixture" /> instance instead of each triggering its own pack + three consumer
///     restore/build cycles: <c>IClassFixture&lt;T&gt;</c> alone would give each test class a separate instance, this
///     gives every class in the collection the same one.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PackagedUmbrellaSuite : ICollectionFixture<PackagedUmbrellaFixture>
{
    /// <summary>The collection name every <c>Packaging/*.cs</c> test class passes to <c>[Collection]</c>.</summary>
    public const string Name = "Packaged umbrella";
}

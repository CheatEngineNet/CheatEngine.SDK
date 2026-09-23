using System.Reflection;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The Debug CI leg excludes the packaging fixture with <c>--filter-not-trait "Category=Packaging"</c>, and the
///     Release
///     leg hands it the exact package. That only works if the trait and the collection coincide: a collection class
///     without the trait would start a pack in the Debug leg (or fail it under <c>CI=true</c>), and a traited class
///     outside the collection would silently drop out of the Debug leg. Read through <see cref="CustomAttributeData" />,
///     so nothing is instantiated.
/// </summary>
public sealed class PackagedUmbrellaTraitTests
{
	private const string CategoryTrait = "Category";

	[Fact]
	public void Every_packaged_umbrella_collection_class_carries_the_packaging_trait()
	{
		List<Type> members = [];
		List<string> offenders = [];
		foreach (Type type in TestTypes())
		{
			bool joinsCollection = JoinsPackagedUmbrellaCollection(type);
			if (joinsCollection)
			{
				members.Add(type);
				if (!HasPackagingTrait(type))
				{
					offenders.Add($"{type.FullName} joins '{PackagedUmbrellaSuite.Name}' without " +
					              $"[Trait(\"{CategoryTrait}\", UmbrellaPackage.PackagingCategory)].");
				}
			}
			else if (ReceivesTheFixture(type))
			{
				offenders.Add($"{type.FullName} receives {nameof(PackagedUmbrellaFixture)} outside collection " +
				              $"'{PackagedUmbrellaSuite.Name}', so it would get its own pack.");
			}
		}

		Assert.Contains(typeof(RestoreIsolationTests), members);
		Assert.Contains(typeof(SupplyChainPackageTests), members);
		Assert.Contains(typeof(PackageProvenanceTests), members);
		Assert.Contains(typeof(CleanConsumerIsolationTests), members);
		Assert.Empty(offenders);
	}

	[Fact]
	public void No_class_outside_the_packaged_umbrella_collection_carries_the_packaging_trait()
	{
		List<string> offenders = [];
		int outside = 0;
		foreach (Type type in TestTypes())
		{
			if (JoinsPackagedUmbrellaCollection(type))
			{
				continue;
			}

			outside++;
			if (HasPackagingTrait(type) || HasPackagingTraitOnAMethod(type))
			{
				offenders.Add(type.FullName!);
			}
		}

		Assert.True(outside >= 5,
			$"Only {outside} test classes live outside the collection; the Debug module must never become empty.");
		Assert.Contains(typeof(PackagedUmbrellaTraitTests), TestTypes());
		Assert.Empty(offenders);
	}

	private static List<Type> TestTypes()
	{
		List<Type> types = [];
		foreach (Type type in typeof(PackagedUmbrellaFixture).Assembly.GetTypes())
		{
			if (type is { IsClass: true, IsAbstract: false } && HasTestMethod(type))
			{
				types.Add(type);
			}
		}

		return types;
	}

	private static bool HasTestMethod(Type type)
	{
		foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
		                                              BindingFlags.Static |
		                                              BindingFlags.DeclaredOnly))
		{
			foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
			{
				if (typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool JoinsPackagedUmbrellaCollection(Type type)
	{
		foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
		{
			if (attribute.AttributeType != typeof(CollectionAttribute) || attribute.ConstructorArguments.Count != 1)
			{
				continue;
			}

			object? argument = attribute.ConstructorArguments[0].Value;
			if (Equals(argument, PackagedUmbrellaSuite.Name) || Equals(argument, typeof(PackagedUmbrellaSuite)))
			{
				return true;
			}
		}

		return false;
	}

	private static bool ReceivesTheFixture(Type type)
	{
		foreach (ConstructorInfo constructor in type.GetConstructors())
		{
			foreach (ParameterInfo parameter in constructor.GetParameters())
			{
				if (parameter.ParameterType == typeof(PackagedUmbrellaFixture))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool HasPackagingTrait(MemberInfo member)
	{
		foreach (CustomAttributeData attribute in member.GetCustomAttributesData())
		{
			if (attribute.AttributeType == typeof(TraitAttribute)
			    && attribute.ConstructorArguments.Count == 2
			    && Equals(attribute.ConstructorArguments[0].Value, CategoryTrait)
			    && Equals(attribute.ConstructorArguments[1].Value, UmbrellaPackage.PackagingCategory))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasPackagingTraitOnAMethod(Type type)
	{
		foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
		                                              BindingFlags.Static |
		                                              BindingFlags.DeclaredOnly))
		{
			if (HasPackagingTrait(method))
			{
				return true;
			}
		}

		return false;
	}
}

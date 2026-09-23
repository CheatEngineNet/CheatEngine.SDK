using System.Reflection;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;

using EngineAddressList = CheatEngine.SDK.Engine.AddressList.AddressList;
using EngineMemoryRecord = CheatEngine.SDK.Engine.AddressList.MemoryRecord;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>
///     The public ownership surface of the Engine assembly: every durable resource exposes its origin, a getter never
///     grants destroy authority, and memory records stay borrowed.
/// </summary>
public sealed class OwnershipSurfaceTests
{
	private const BindingFlags PublicMembers =
		BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	private static readonly Type[] SDurableResources =
	[
		typeof(Owned<CEObject>), typeof(AllocatedRegion), typeof(AutoAssemblerPatch), typeof(SymbolRegistrationLease),
		typeof(SymbolListRegistrationLease)
	];

	[Fact]
	public void Every_durable_resource_exposes_its_origin()
	{
		foreach (Type resource in SDurableResources)
		{
			PropertyInfo? origin = resource.GetProperty("Origin", BindingFlags.Instance | BindingFlags.Public);

			Assert.True(origin is not null, resource.Name + " has no public Origin.");
			Assert.Equal(typeof(EngineResourceOrigin), origin.PropertyType);
			Assert.False(origin.CanWrite, resource.Name + ".Origin is writable.");
		}
	}

	[Fact]
	public void No_public_property_or_get_method_returns_an_owner()
	{
		List<string> offenders = [];
		foreach (Type type in EngineTypes())
		{
			foreach (PropertyInfo property in type.GetProperties(PublicMembers))
			{
				if (IsOwner(property.PropertyType))
				{
					offenders.Add(type.Name + "." + property.Name);
				}
			}

			foreach (MethodInfo method in type.GetMethods(PublicMembers))
			{
				if (!method.IsSpecialName && method.Name.StartsWith("Get", StringComparison.Ordinal) &&
				    IsOwner(method.ReturnType))
				{
					offenders.Add(type.Name + "." + method.Name);
				}
			}
		}

		Assert.True(offenders.Count == 0, "A getter grants destroy authority: " + string.Join(", ", offenders));
	}

	[Fact]
	public void Memory_records_are_never_wrapped_in_an_owner()
	{
		Type ownedRecord = typeof(Owned<EngineMemoryRecord>);
		List<string> offenders = [];
		foreach (Type type in EngineTypes())
		{
			foreach (PropertyInfo property in type.GetProperties(PublicMembers))
			{
				if (property.PropertyType == ownedRecord)
				{
					offenders.Add(type.Name + "." + property.Name);
				}
			}

			foreach (MethodInfo method in type.GetMethods(PublicMembers))
			{
				if (method.ReturnType == ownedRecord || Array.Exists(method.GetParameters(),
					    parameter => UnderlyingType(parameter.ParameterType) == ownedRecord))
				{
					offenders.Add(type.Name + "." + method.Name);
				}
			}
		}

		ParameterInfo created = typeof(EngineAddressList).GetMethod(nameof(EngineAddressList.TryCreateMemoryRecord))!
			.GetParameters()[0];

		Assert.True(offenders.Count == 0, "A memory record is exposed as an owner: " + string.Join(", ", offenders));
		Assert.True(created.IsOut);
		Assert.True(Attribute.IsDefined(created, typeof(CEOwnedAttribute)));
	}

	private static Type[] EngineTypes()
	{
		return typeof(Owned<>).Assembly.GetExportedTypes();
	}

	private static bool IsOwner(Type type)
	{
		Type candidate = UnderlyingType(type);
		return candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Owned<>);
	}

	private static Type UnderlyingType(Type type)
	{
		return type.IsByRef ? type.GetElementType()! : type;
	}
}

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>
///     The exact set of scanning members behind an <c>[Experimental]</c> gate, read by reflection from the compiled
///     Engine assembly: CESDK5010 covers every member whose correctness depends on the unobserved timed-out wait or on
///     <c>terminateScan</c> (spike D4.7), CESDK5011 covers the separately named first-found opt-in (F07); nothing else in
///     the scanning namespaces is gated. The no-deadline bounded scan stays ungated because the spike settled its
///     semantics.
/// </summary>
public sealed class ScanExperimentalApiTests
{
	private const string UrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md";

	[Fact]
	public void Scanning_experimental_members_are_exactly_the_documented_gates()
	{
		SortedSet<string> expected = new(StringComparer.Ordinal)
		{
			"CESDK5010 AobScanner.TryScanWithinBounds(String, AobScanBounds, AobScanOptions, TimeSpan, Span<Address>, CancellationToken)",
			"CESDK5010 MemoryScanSession.TryTerminateScan(TimeSpan)",
			"CESDK5010 MemoryScanSession.TryWaitForCompletion(TimeSpan)",
			"CESDK5011 AobScanner.TryFindFirstFoundWithinBounds(String, AobScanBounds, AobScanOptions, CancellationToken)"
		};

		SortedSet<string> actual = new(StringComparer.Ordinal);
		foreach ((MemberInfo member, ExperimentalAttribute attribute) in ExperimentalScanningMembers())
		{
			Assert.Equal(UrlFormat, attribute.UrlFormat);
			actual.Add(attribute.DiagnosticId + " " + Describe(member));
		}

		Assert.Equal(expected, actual, StringComparer.Ordinal);
	}

	private static IEnumerable<(MemberInfo Member, ExperimentalAttribute Attribute)> ExperimentalScanningMembers()
	{
		foreach (Type type in typeof(AobScanner).Assembly.GetExportedTypes())
		{
			if (type.Namespace is null ||
				!type.Namespace.StartsWith("CheatEngine.SDK.Engine.Scanning", StringComparison.Ordinal))
			{
				continue;
			}

			if (type.GetCustomAttribute<ExperimentalAttribute>() is { } typeAttribute)
			{
				yield return (type, typeAttribute);
			}

			foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
														  BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				if (member.GetCustomAttribute<ExperimentalAttribute>() is { } attribute)
				{
					yield return (member, attribute);
				}
			}
		}
	}

	private static string Describe(MemberInfo member)
	{
		if (member is MethodBase method)
		{
			string parameters = string.Join(", ", method.GetParameters().Select(static parameter =>
				parameter.ParameterType.IsByRef
					? TypeName(parameter.ParameterType.GetElementType()!) + "&"
					: TypeName(parameter.ParameterType)));
			return method.DeclaringType!.Name + "." + method.Name + "(" + parameters + ")";
		}

		return member is Type type ? type.Name : member.DeclaringType!.Name + "." + member.Name;
	}

	private static string TypeName(Type type)
	{
		if (!type.IsGenericType)
		{
			return type.Name;
		}

		string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
		return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
	}
}

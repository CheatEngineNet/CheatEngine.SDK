using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.SourceScanning;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     A lexical scanner for raw <c>LuaApi</c> uses in C# source: <c>LuaApi.member</c> (optionally namespace- or
///     <c>global::</c>-qualified), or a bare member name in a file that imports
///     <c>using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;</c>. Comments and literals are blanked first
///     (<see cref="CSharpCode.BlankCommentsAndLiterals" />), so documentation that names a member is not a use. Only
///     names of real <c>LuaApi</c> members count.
/// </summary>
/// <remarks>
///     Text-based on purpose: the repository tests reference no Roslyn package. The Roslyn guard for the catalogued
///     bridge routes is <c>LuaDirectApiBoundaryGuardTests</c> in the analyzer tests.
/// </remarks>
internal static partial class LuaApiUseScanner
{
	/// <summary>Returns every use of a member of <paramref name="members" /> in <paramref name="source" />.</summary>
	internal static IReadOnlyList<LuaApiUse> Scan(string source, IReadOnlySet<string> members)
	{
		string code = CSharpCode.BlankCommentsAndLiterals(source);
		bool staticImport = StaticImport().IsMatch(code);
		List<LuaApiUse> uses = [];
		foreach (Match match in MemberReference().Matches(code))
		{
			string member = match.Groups["member"].Value;
			if (!members.Contains(member) || (!match.Groups["qualifier"].Success && !staticImport))
			{
				continue;
			}

			uses.Add(new LuaApiUse(member, CSharpCode.LineOf(code, match.Index)));
		}

		return uses;
	}

	[GeneratedRegex(@"using\s+static\s+(?:global::)?CheatEngine\.SDK\.Lua\.Interop\.Api\.LuaApi\s*;",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex StaticImport();

	[GeneratedRegex(
		@"(?<![\w.])(?<qualifier>(?:global::)?(?:CheatEngine\.SDK\.Lua\.Interop\.Api\.)?LuaApi\s*\.\s*)?\b(?<member>(?:lua|luaL|luaopen)_\w+)\b",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex MemberReference();
}

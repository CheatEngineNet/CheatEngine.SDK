using System.Globalization;
using System.Text.Json;

using CheatEngine.SDK.Analyzers.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.Analyzers.Tests.Architecture;

/// <summary>
///     Repository-only gate for the C11 Lua protection boundary. This is intentionally not a consumer-facing Roslyn
///     diagnostic: it inspects SDK sources and is driven by the versioned bridge-operation catalogue, so there is no
///     new <c>CESDKxxxx</c> identifier or release-tracking entry until the policy is stable enough for consumers.
/// </summary>
public sealed class LuaDirectApiBoundaryGuardTests
{
	private const string LuaApiQualifiedName = "CheatEngine.SDK.Lua.Interop.Api.LuaApi";
	private const string CataloguePath = "eng/lua-bridge/protected-operations.json";
	private const string LibrariesPath = "libs";
	private const string RawApiPath = "libs/CheatEngine.SDK.Lua.Interop/Api/";
	private const string LightCFunctionFastPathSourcePath = "libs/CheatEngine.SDK.Lua/State/LuaState.Callbacks.cs";

	[Fact]
	public void Production_layers_route_catalogued_risky_LuaApi_operations_through_the_native_boundary()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectProductionSources(policy);

		Assert.True(violations.Count == 0, FormatViolations(violations));
	}

	[Fact]
	public void Catalogue_risky_direct_operations_have_a_deterministic_bridge_decision()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		HashSet<string> bridgeOperations = LoadBridgeOperations();
		List<LuaDirectApiPolicyEntry> entries = policy.Entries;
		int riskyCount = 0;

		for (int index = 0; index < entries.Count; index++)
		{
			LuaDirectApiPolicyEntry entry = entries[index];
			if (string.Equals(entry.Raises, "never", StringComparison.Ordinal))
			{
				Assert.True(entry.AllowedDirectly, entry.ManagedSymbol);
				Assert.False(entry.RequiresBridge, entry.ManagedSymbol);
				continue;
			}

			riskyCount++;
			Assert.False(entry.AllowedDirectly, entry.ManagedSymbol);
			Assert.True(entry.RequiresBridge, entry.ManagedSymbol);
			Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.ManagedSymbol);
			Assert.NotEmpty(entry.Provenance);

			if (entry.ConditionalDirectUse)
			{
				Assert.Equal("lua_pushcclosure", entry.MemberName);
				Assert.True(entry.ConditionalAllowed, entry.ManagedSymbol);
				Assert.False(string.IsNullOrWhiteSpace(entry.ConditionalWhen), entry.ManagedSymbol);
				Assert.False(string.IsNullOrWhiteSpace(entry.ConditionalProof), entry.ManagedSymbol);
				continue;
			}

			if (string.IsNullOrWhiteSpace(entry.BridgeOperation))
			{
				// lua_error is deliberately used only inside a bridge operation's native failure path. It has no
				// public managed wrapper to name here, but the direct-use decision remains unambiguously "bridge".
				Assert.Equal("lua_error", entry.MemberName);
				continue;
			}

			Assert.Contains(entry.BridgeOperation, bridgeOperations, StringComparer.Ordinal);
		}

		Assert.True(riskyCount > 0, "The catalogue contains no direct Lua APIs classified memory or any.");
	}

	[Fact]
	public void Guard_reports_a_catalogued_risky_member_access()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using CheatEngine.SDK.Lua.Interop.Api;

		                                                        unsafe class C
		                                                        {
		                                                            void M(lua_State* state, byte* bytes)
		                                                            {
		                                                                _ = LuaApi.lua_pushlstring(state, bytes, 1);
		                                                            }
		                                                        }
		                                                        """, "MemberAccess.cs");

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_pushlstring", violation.MemberName);
		Assert.Equal(7, violation.Line);
	}

	[Fact]
	public void Guard_reports_a_catalogued_risky_static_import()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using CheatEngine.SDK.Lua.Interop.Api;
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe class C
		                                                        {
		                                                            void M(lua_State* state, lua_CFunction function)
		                                                            {
		                                                                lua_pushcclosure(state, function, 1);
		                                                            }
		                                                        }
		                                                        """, "StaticImport.cs");

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_pushcclosure", violation.MemberName);
	}

	[Fact]
	public void Guard_reports_a_catalogued_risky_exact_LuaApi_alias()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using CheatEngine.SDK.Lua.Interop.Api;
		                                                        using Api = CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe class C
		                                                        {
		                                                            void M(lua_State* state)
		                                                            {
		                                                                Api.lua_createtable(state, 0, 0);
		                                                            }
		                                                        }
		                                                        """, "Alias.cs");

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_createtable", violation.MemberName);
	}

	[Fact]
	public void Guard_reports_a_zero_upvalue_closure_outside_the_audited_fast_path()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using CheatEngine.SDK.Lua.Interop.Api;

		                                                        unsafe class C
		                                                        {
		                                                            void M(lua_State* state, lua_CFunction function)
		                                                            {
		                                                                LuaApi.lua_pushcclosure(state, function, 0);
		                                                            }
		                                                        }
		                                                        """, "LightCFunction.cs");

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_pushcclosure", violation.MemberName);
	}

	[Fact]
	public void Guard_allows_only_the_audited_light_C_function_fast_path_after_its_immediate_one_slot_reservation()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe struct C
		                                                        {
		                                                            private lua_State* Pointer;

		                                                            void PushUncheckedFunction(lua_CFunction thunk)
		                                                            {
		                                                                if (lua_checkstack(Pointer, 1) == 0)
		                                                                    throw new InvalidOperationException();

		                                                                lua_pushcclosure(Pointer, thunk, 0);
		                                                            }
		                                                        }
		                                                        """, LightCFunctionFastPathSourcePath);

		Assert.Empty(violations);
	}

	[Fact]
	public void Guard_allows_the_audited_fast_path_when_the_throw_guard_uses_a_block()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe struct C
		                                                        {
		                                                            private lua_State* Pointer;

		                                                            void PushUncheckedFunction(lua_CFunction thunk)
		                                                            {
		                                                                if (lua_checkstack(Pointer, 1) == 0)
		                                                                {
		                                                                    throw new InvalidOperationException();
		                                                                }

		                                                                lua_pushcclosure(Pointer, thunk, 0);
		                                                            }
		                                                        }
		                                                        """, LightCFunctionFastPathSourcePath);

		Assert.Empty(violations);
	}

	[Fact]
	public void Guard_rejects_the_audited_fast_path_when_a_braced_throw_guard_contains_a_Lua_call()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe struct C
		                                                        {
		                                                            private lua_State* Pointer;

		                                                            void PushUncheckedFunction(lua_CFunction thunk)
		                                                            {
		                                                                if (lua_checkstack(Pointer, 1) == 0)
		                                                                {
		                                                                    lua_pushinteger(Pointer, 42);
		                                                                    throw new InvalidOperationException();
		                                                                }

		                                                                lua_pushcclosure(Pointer, thunk, 0);
		                                                            }
		                                                        }
		                                                        """, LightCFunctionFastPathSourcePath);

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_pushcclosure", violation.MemberName);
	}

	[Fact]
	public void Guard_reports_the_audited_path_when_a_Lua_call_interrupts_the_reservation_and_push()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        unsafe struct C
		                                                        {
		                                                            private lua_State* Pointer;

		                                                            void PushUncheckedFunction(lua_CFunction thunk)
		                                                            {
		                                                                if (lua_checkstack(Pointer, 1) == 0)
		                                                                    throw new InvalidOperationException();

		                                                                lua_pushinteger(Pointer, 42);
		                                                                lua_pushcclosure(Pointer, thunk, 0);
		                                                            }
		                                                        }
		                                                        """, LightCFunctionFastPathSourcePath);

		GuardViolation violation = Assert.Single(violations);
		Assert.Equal("lua_pushcclosure", violation.MemberName);
	}

	[Fact]
	public void Guard_does_not_match_an_unrelated_method_with_a_Lua_shaped_name()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        class C
		                                                        {
		                                                            void lua_pushlstring(int value) { }

		                                                            void M()
		                                                            {
		                                                                lua_pushlstring(42);
		                                                            }
		                                                        }
		                                                        """, "Unrelated.cs");

		Assert.Empty(violations);
	}

	[Fact]
	public void Guard_does_not_treat_a_member_that_shadows_a_static_import_as_the_raw_LuaApi_member()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

		                                                        class C
		                                                        {
		                                                            static void lua_rawset(int value) { }

		                                                            void M()
		                                                            {
		                                                                lua_rawset(42);
		                                                            }
		                                                        }
		                                                        """, "ShadowedStaticImport.cs");

		Assert.Empty(violations);
	}

	[Fact]
	public void Guard_does_not_match_an_unrelated_LuaApi_type_without_the_exact_SDK_import()
	{
		LuaDirectApiPolicy policy = LoadPolicy();
		List<GuardViolation> violations = InspectSource(policy, """
		                                                        unsafe class LuaApi
		                                                        {
		                                                            public static void lua_rawset(int value) { }
		                                                        }

		                                                        class C
		                                                        {
		                                                            void M()
		                                                            {
		                                                                LuaApi.lua_rawset(42);
		                                                            }
		                                                        }
		                                                        """, "UnrelatedType.cs");

		Assert.Empty(violations);
	}

	private static LuaDirectApiPolicy LoadPolicy()
	{
		using JsonDocument catalogue = LoadCatalogue();
		JsonElement directApiPolicy = catalogue.RootElement.GetProperty("directApiPolicy");
		List<LuaDirectApiPolicyEntry> entries = [];

		foreach (JsonElement entry in directApiPolicy.EnumerateArray())
		{
			string managedSymbol = RequiredString(entry, "managedSymbol");
			const string prefix = LuaApiQualifiedName + ".";
			Assert.StartsWith(prefix, managedSymbol, StringComparison.Ordinal);
			string memberName = managedSymbol[prefix.Length..];
			Assert.DoesNotContain(".", memberName, StringComparison.Ordinal);

			bool conditionalDirectUse = entry.TryGetProperty("conditionalDirectUse", out JsonElement conditional);
			entries.Add(new LuaDirectApiPolicyEntry(
				managedSymbol,
				memberName,
				RequiredString(entry, "raises"),
				entry.GetProperty("allowedDirectly").GetBoolean(),
				entry.GetProperty("requiresBridge").GetBoolean(),
				RequiredString(entry, "reason"),
				ReadProvenance(entry),
				entry.TryGetProperty("bridgeOperation", out JsonElement bridgeOperation)
					? bridgeOperation.GetString()
					: null,
				conditionalDirectUse,
				conditionalDirectUse && conditional.GetProperty("allowed").GetBoolean(),
				conditionalDirectUse ? RequiredString(conditional, "when") : null,
				conditionalDirectUse ? RequiredString(conditional, "proof") : null));
		}

		Assert.NotEmpty(entries);
		return new LuaDirectApiPolicy(entries);
	}

	private static HashSet<string> LoadBridgeOperations()
	{
		using JsonDocument catalogue = LoadCatalogue();
		HashSet<string> operations = new(StringComparer.Ordinal);

		foreach (JsonElement operation in catalogue.RootElement.GetProperty("operations").EnumerateArray())
		{
			Assert.True(operations.Add(RequiredString(operation, "id")),
				"Bridge operation identifiers must be unique.");
		}

		return operations;
	}

	private static JsonDocument LoadCatalogue()
	{
		string path = RepositoryLayout.PathOf(CataloguePath);
		Assert.True(File.Exists(path), $"The direct Lua API policy catalogue is missing: '{path}'.");

		JsonDocument catalogue = JsonDocument.Parse(File.ReadAllText(path));
		Assert.Equal(1, catalogue.RootElement.GetProperty("schemaVersion").GetInt32());
		Assert.Equal("cheatengine-sdk-lua-protected-operations",
			RequiredString(catalogue.RootElement, "catalogId"));
		return catalogue;
	}

	private static List<GuardViolation> InspectProductionSources(LuaDirectApiPolicy policy)
	{
		string root = RepositoryLayout.PathOf(LibrariesPath);
		List<GuardViolation> violations = [];

		foreach (string sourcePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
		{
			string repositoryPath = GetRepositoryPath(sourcePath);
			if (repositoryPath.StartsWith(RawApiPath, StringComparison.Ordinal) || IsGeneratedPath(repositoryPath))
			{
				continue;
			}

			AddViolations(violations, policy, File.ReadAllText(sourcePath), repositoryPath);
		}

		return violations;
	}

	private static List<GuardViolation> InspectSource(LuaDirectApiPolicy policy, string source, string path)
	{
		List<GuardViolation> violations = [];
		AddViolations(violations, policy, source, path);
		return violations;
	}

	private static void AddViolations(List<GuardViolation> violations, LuaDirectApiPolicy policy, string source,
		string path)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
		CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
		HashSet<string> aliases = CollectLuaApiAliases(root);
		bool hasStaticLuaApiImport = HasStaticLuaApiImport(root);
		bool hasLuaApiNamespaceImport = HasLuaApiNamespaceImport(root);
		HashSet<string> shadowedNames = CollectPotentialSourceDeclarations(root);

		foreach (SyntaxNode node in root.DescendantNodes())
		{
			if (node is not InvocationExpressionSyntax invocation ||
				!TryGetLuaApiMemberName(invocation, aliases, hasStaticLuaApiImport, hasLuaApiNamespaceImport,
					shadowedNames, out string memberName) ||
				!policy.TryGet(memberName, out LuaDirectApiPolicyEntry entry) ||
				!entry.RequiresBridge ||
				entry.AllowedDirectly ||
				IsConditionallyAllowed(entry, invocation, path))
			{
				continue;
			}

			int line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
			violations.Add(new GuardViolation(path, line, memberName, entry.Reason));
		}
	}

	private static HashSet<string> CollectLuaApiAliases(CompilationUnitSyntax root)
	{
		HashSet<string> aliases = new(StringComparer.Ordinal);
		foreach (SyntaxNode node in root.DescendantNodesAndSelf())
		{
			if (node is not UsingDirectiveSyntax directive || directive.Alias is null || directive.Name is null ||
				!IsExactLuaApiTypeName(directive.Name.ToString()))
			{
				continue;
			}

			aliases.Add(directive.Alias.Name.Identifier.ValueText);
		}

		return aliases;
	}

	private static bool HasStaticLuaApiImport(CompilationUnitSyntax root)
	{
		foreach (SyntaxNode node in root.DescendantNodesAndSelf())
		{
			if (node is UsingDirectiveSyntax { Name: not null } directive &&
				directive.StaticKeyword.RawKind != 0 &&
				IsExactLuaApiTypeName(directive.Name.ToString()))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasLuaApiNamespaceImport(CompilationUnitSyntax root)
	{
		foreach (SyntaxNode node in root.DescendantNodesAndSelf())
		{
			if (node is UsingDirectiveSyntax { Alias: null, Name: not null } directive &&
				directive.StaticKeyword.RawKind == 0 &&
				IsLuaApiNamespaceName(directive.Name.ToString()))
			{
				return true;
			}
		}

		return false;
	}

	private static HashSet<string> CollectPotentialSourceDeclarations(CompilationUnitSyntax root)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (SyntaxNode node in root.DescendantNodes())
		{
			switch (node)
			{
				case MethodDeclarationSyntax method:
					names.Add(method.Identifier.ValueText);
					break;

				case LocalFunctionStatementSyntax localFunction:
					names.Add(localFunction.Identifier.ValueText);
					break;

				case VariableDeclaratorSyntax variable:
					names.Add(variable.Identifier.ValueText);
					break;

				case ParameterSyntax parameter:
					names.Add(parameter.Identifier.ValueText);
					break;
			}
		}

		return names;
	}

	private static bool TryGetLuaApiMemberName(InvocationExpressionSyntax invocation, HashSet<string> aliases,
		bool hasStaticLuaApiImport, bool hasLuaApiNamespaceImport, HashSet<string> shadowedNames,
		out string memberName)
	{
		memberName = string.Empty;
		switch (invocation.Expression)
		{
			case IdentifierNameSyntax identifier when hasStaticLuaApiImport &&
													  !shadowedNames.Contains(identifier.Identifier.ValueText):
				memberName = identifier.Identifier.ValueText;
				return true;

			case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } memberAccess:
				string typeName = memberAccess.Expression.ToString();
				if (IsExactLuaApiTypeName(typeName) || aliases.Contains(typeName) ||
					(hasLuaApiNamespaceImport && string.Equals(typeName, "LuaApi", StringComparison.Ordinal)))
				{
					memberName = name.Identifier.ValueText;
					return true;
				}

				return false;

			default:
				return false;
		}
	}

	private static bool IsConditionallyAllowed(LuaDirectApiPolicyEntry entry, InvocationExpressionSyntax invocation,
		string path)
	{
		if (!entry.ConditionalDirectUse || !entry.ConditionalAllowed ||
			!string.Equals(entry.MemberName, "lua_pushcclosure", StringComparison.Ordinal) ||
			!string.Equals(path, LightCFunctionFastPathSourcePath, StringComparison.Ordinal))
		{
			return false;
		}

		SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
		if (arguments.Count != 3 || !IsIdentifier(arguments[0].Expression, "Pointer") ||
			!IsIntegerZero(arguments[2].Expression))
		{
			return false;
		}

		ExpressionStatementSyntax? pushStatement = invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>();
		MethodDeclarationSyntax? method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
		if (pushStatement is null || method is null ||
			!string.Equals(method.Identifier.ValueText, "PushUncheckedFunction", StringComparison.Ordinal) ||
			method.Body is null)
		{
			return false;
		}

		SyntaxList<StatementSyntax> statements = method.Body.Statements;
		int statementIndex = -1;
		for (int index = 0; index < statements.Count; index++)
		{
			if (statements[index] == pushStatement)
			{
				statementIndex = index;
				break;
			}
		}

		return statementIndex > 0 && IsImmediateOneSlotCheckStackGuard(statements[statementIndex - 1]);
	}

	private static bool IsImmediateOneSlotCheckStackGuard(StatementSyntax statement)
	{
		if (statement is not IfStatementSyntax condition ||
			!ContainsOnlyThrow(condition.Statement) ||
			condition.Condition is not BinaryExpressionSyntax equals ||
			!equals.IsKind(SyntaxKind.EqualsExpression) ||
			!IsIntegerZero(equals.Right))
		{
			return false;
		}

		if (equals.Left is not InvocationExpressionSyntax
			{
				Expression: IdentifierNameSyntax { Identifier.ValueText: "lua_checkstack" },
				ArgumentList.Arguments: var arguments
			} || arguments.Count != 2 || !IsIdentifier(arguments[0].Expression, "Pointer") ||
			!IsIntegerOne(arguments[1].Expression))
		{
			return false;
		}

		return true;
	}

	private static bool ContainsOnlyThrow(StatementSyntax statement)
	{
		return statement is ThrowStatementSyntax ||
			   (statement is BlockSyntax { Statements.Count: 1 } block && block.Statements[0] is ThrowStatementSyntax);
	}

	private static bool IsIntegerZero(ExpressionSyntax expression)
	{
		if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
		{
			return false;
		}

		return literal.Token.Value switch
		{
			byte value => value == 0,
			sbyte value => value == 0,
			short value => value == 0,
			ushort value => value == 0,
			int value => value == 0,
			uint value => value == 0,
			long value => value == 0,
			ulong value => value == 0,
			_ => false
		};
	}

	private static bool IsIntegerOne(ExpressionSyntax expression)
	{
		if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
		{
			return false;
		}

		return literal.Token.Value switch
		{
			byte value => value == 1,
			sbyte value => value == 1,
			short value => value == 1,
			ushort value => value == 1,
			int value => value == 1,
			uint value => value == 1,
			long value => value == 1,
			ulong value => value == 1,
			_ => false
		};
	}

	private static bool IsIdentifier(ExpressionSyntax expression, string identifier)
	{
		return expression is IdentifierNameSyntax name &&
			   string.Equals(name.Identifier.ValueText, identifier, StringComparison.Ordinal);
	}

	private static string[] ReadProvenance(JsonElement entry)
	{
		List<string> provenance = [];
		foreach (JsonElement item in entry.GetProperty("provenance").EnumerateArray())
		{
			provenance.Add(RequiredString(item, "source"));
		}

		return [.. provenance];
	}

	private static string RequiredString(JsonElement element, string propertyName)
	{
		string? value = element.GetProperty(propertyName).GetString();
		Assert.False(string.IsNullOrWhiteSpace(value), $"'{propertyName}' must be a non-empty string.");
		return value!;
	}

	private static string GetRepositoryPath(string sourcePath)
	{
		return Path.GetRelativePath(RepositoryLayout.Root, sourcePath).Replace('\\', '/');
	}

	private static bool IsGeneratedPath(string repositoryPath)
	{
		return repositoryPath.Contains("/bin/", StringComparison.Ordinal) ||
			   repositoryPath.Contains("/obj/", StringComparison.Ordinal);
	}

	private static bool IsExactLuaApiTypeName(string typeName)
	{
		string normalized = typeName.Replace("global::", string.Empty, StringComparison.Ordinal);
		return string.Equals(normalized, LuaApiQualifiedName, StringComparison.Ordinal);
	}

	private static bool IsLuaApiNamespaceName(string namespaceName)
	{
		string normalized = namespaceName.Replace("global::", string.Empty, StringComparison.Ordinal);
		return string.Equals(normalized, "CheatEngine.SDK.Lua.Interop.Api", StringComparison.Ordinal);
	}

	private static string FormatViolations(List<GuardViolation> violations)
	{
		if (violations.Count == 0)
		{
			return string.Empty;
		}

		List<string> lines = ["Catalogued Lua APIs that can allocate or raise must run through the C11 bridge:"];
		for (int index = 0; index < violations.Count; index++)
		{
			GuardViolation violation = violations[index];
			lines.Add(
				$" - {violation.Path}:{violation.Line.ToString(CultureInfo.InvariantCulture)}: {violation.MemberName}: {violation.Reason}");
		}

		return string.Join(Environment.NewLine, lines);
	}

	private sealed class LuaDirectApiPolicy(List<LuaDirectApiPolicyEntry> entries)
	{
		private readonly Dictionary<string, LuaDirectApiPolicyEntry> _byMember = CreateIndex(entries);

		internal List<LuaDirectApiPolicyEntry> Entries
		{
			get;
		} = entries;

		internal bool TryGet(string memberName, out LuaDirectApiPolicyEntry entry)
		{
			return _byMember.TryGetValue(memberName, out entry!);
		}

		private static Dictionary<string, LuaDirectApiPolicyEntry> CreateIndex(List<LuaDirectApiPolicyEntry> entries)
		{
			Dictionary<string, LuaDirectApiPolicyEntry> index = new(StringComparer.Ordinal);
			for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
			{
				LuaDirectApiPolicyEntry entry = entries[entryIndex];
				Assert.True(index.TryAdd(entry.MemberName, entry),
					$"The direct Lua API policy has a duplicate member '{entry.MemberName}'.");
			}

			return index;
		}
	}

	private sealed record LuaDirectApiPolicyEntry(
		string ManagedSymbol,
		string MemberName,
		string Raises,
		bool AllowedDirectly,
		bool RequiresBridge,
		string Reason,
		string[] Provenance,
		string? BridgeOperation,
		bool ConditionalDirectUse,
		bool ConditionalAllowed,
		string? ConditionalWhen,
		string? ConditionalProof);

	private readonly record struct GuardViolation(string Path, int Line, string MemberName, string Reason);
}

using System;
using System.Collections.Immutable;

using CheatEngine.SDK.Analyzers.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CheatEngine.SDK.Analyzers.Usage;

/// <summary>
///     CESDK1020: a <c>CheatEngine.SDK.Engine.Runtime.PointerSize</c> built from the width of the plugin process
///     (<c>IntPtr.Size</c>, <c>sizeof(nint)</c>, <c>Environment.Is64BitProcess</c>, ...) instead of a Cheat Engine
///     observation of the target.
/// </summary>
/// <remarks>
///     <para>
///         The plugin always runs inside the 64-bit Cheat Engine process, so its own pointer width says nothing about
///         the target: an x86 target has 4-byte pointers, and Cheat Engine's configured pointer size can be any value
///         (audit A07-03). The rule reports two direct shapes only: a <c>new PointerSize(x)</c> whose argument, after
///         conversions, is a host-width source, and a conditional expression that selects <c>PointerSize.Bit64</c> or
///         <c>PointerSize.Bit32</c> on a condition that reads a host-width source. It does no dataflow: a width copied
///         into a local first is not followed.
///     </para>
///     <para>
///         <c>PointerSize</c> is resolved by metadata name and by its defining assembly <c>CheatEngine.SDK.Engine</c>; a
///         project-local lookalike is ignored, and a project that does not reference the Engine assembly registers
///         nothing. Host-address formatting and every other use of <c>IntPtr.Size</c> stay silent.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostWidthPointerSizeAnalyzer : DiagnosticAnalyzer
{
	private const string EngineAssemblyName = "CheatEngine.SDK.Engine";
	private const string PointerSizeMetadataName = "CheatEngine.SDK.Engine.Runtime.PointerSize";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
	{
		get;
	} = [DiagnosticDescriptors.HostWidthPointerSize];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		INamedTypeSymbol? pointerSize = ResolvePointerSize(context.Compilation);
		if (pointerSize is null)
		{
			return;
		}

		HostWidthSymbols symbols = new(
			pointerSize,
			context.Compilation.GetTypeByMetadataName("System.Environment"),
			context.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.Unsafe"),
			context.Compilation.GetTypeByMetadataName("System.Runtime.InteropServices.Marshal"));
		context.RegisterOperationAction(operationContext => AnalyzeObjectCreation(operationContext, symbols),
			OperationKind.ObjectCreation);
		context.RegisterOperationAction(operationContext => AnalyzeConditional(operationContext, symbols),
			OperationKind.Conditional);
	}

	private static INamedTypeSymbol? ResolvePointerSize(Compilation compilation)
	{
		foreach (MetadataReference reference in compilation.References)
		{
			if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly
			    && string.Equals(assembly.Identity.Name, EngineAssemblyName, StringComparison.Ordinal))
			{
				return assembly.GetTypeByMetadataName(PointerSizeMetadataName);
			}
		}

		return null;
	}

	private static void AnalyzeObjectCreation(OperationAnalysisContext context, HostWidthSymbols symbols)
	{
		IObjectCreationOperation creation = (IObjectCreationOperation) context.Operation;
		if (creation.Constructor is null
		    || !SymbolEqualityComparer.Default.Equals(creation.Constructor.ContainingType, symbols.PointerSize)
		    || creation.Arguments.Length != 1)
		{
			return;
		}

		IOperation argument = SkipConversions(creation.Arguments[0].Value);
		if (!IsHostWidthSource(argument, symbols))
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.HostWidthPointerSize,
			creation.Syntax.GetLocation(), argument.Syntax.ToString()));
	}

	private static void AnalyzeConditional(OperationAnalysisContext context, HostWidthSymbols symbols)
	{
		IConditionalOperation conditional = (IConditionalOperation) context.Operation;
		if (conditional.WhenFalse is null
		    || !SymbolEqualityComparer.Default.Equals(conditional.Type, symbols.PointerSize)
		    || !IsWidthConstant(conditional.WhenTrue, symbols)
		    || !IsWidthConstant(conditional.WhenFalse, symbols))
		{
			return;
		}

		IOperation? source = FindHostWidthSource(conditional.Condition, symbols);
		if (source is null)
		{
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.HostWidthPointerSize,
			conditional.Syntax.GetLocation(), source.Syntax.ToString()));
	}

	// PointerSize.Bit32 or PointerSize.Bit64, the two widths a host-width conditional chooses between.
	private static bool IsWidthConstant(IOperation operation, HostWidthSymbols symbols)
	{
		return SkipConversions(operation) is IPropertyReferenceOperation { Instance: null } property
		       && SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, symbols.PointerSize)
		       && property.Property.Name is "Bit32" or "Bit64";
	}

	private static IOperation? FindHostWidthSource(IOperation condition, HostWidthSymbols symbols)
	{
		if (IsHostWidthSource(condition, symbols) || IsIs64BitProcess(condition, symbols))
		{
			return condition;
		}

		foreach (IOperation descendant in condition.Descendants())
		{
			if (IsHostWidthSource(descendant, symbols) || IsIs64BitProcess(descendant, symbols))
			{
				return descendant;
			}
		}

		return null;
	}

	private static bool IsHostWidthSource(IOperation operation, HostWidthSymbols symbols)
	{
		switch (operation)
		{
			case IPropertyReferenceOperation { Instance: null } property:
				// IntPtr.Size and UIntPtr.Size; nint.Size and nuint.Size bind to the same properties.
				return string.Equals(property.Property.Name, "Size", StringComparison.Ordinal)
				       && IsNativeWidthType(property.Property.ContainingType);
			case ISizeOfOperation sizeOf:
				return IsNativeWidthType(sizeOf.TypeOperand) || sizeOf.TypeOperand is IPointerTypeSymbol;
			case IInvocationOperation invocation:
				return IsGenericSizeOf(invocation, symbols);
			default:
				return false;
		}
	}

	// Unsafe.SizeOf<nint>() and Marshal.SizeOf<IntPtr>() (and their unsigned and pointer forms).
	private static bool IsGenericSizeOf(IInvocationOperation invocation, HostWidthSymbols symbols)
	{
		IMethodSymbol method = invocation.TargetMethod;
		if (!string.Equals(method.Name, "SizeOf", StringComparison.Ordinal) || method.TypeArguments.Length != 1
		                                                                    || invocation.Arguments.Length != 0)
		{
			return false;
		}

		INamedTypeSymbol declaring = method.ContainingType;
		bool knownHelper = SymbolEqualityComparer.Default.Equals(declaring, symbols.Unsafe)
		                   || SymbolEqualityComparer.Default.Equals(declaring, symbols.Marshal);
		ITypeSymbol argument = method.TypeArguments[0];
		return knownHelper && (IsNativeWidthType(argument) || argument is IPointerTypeSymbol);
	}

	private static bool IsIs64BitProcess(IOperation operation, HostWidthSymbols symbols)
	{
		return operation is IPropertyReferenceOperation { Instance: null } property
		       && string.Equals(property.Property.Name, "Is64BitProcess", StringComparison.Ordinal)
		       && SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, symbols.Environment);
	}

	// System.IntPtr and System.UIntPtr, including their nint and nuint spellings.
	private static bool IsNativeWidthType(ITypeSymbol? type)
	{
		return type?.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
	}

	private static IOperation SkipConversions(IOperation operation)
	{
		while (operation is IConversionOperation conversion)
		{
			operation = conversion.Operand;
		}

		return operation;
	}

	private sealed class HostWidthSymbols
	{
		public HostWidthSymbols(INamedTypeSymbol pointerSize, INamedTypeSymbol? environment,
			INamedTypeSymbol? unsafeType, INamedTypeSymbol? marshal)
		{
			PointerSize = pointerSize;
			Environment = environment;
			Unsafe = unsafeType;
			Marshal = marshal;
		}

		public INamedTypeSymbol PointerSize
		{
			get;
		}

		public INamedTypeSymbol? Environment
		{
			get;
		}

		public INamedTypeSymbol? Unsafe
		{
			get;
		}

		public INamedTypeSymbol? Marshal
		{
			get;
		}
	}
}

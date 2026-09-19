using System.Reflection;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Managed;

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     Tests of the gate itself. <see cref="AbiShape" /> decides whether a structure added to
///     <c>CheatEngine.SDK.Abi</c> later is acceptable, so each rule it claims to enforce is proven here against a
///     deliberately wrong fixture: a gate that accepts everything would otherwise look exactly like a gate that works.
/// </summary>
public sealed unsafe class AbiShapeTests
{
    /// <summary>Byte-wide on purpose: an enumeration of the interface is 4 bytes.</summary>
    public enum ByteKind : byte
    {
        /// <summary>Only member.</summary>
        None
    }

    /// <summary>Four bytes wide, like every enumeration of the interface.</summary>
    public enum IntKind
    {
        /// <summary>Only member.</summary>
        None
    }

    /// <summary>The fixtures live here and borrow the two boolean types of the real assembly.</summary>
    private static readonly Assembly[] Trusted = [typeof(AbiShapeTests).Assembly, typeof(PluginInitRecord).Assembly];

    [Theory]
    [InlineData(typeof(Conforming))]
    [InlineData(typeof(ConformingInner))]
    [InlineData(typeof(SelfReferencing))]
    public void FindViolation_conforming_structure_returns_null(Type structure)
    {
        Assert.Null(AbiShape.FindViolation(structure, Trusted));
    }

    [Theory]
    [InlineData(typeof(BoolField), nameof(BoolField.Flag))]
    [InlineData(typeof(CharField), nameof(CharField.Letter))]
    [InlineData(typeof(ReferenceField), nameof(ReferenceField.Text))]
    [InlineData(typeof(ForeignValueTypeField), nameof(ForeignValueTypeField.Id))]
    [InlineData(typeof(ByteKindField), nameof(ByteKindField.Kind))]
    [InlineData(typeof(BoolInsideNestedStructure), nameof(BoolField.Flag))]
    [InlineData(typeof(BoolBehindStructurePointer), nameof(BoolField.Flag))]
    [InlineData(typeof(BoolBehindPointer), nameof(BoolBehindPointer.Flag))]
    [InlineData(typeof(ManagedFunctionPointer), nameof(ManagedFunctionPointer.Callback))]
    [InlineData(typeof(CdeclFunctionPointer), nameof(CdeclFunctionPointer.Callback))]
    [InlineData(typeof(UnspecifiedConventionFunctionPointer), nameof(UnspecifiedConventionFunctionPointer.Callback))]
    [InlineData(typeof(BoolParameter), nameof(BoolParameter.Callback))]
    [InlineData(typeof(CharResult), nameof(CharResult.Callback))]
    [InlineData(typeof(ByReferenceParameter), nameof(ByReferenceParameter.Callback))]
    [InlineData(typeof(BoolBehindPointerParameter), nameof(BoolBehindPointerParameter.Callback))]
    [InlineData(typeof(NestedFunctionPointerWithoutStdcall), nameof(NestedFunctionPointerWithoutStdcall.Callback))]
    public void FindViolation_forbidden_shape_names_the_offending_field(Type structure, string offendingField)
    {
        var violation = AbiShape.FindViolation(structure, Trusted);

        Assert.NotNull(violation);
        Assert.Contains(offendingField, violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FindViolation_explicit_layout_is_rejected()
    {
        var violation = AbiShape.FindViolation(typeof(ExplicitLayout), Trusted);

        Assert.NotNull(violation);
        Assert.Contains("sequential", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindViolation_structure_of_another_assembly_is_foreign()
    {
        // Same fixture, other home: a structure is only trusted when it belongs to the assembly under test.
        var violation = AbiShape.FindViolation(typeof(Conforming), typeof(PluginInitRecord).Assembly);

        Assert.NotNull(violation);
        Assert.Contains(nameof(Conforming.Inner), violation, StringComparison.Ordinal);
    }

    /// <summary>Every shape the interface really uses, in one record.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Conforming
    {
        public byte* Text;
        public void* Opaque;
        public nint Handle;
        public nuint Address;
        public int Size;
        public uint Version;
        public double Number;
        public IntKind Kind;
        public ConformingInner Inner;
        public ConformingInner* InnerPointer;
        public delegate* unmanaged[Stdcall]<void> Notify;
        public delegate* unmanaged[Stdcall]<void*> GetState;
        public delegate* unmanaged[Stdcall]<ConformingInner*, int, Bool32> Query;
        public delegate* unmanaged[Stdcall]<nuint, byte**, Bool32*, IntKind, Bool8> Popup;
        public delegate* unmanaged[Stdcall]<delegate* unmanaged[Stdcall]<int, void>, void> Register;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ConformingInner
    {
        public long Value;
        public Bool32 Flag;
    }

    /// <summary>A pointer cycle must not send the gate into infinite recursion.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SelfReferencing
    {
        public SelfReferencing* Next;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoolField
    {
        public bool Flag;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CharField
    {
        public char Letter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ReferenceField
    {
        public string? Text;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ForeignValueTypeField
    {
        public Guid Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ByteKindField
    {
        public ByteKind Kind;
    }

    /// <summary>A forbidden field one level down is found, not accepted unseen.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoolInsideNestedStructure
    {
        public nint Handle;
        public BoolField Inner;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoolBehindStructurePointer
    {
        public BoolField* Inner;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoolBehindPointer
    {
        public bool* Flag;
    }

    /// <summary>A function-pointer field is judged by its signature, not accepted without looking.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ManagedFunctionPointer
    {
        public delegate*<int, int> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CdeclFunctionPointer
    {
        public delegate* unmanaged[Cdecl]<int, int> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnspecifiedConventionFunctionPointer
    {
        public delegate* unmanaged<int, int> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoolParameter
    {
        public delegate* unmanaged[Stdcall]<bool, int> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CharResult
    {
        public delegate* unmanaged[Stdcall]<int, char> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ByReferenceParameter
    {
        public delegate* unmanaged[Stdcall]<ref int, void> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoolBehindPointerParameter
    {
        public delegate* unmanaged[Stdcall]<bool*, void> Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NestedFunctionPointerWithoutStdcall
    {
        public delegate* unmanaged[Stdcall]<delegate* unmanaged[Cdecl]<int, void>, void> Callback;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ExplicitLayout
    {
        [FieldOffset(0)] public int Low;

        [FieldOffset(4)] public int High;
    }
}

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>Helpers shared by the layout tests.</summary>
internal static unsafe class Layout
{
    /// <summary>Skip reason for the tests whose expected numbers are the 64-bit ones.</summary>
    public const string Requires64BitProcess =
        "The expected sizes and offsets are the 64-bit ones (Cheat Engine x64); this test process is 32-bit.";

    /// <summary>Gets a value indicating whether the size/offset expectations apply to this process.</summary>
    public static bool Is64BitProcess => Environment.Is64BitProcess;

    /// <summary>
    ///     Managed size of <typeparamref name="T" />. The constraint is part of the assertion: a structure that
    ///     contained a reference would not compile here.
    /// </summary>
    public static int SizeOf<T>()
        where T : unmanaged
    {
        return sizeof(T);
    }

    /// <summary>Byte distance between the start of a structure and one of its fields.</summary>
    public static int OffsetOf(void* origin, void* field)
    {
        return checked((int)((byte*)field - (byte*)origin));
    }

    /// <summary>Managed alignment of an unmanaged value, measured as the offset after a leading byte.</summary>
    public static int AlignmentOf<T>()
        where T : unmanaged
    {
        AlignmentProbe<T> probe = default;
        return OffsetOf(&probe, &probe.Value);
    }

    private struct AlignmentProbe<T>
        where T : unmanaged
    {
        public byte Prefix;
        public T Value;
    }
}

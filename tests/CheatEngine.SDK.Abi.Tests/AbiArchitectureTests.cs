using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Tests;

public sealed class AbiArchitectureTests
{
    [Theory]
    [InlineData(Architecture.X64, true)]
    [InlineData(Architecture.X86, false)]
    [InlineData(Architecture.Arm64, false)]
    [InlineData(Architecture.Arm, false)]
    public void IsSupportedArchitecture_is_true_for_x64_only(Architecture architecture, bool expected)
    {
        Assert.Equal(expected, AbiArchitecture.IsSupportedArchitecture(architecture));
    }

    [Fact]
    public void IsSupported_reflects_the_architecture_of_the_current_process()
    {
        var expected = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        Assert.Equal(expected, AbiArchitecture.IsSupported);
    }

    [Fact]
    public void ThrowIfUnsupported_for_x64_does_not_throw()
    {
        AbiArchitecture.ThrowIfUnsupported(Architecture.X64);
    }

    [Fact]
    public void ThrowIfUnsupported_for_another_architecture_throws_and_names_it()
    {
        var exception =
            Assert.Throws<PlatformNotSupportedException>(static () =>
                AbiArchitecture.ThrowIfUnsupported(Architecture.X86));

        Assert.Contains("X86", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowIfUnsupported_for_the_current_process_agrees_with_IsSupported()
    {
        var exception = Record.Exception(AbiArchitecture.ThrowIfUnsupported);

        Assert.Equal(AbiArchitecture.IsSupported, exception is null);
    }
}

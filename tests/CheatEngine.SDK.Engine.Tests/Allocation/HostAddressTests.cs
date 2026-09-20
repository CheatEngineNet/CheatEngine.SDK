using System.Globalization;
using System.Reflection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     The host-pointer value type must remain explicitly separate from a target-process <see cref="Address" />.
/// </summary>
public sealed class HostAddressTests
{
    [Fact]
    public void HostAddress_wraps_only_a_native_host_value()
    {
        nuint value = unchecked((nuint)0x7FF6_4000_0000UL);
        HostAddress address = new(value);

        Assert.Equal(value, address.Value);
        Assert.False(address.IsZero);
        Assert.Equal("00007FF640000000", address.ToString("X16", CultureInfo.InvariantCulture));
        Assert.True(HostAddress.Zero.IsZero);
        Assert.NotEqual(typeof(HostAddress), typeof(Address));
    }

    [Fact]
    public void HostAddress_declares_no_conversion_operator_to_or_from_target_Address()
    {
        MethodInfo[] methods = typeof(HostAddress).GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo method in methods)
        {
            if (method.Name is not "op_Implicit" and not "op_Explicit") continue;

            Assert.NotEqual(typeof(Address), method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
                Assert.NotEqual(typeof(Address), parameter.ParameterType);
        }
    }
}

using System.Globalization;
using System.Text;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Values;

/// <summary>The address value type without any Lua: parsing, formatting, culture invariance, conversions and arithmetic.</summary>
public sealed class AddressTests
{
    [Theory]
    [InlineData("00400000", 0x400000UL)]
    [InlineData("400000", 0x400000UL)]
    [InlineData("0x400000", 0x400000UL)]
    [InlineData("0X400000", 0x400000UL)]
    [InlineData("0x0", 0UL)]
    [InlineData("0", 0UL)]
    [InlineData("000000000000000000000000001", 1UL)]
    [InlineData("7FF6A1B2C3D4", 0x7FF6A1B2C3D4UL)]
    [InlineData("7ff6a1b2c3d4", 0x7FF6A1B2C3D4UL)]
    [InlineData("FFFFFFFFFFFFFFFF", ulong.MaxValue)]
    [InlineData("0xFFFFFFFFFFFFFFFF", ulong.MaxValue)]
    [InlineData("  0x10  ", 0x10UL)]
    [InlineData("\t10\r\n", 0x10UL)]
    [InlineData("10", 0x10UL)]
    public void TryParse_accepts_hexadecimal_text_with_an_optional_prefix(string text, ulong expected)
    {
        Assert.True(Address.TryParse(text, out var fromChars));
        Assert.Equal(expected, fromChars.Value);

        Assert.True(Address.TryParse(Encoding.UTF8.GetBytes(text), out var fromBytes));
        Assert.Equal(expected, fromBytes.Value);

        Assert.Equal(new Address(expected), Address.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("0x ")]
    [InlineData("x10")]
    [InlineData("-10")]
    [InlineData("+10")]
    [InlineData("10h")]
    [InlineData("$10")]
    [InlineData("1 0")]
    [InlineData("0x0x10")]
    [InlineData("1G")]
    [InlineData("10000000000000000")]
    [InlineData("0x10000000000000000")]
    [InlineData("1.5")]
    [InlineData("kernel32.dll+10")]
    [InlineData("\u0661\u0660")]
    [InlineData("\u00a010")]
    [InlineData("10\u2003")]
    public void TryParse_rejects_text_that_is_not_a_hexadecimal_address(string text)
    {
        Assert.False(Address.TryParse(text, out var fromChars));
        Assert.Equal(Address.Zero, fromChars);

        Assert.False(Address.TryParse(Encoding.UTF8.GetBytes(text), out var fromBytes));
        Assert.Equal(Address.Zero, fromBytes);

        Assert.Throws<FormatException>(() => Address.Parse(text));
    }

    [Fact]
    public void TryParse_of_a_null_string_is_a_failure_and_Parse_throws()
    {
        Assert.False(Address.TryParse((string?)null, out var address));
        Assert.Equal(Address.Zero, address);
        Assert.Throws<ArgumentNullException>(() => Address.Parse(null!));
    }

    [Theory]
    [InlineData(0UL, "00000000")]
    [InlineData(0x400000UL, "00400000")]
    [InlineData(0xFFFFFFFFUL, "FFFFFFFF")]
    [InlineData(0x100000000UL, "0000000100000000")]
    [InlineData(0x7FF6A1B2C3D4UL, "00007FF6A1B2C3D4")]
    [InlineData(ulong.MaxValue, "FFFFFFFFFFFFFFFF")]
    public void ToString_uses_Cheat_Engines_padded_uppercase_convention(ulong value, string expected)
    {
        Address address = value;
#pragma warning disable MA0011 // The parameterless ToString() is the member under test; it is culture-invariant by design.
        Assert.Equal(expected, address.ToString());
#pragma warning restore MA0011
        Assert.Equal(expected, address.ToString(null, null));
        Assert.Equal(expected, address.ToString("G", CultureInfo.InvariantCulture));
        Assert.Equal(expected, address.ToString(string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(expected, string.Create(CultureInfo.InvariantCulture, $"{address}"));
        Assert.Equal(expected, string.Format(CultureInfo.InvariantCulture, "{0}", address));
    }

    [Theory]
    [InlineData(0x400000UL, "X", "400000")]
    [InlineData(0x400000UL, "x", "400000")]
    [InlineData(0xABCUL, "x", "abc")]
    [InlineData(0xABCUL, "X8", "00000ABC")]
    [InlineData(0xABCUL, "x16", "0000000000000abc")]
    [InlineData(0xABCUL, "X2", "ABC")]
    [InlineData(0UL, "X", "0")]
    public void ToString_with_a_hexadecimal_format_behaves_like_ulong(ulong value, string format, string expected)
    {
        Address address = value;
        Assert.Equal(expected, address.ToString(format, CultureInfo.InvariantCulture));
        Assert.Equal(expected, string.Format(CultureInfo.InvariantCulture, "{0:" + format + "}", address));
        Assert.Equal(value.ToString(format, CultureInfo.InvariantCulture), address.ToString(format, null));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("XG")]
    [InlineData("X8X")]
    [InlineData("0x")]
    [InlineData("P")]
    public void ToString_with_an_unsupported_format_throws(string format)
    {
        Address address = 0x10;
        Assert.Throws<FormatException>(() => address.ToString(format, CultureInfo.InvariantCulture));
        Assert.Throws<FormatException>(() => address.TryFormat(new char[32], out _, format, null));
    }

    [Fact]
    public void TryFormat_writes_utf16_and_utf8_and_reports_a_small_buffer()
    {
        Address address = 0x7FF6A1B2C3D4;

        Span<char> chars = stackalloc char[16];
        Assert.True(address.TryFormat(chars, out var charsWritten, default, null));
        Assert.Equal("00007FF6A1B2C3D4", chars[..charsWritten].ToString());
        Assert.True(address.TryFormat(chars, out charsWritten, "x", null));
        Assert.Equal("7ff6a1b2c3d4", chars[..charsWritten].ToString());
        Assert.False(address.TryFormat(chars[..4], out charsWritten, default, null));
        Assert.Equal(0, charsWritten);

        Span<byte> bytes = stackalloc byte[16];
        Assert.True(address.TryFormat(bytes, out var bytesWritten, default, null));
        Assert.True(bytes[..bytesWritten].SequenceEqual("00007FF6A1B2C3D4"u8));
        Assert.True(address.TryFormat(bytes, out bytesWritten, "X", null));
        Assert.True(bytes[..bytesWritten].SequenceEqual("7FF6A1B2C3D4"u8));
        Assert.False(address.TryFormat(bytes[..4], out bytesWritten, default, null));
        Assert.Equal(0, bytesWritten);

        // What TryFormat writes parses back to the same address.
        Assert.True(address.TryFormat(bytes, out bytesWritten, default, null));
        Assert.True(Address.TryParse(bytes[..bytesWritten], out var roundTripped));
        Assert.Equal(address, roundTripped);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void Parsing_and_formatting_do_not_depend_on_the_current_culture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var address = Address.Parse("0x7ff6a1b2c3d4");
            Assert.Equal(0x7FF6A1B2C3D4UL, address.Value);
#pragma warning disable MA0011 // The parameterless ToString() under a foreign culture is the point of this test.
            Assert.Equal("00007FF6A1B2C3D4", address.ToString());
#pragma warning restore MA0011
            Assert.Equal("7ff6a1b2c3d4", address.ToString("x", culture));
            Assert.Equal("00007FF6A1B2C3D4", string.Format(culture, "{0}", address));
            Assert.True(Address.TryParse("00000000000000FF", out var parsed));
            Assert.Equal(255UL, parsed.Value);

            // The Turkish dotless i must not affect the 'x' prefix test, and Arabic-Indic digits are never digits here.
            Assert.True(Address.TryParse("0X1F", out parsed));
            Assert.Equal(0x1FUL, parsed.Value);
            Assert.False(Address.TryParse("\u0660\u0661", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Conversions_keep_the_bits()
    {
        var high = Address.FromInt64(-1);
        Assert.Equal(ulong.MaxValue, high.Value);
        Assert.Equal(-1L, high.ToInt64());
        Assert.Equal(ulong.MaxValue, high.ToUInt64());
        Assert.Equal(ulong.MaxValue, (ulong)high);

        var low = Address.FromUInt64(0x400000);
        Assert.Equal(0x400000L, low.ToInt64());
        Address implicitlyConverted = 0x400000UL;
        Assert.Equal(low, implicitlyConverted);
        Assert.Equal(low, Address.FromInt64(0x400000));

        Assert.True(Address.Zero.IsZero);
        Assert.False(low.IsZero);
        Assert.Equal(default, Address.Zero);
    }

    [Fact]
    public void Arithmetic_offsets_and_wraps_like_a_pointer()
    {
        Address address = 0x1000;
        Assert.Equal(0x1010UL, (address + 0x10).Value);
        Assert.Equal(0x0FF0UL, (address - 0x10).Value);
        Assert.Equal(0x0FF0UL, (address + -0x10).Value);
        Assert.Equal(0x1010UL, address.Add(0x10).Value);
        Assert.Equal(0x0FF0UL, address.Subtract(0x10).Value);

        Address top = ulong.MaxValue;
        Assert.Equal(Address.Zero, top + 1);
        Assert.Equal(top, Address.Zero - 1);
    }

    [Fact]
    public void Equality_ordering_and_hashing_follow_the_unsigned_value()
    {
        Address a = 0x10;
        Address b = 0x10;
        Address c = 0x20;
        Address high = ulong.MaxValue;

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(null));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.True(a < c);
        Assert.True(c > a);
        Assert.True(a <= b);
        Assert.True(a >= b);
        Assert.True(c < high);
        Assert.True(a.CompareTo(c) < 0);
        Assert.True(c.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(1, a.CompareTo(null));
        Assert.Throws<ArgumentException>(() => a.CompareTo("not an address"));

        Address[] sorted = [high, c, a];
        Array.Sort(sorted);
        Assert.Equal([a, c, high], sorted);
    }
}

using CheatEngine.SDK.Hosting.Bootstrap;

namespace CheatEngine.SDK.Hosting.Tests.Bootstrap;

/// <summary>The name buffer on its own: ASCII copy, NUL handling, empty input. No Lua needed.</summary>
public sealed unsafe class AnsiNameBufferTests
{
    [Fact]
    public void ASCII_is_copied_byte_for_byte_with_a_terminating_NUL()
    {
        var buffer = AnsiNameBuffer.Allocate("My Trainer"u8);

        Assert.True(AnsiNameBuffer.Read(buffer).SequenceEqual("My Trainer"u8));
        Assert.Equal(0, buffer[10]);
    }

    [Fact]
    public void An_embedded_NUL_ends_the_name()
    {
        var buffer = AnsiNameBuffer.Allocate("Cut\0Here"u8);

        Assert.True(AnsiNameBuffer.Read(buffer).SequenceEqual("Cut"u8));
    }

    [Fact]
    public void An_empty_name_is_one_NUL()
    {
        var buffer = AnsiNameBuffer.Allocate(default);

        Assert.True(buffer is not null);
        Assert.Equal(0, buffer[0]);
        Assert.True(AnsiNameBuffer.Read(buffer).IsEmpty);
    }

    [Fact]
    public void Reading_a_null_buffer_is_empty()
    {
        Assert.True(AnsiNameBuffer.Read(null).IsEmpty);
    }
}

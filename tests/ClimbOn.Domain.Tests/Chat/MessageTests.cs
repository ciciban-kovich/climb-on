using ClimbOn.Domain.Chat;
using ClimbOn.Domain.Errors;
using Xunit;

namespace ClimbOn.Domain.Tests.Chat;

// S119's bounds, below the JSON layer, which refuses a lone surrogate itself.
public sealed class MessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // A fact, not a theory: test data with a lone surrogate does not survive serialisation by the runner.
    [Fact]
    [Trait("Spec", "S119")]
    public void Unstorable_text_is_refused() =>
        Assert.All(
            new[] { "x \ud800 y", "x \udc00 y", "x \udc00\ud800 y", "\ud800", "x\0" },
            text => Assert.Equal(Message.InvalidCode, Assert.Throws<DomainError>(() => Write(text)).Code));

    [Theory]
    [Trait("Spec", "S119")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t  ")]
    public void Blank_text_is_refused(string? text) =>
        Assert.Equal(Message.EmptyCode, Assert.Throws<DomainError>(() => Write(text)).Code);

    // Counted in code points: an astral character is one, though it is two UTF-16 units.
    [Fact]
    [Trait("Spec", "S119")]
    public void Length_is_counted_in_code_points()
    {
        var astral = string.Concat(Enumerable.Repeat("\U0001F9D7", Message.MaximumLength));

        Assert.Equal(astral, Write(astral).Text);
        Assert.Equal(Message.TooLongCode, Assert.Throws<DomainError>(() => Write(astral + "x")).Code);
        Assert.Equal(Message.TooLongCode, Assert.Throws<DomainError>(() => Write(new string('x', Message.MaximumLength + 1))).Code);
    }

    // Counted exactly as written: trailing whitespace is not trimmed away and a combining accent is not composed
    // first, so each refused text below would fit if it were trimmed or normalised.
    [Theory]
    [Trait("Spec", "S119")]
    [InlineData(" ")]
    [InlineData("\r\n")]
    public void Trailing_whitespace_is_counted(string tail)
    {
        var fits = new string('x', Message.MaximumLength - tail.Length) + tail;

        Assert.Equal(fits, Write(fits).Text);
        Assert.Equal(Message.TooLongCode, Assert.Throws<DomainError>(() => Write(new string('x', Message.MaximumLength) + tail)).Code);
    }

    [Fact]
    [Trait("Spec", "S119")]
    public void Combining_accent_is_counted_as_written()
    {
        // "e" + U+0301 is two code points; in NFC it is one, so the refused 4001 code points would be 2001 if normalised.
        var fits = string.Concat(Enumerable.Repeat("e\u0301", Message.MaximumLength / 2));

        Assert.Equal(fits, Write(fits).Text);
        Assert.Equal(Message.TooLongCode, Assert.Throws<DomainError>(() => Write(fits + "x")).Code);
    }

    // S115: kept exactly, a real U+FFFD and surrounding whitespace included.
    [Fact]
    [Trait("SpecPartial", "S115")]
    public void Text_is_kept_exactly()
    {
        const string text = "  � é <b>x</b>\r\n";

        Assert.Equal(text, Write(text).Text);
    }

    private static Message Write(string? text) => Message.Write(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text, Now);
}

using GifMaker.Core;

namespace GifMaker.X11;

public readonly record struct X11DisplayName
{
    private readonly string? _value;

    private X11DisplayName(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("DISPLAY is not set");

    public static Result<X11DisplayName> Current()
    {
        var value = Environment.GetEnvironmentVariable("DISPLAY");
        return string.IsNullOrWhiteSpace(value)
            ? Result<X11DisplayName>.Fail("DISPLAY is not set")
            : Result<X11DisplayName>.Ok(new X11DisplayName(value));
    }

    public string FormatInput(ScreenRegion region)
    {
        Span<char> xChars = stackalloc char[11];
        Span<char> yChars = stackalloc char[11];
        region.X.TryFormat(xChars, out var xLen);
        region.Y.TryFormat(yChars, out var yLen);

        var display = Value;
        return string.Create(display.Length + 1 + xLen + 1 + yLen, (display, region.X, region.Y), static (span, state) =>
        {
            state.display.CopyTo(span);
            var pos = state.display.Length;
            span[pos++] = '+';
            state.X.TryFormat(span[pos..], out var written);
            pos += written;
            span[pos++] = ',';
            state.Y.TryFormat(span[pos..], out _);
        });
    }
}

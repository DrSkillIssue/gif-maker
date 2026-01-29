using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace GifMaker.Recording;

/// <summary>
/// Fixed-size inline array for FFmpeg recording command arguments.
/// </summary>
/// <remarks>
/// Uses C# 12 <see cref="InlineArrayAttribute"/> for stack allocation.
/// Capacity of 18 covers all FFmpeg recording arguments with headroom.
/// </remarks>
[InlineArray(Capacity)]
internal struct RecordingArgumentStorage
{
    /// <summary>Maximum number of arguments supported.</summary>
    internal const int Capacity = 18;

    private string _element;
}

/// <summary>
/// Stack-allocated argument list for building FFmpeg commands with zero heap allocation.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="RecordingArgumentStorage"/> inline array.
/// All operations are O(1) with no GC pressure in steady state.
/// </para>
/// <para>
/// Typical FFmpeg recording command uses ~17 arguments:
/// -y -f x11grab -framerate {fps} -video_size {WxH} -i {display+x,y}
/// -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p {output}
/// </para>
/// </remarks>
internal ref struct RecordingArgumentList
{
    private RecordingArgumentStorage _storage;
    private int _count;

    /// <summary>Adds an argument to the list.</summary>
    /// <param name="arg">Argument string to add.</param>
    /// <exception cref="InvalidOperationException">Capacity exceeded.</exception>
    public void Add(string arg)
    {
        if (_count >= RecordingArgumentStorage.Capacity)
            ThrowOverflow();
        _storage[_count++] = arg;
    }

    /// <summary>Returns the arguments as a read-only span.</summary>
    /// <returns>Span containing all added arguments.</returns>
    [UnscopedRef]
    public readonly ReadOnlySpan<string> AsSpan() => ((ReadOnlySpan<string>)_storage)[.._count];

    [DoesNotReturn]
    private static void ThrowOverflow() =>
        throw new InvalidOperationException(
            $"Argument buffer overflow - max {RecordingArgumentStorage.Capacity} arguments supported");
}

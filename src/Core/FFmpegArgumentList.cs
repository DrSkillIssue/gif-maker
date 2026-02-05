using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace GifMaker.Core;

/// <summary>
/// Fixed-size inline array for FFmpeg command arguments.
/// </summary>
/// <remarks>
/// Uses C# 12 <see cref="InlineArrayAttribute"/> for stack allocation.
/// Capacity of 24 covers all FFmpeg recording/conversion arguments with headroom.
/// </remarks>
[InlineArray(Capacity)]
internal struct FFmpegArgumentStorage
{
    /// <summary>Maximum number of arguments supported.</summary>
    internal const int Capacity = 24;

    private string _element;
}

/// <summary>
/// Stack-allocated argument list for building FFmpeg commands with zero heap allocation.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="FFmpegArgumentStorage"/> inline array.
/// All operations are O(1) with no GC pressure in steady state.
/// </para>
/// <para>
/// Typical usage:
/// - Recording: ~17 args (-y -f x11grab -framerate {fps} -video_size {WxH} ...)
/// - Conversion: ~18 args for MP4/WebM with scaling
/// </para>
/// </remarks>
public ref struct FFmpegArgumentList
{
    private FFmpegArgumentStorage _storage;
    private int _count;

    /// <summary>Adds an argument to the list.</summary>
    /// <param name="arg">Argument string to add.</param>
    /// <exception cref="InvalidOperationException">Capacity exceeded.</exception>
    public void Add(string arg)
    {
        if (_count >= FFmpegArgumentStorage.Capacity)
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
            $"FFmpeg argument buffer overflow - max {FFmpegArgumentStorage.Capacity} arguments supported");
}

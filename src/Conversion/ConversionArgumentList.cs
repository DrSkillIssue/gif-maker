using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace GifMaker.Conversion;

/// <summary>
/// Fixed-size inline array for FFmpeg conversion command arguments.
/// </summary>
/// <remarks>
/// Uses C# 12 <see cref="InlineArrayAttribute"/> for stack allocation.
/// Capacity of 20 covers all FFmpeg conversion arguments with headroom.
/// </remarks>
[InlineArray(Capacity)]
internal struct ArgumentStorage
{
    /// <summary>Maximum number of arguments supported.</summary>
    internal const int Capacity = 20;

    private string _element;
}

/// <summary>
/// Stack-allocated argument list for building FFmpeg commands with zero heap allocation.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="ArgumentStorage"/> inline array.
/// All operations are O(1) with no GC pressure in steady state.
/// </para>
/// <para>
/// Typical FFmpeg conversion commands use up to ~18 arguments for MP4/WebM with scaling.
/// GIF commands are shorter but use complex filter strings.
/// </para>
/// </remarks>
internal ref struct ArgumentList
{
    private ArgumentStorage _storage;
    private int _count;

    /// <summary>Adds an argument to the list.</summary>
    /// <param name="arg">Argument string to add.</param>
    /// <exception cref="InvalidOperationException">Capacity exceeded.</exception>
    public void Add(string arg)
    {
        if (_count >= ArgumentStorage.Capacity)
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
            $"Argument buffer overflow - max {ArgumentStorage.Capacity} arguments supported");
}

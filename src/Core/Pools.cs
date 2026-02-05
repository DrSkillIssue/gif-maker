using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace GifMaker.Core;

/// <summary>
/// Shared object pools for zero-allocation hot paths.
/// </summary>
public static class Pools
{
    /// <summary>
    /// Pooled StringBuilder for string accumulation (e.g., stderr reading).
    /// </summary>
    /// <remarks>
    /// Initial capacity: 4KB (typical stderr chunks).
    /// Max retained: 64KB per instance.
    /// Pool size: ProcessorCount * 2 (allows concurrent recording + conversion).
    /// </remarks>
    public static readonly ObjectPool<StringBuilder> StringBuilder =
        new DefaultObjectPoolProvider { MaximumRetained = Environment.ProcessorCount * 2 }
            .CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);
}

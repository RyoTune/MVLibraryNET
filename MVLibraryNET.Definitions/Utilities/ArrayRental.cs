using System.Buffers;
using System.Runtime.InteropServices;

namespace MVLibraryNET.Definitions.Utilities;

/// <summary>
/// Represents a temporary array rental from the runtime's ArrayPool.
/// By Sewer56, from CriFsV2Lib
/// </summary>
public readonly struct ArrayRental : IDisposable
{
    /// <summary>
    /// Max Arrays for 32-bit item.
    /// </summary>
    private const int Max32BitItemsPerBucket = 1;

    /// <summary>
    /// Empty rental.
    /// </summary>
    public static readonly ArrayRental Empty;

    static ArrayRental()
    {
        Reset();
        Empty = new(0, false);
    }

    private readonly byte[] _data;
    private readonly int _count;
    private readonly bool _canDispose;
    private static ArrayPool<byte> _dataPool = null!;

    /// <summary>
    /// Resets the data pool allowing memory to be reclaimed.
    /// </summary>
    public static void Reset() 
    {
        if (IntPtr.Size == 4)
            _dataPool = ArrayPool<byte>.Create(1024 * 1024 * Max32BitItemsPerBucket, 16); // 1 x 16MB
        else
            _dataPool = ArrayPool<byte>.Create(1024 * 1024 * 64, Environment.ProcessorCount); // 64MB
    }

    /// <summary>
    /// Rents an array of bytes from the array pool.
    /// </summary>
    /// <param name="count">Needed amount of bytes.</param>
    public ArrayRental(int count) : this(count, true) { }

    /// <summary>
    /// Rents an array of bytes from the array pool.
    /// </summary>
    /// <param name="count">Needed amount of bytes.</param>
    /// <param name="canDispose">True if this rental can be disposed, set false for internal use only.</param>
    private ArrayRental(int count, bool canDispose)
    {
        _data = _dataPool.Rent(count);
        _count = count;
        _canDispose = canDispose;
    }

    /// <summary>
    /// Exposes the raw underlying array, which will likely
    /// be bigger than the number of elements.
    /// </summary>
    public byte[] RawArray => _data;

    /// <summary>
    /// Returns the rented array as a span.
    /// </summary>
    public Span<byte> Span => MemoryMarshal.CreateSpan(ref MemoryMarshal.GetArrayDataReference(_data), _count);

    /// <summary>
    /// Exposes the number of elements stored by this structure.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Returns the array to the pool.
    /// </summary>
    public void Dispose()
    {
        if (_canDispose)
            _dataPool.Return(_data);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics;

/// <summary>
/// https://gist.github.com/ahsonkhan/c76a1cc4dc7107537c3fdc0079a68b35
/// Standard ArrayBufferWriter is not using pooled memory
/// </summary>
internal class RentArrayBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int MINIMUM_BUFFER_SIZE = 256;

    private byte[]? _rentedBuffer;
    private int _written;
    private long _committed;

    private readonly bool _cleanRentedBuffers;

    public RentArrayBufferWriter(int initialCapacity = MINIMUM_BUFFER_SIZE, bool cleanRentedBuffers = true)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentException(null, nameof(initialCapacity));
        }
        _cleanRentedBuffers = cleanRentedBuffers;

        _rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _written = 0;
        _committed = 0;
    }

    public (byte[], int) WrittenBuffer
    {
        get
        {
            CheckIfDisposed();

            return (_rentedBuffer!, _written);
        }
    }

    public Memory<byte> WrittenMemory
    {
        get
        {
            CheckIfDisposed();

            return _rentedBuffer.AsMemory(0, _written);
        }
    }

    public Span<byte> WrittenSpan
    {
        get
        {
            CheckIfDisposed();

            return _rentedBuffer.AsSpan(0, _written);
        }
    }

    public int BytesWritten
    {
        get
        {
            CheckIfDisposed();

            return _written;
        }
    }

    public long BytesCommitted
    {
        get
        {
            CheckIfDisposed();

            return _committed;
        }
    }

    public void Clear()
    {
        CheckIfDisposed();

        ClearHelper();
    }

    private void ClearHelper()
    {
        if (_cleanRentedBuffers)
        {
            _rentedBuffer.AsSpan(0, _written).Clear();

        }
        _written = 0;
    }

    public async Task CopyToAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        CheckIfDisposed();

        ArgumentNullException.ThrowIfNull(stream);

        await stream.WriteAsync(new Memory<byte>(_rentedBuffer, 0, _written), cancellationToken).ConfigureAwait(false);
        _committed += _written;

        ClearHelper();
    }

    public void CopyTo(Stream stream)
    {
        CheckIfDisposed();

        ArgumentNullException.ThrowIfNull(stream);

        stream.Write(_rentedBuffer!, 0, _written);
        _committed += _written;

        ClearHelper();
    }

    public void Advance(int count)
    {
        CheckIfDisposed();

        ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);

        if (_written > _rentedBuffer!.Length - count)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        _written += count;
    }

    // Returns the rented buffer back to the pool
    public void Dispose()
    {
        if (_rentedBuffer == null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_rentedBuffer, clearArray: _cleanRentedBuffers);
        _rentedBuffer = null;
        _written = 0;
    }

    private void CheckIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_rentedBuffer == null, this);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        CheckIfDisposed();

        ArgumentOutOfRangeException.ThrowIfLessThan(sizeHint, 0);

        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        CheckIfDisposed();

        ArgumentOutOfRangeException.ThrowIfLessThan(sizeHint, 0);

        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsSpan(_written);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        Debug.Assert(sizeHint >= 0);

        if (sizeHint == 0)
        {
            sizeHint = MINIMUM_BUFFER_SIZE;
        }

        var availableSpace = _rentedBuffer!.Length - _written;

        if (sizeHint > availableSpace)
        {
            var growBy = sizeHint > _rentedBuffer.Length ? sizeHint : _rentedBuffer.Length;

            var newSize = checked(_rentedBuffer.Length + growBy);

            var oldBuffer = _rentedBuffer;

            _rentedBuffer = ArrayPool<byte>.Shared.Rent(newSize);

            Debug.Assert(oldBuffer.Length >= _written);
            Debug.Assert(_rentedBuffer.Length >= _written);

            oldBuffer.AsSpan(0, _written).CopyTo(_rentedBuffer);
            ArrayPool<byte>.Shared.Return(oldBuffer, clearArray: _cleanRentedBuffers);
        }

        Debug.Assert(_rentedBuffer.Length - _written > 0);
        Debug.Assert(_rentedBuffer.Length - _written >= sizeHint);
    }
}

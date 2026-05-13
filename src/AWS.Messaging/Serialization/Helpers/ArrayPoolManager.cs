// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using AWS.Messaging.Configuration;

namespace AWS.Messaging.Serialization.Helpers;

/// <summary>
/// Manages the lifetime of byte arrays rented from <see cref="ArrayPool{T}.Shared"/>.
/// Tracks all rented buffers and returns them to the pool when disposed.
/// </summary>
internal sealed class ArrayPoolManager : IDisposable
{
    private const int DEFAULT_RENT_CAPACITY = 2;

    private readonly List<byte[]> _rentedBuffers;
    private readonly bool _cleanRentedBuffers;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="ArrayPoolManager"/> with the specified capacity and cleaning behavior.
    /// </summary>
    /// <param name="initialRentCapacity">Initial capacity for tracking rented buffers. Must be positive. Defaults to 2.</param>
    /// <param name="clearRentedBuffers">Whether to clear buffer contents when returning to the pool. Defaults to true for security.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="initialRentCapacity"/> is less than or equal to zero.</exception>
    public ArrayPoolManager(int initialRentCapacity = DEFAULT_RENT_CAPACITY, bool clearRentedBuffers = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialRentCapacity);
        _rentedBuffers = new List<byte[]>(initialRentCapacity);
        _cleanRentedBuffers = clearRentedBuffers;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ArrayPoolManager"/> using options from DI configuration.
    /// </summary>
    /// <param name="options">Configuration options for rented buffer behavior.</param>
    public ArrayPoolManager(RentedBufferOptions options)
        : this(DEFAULT_RENT_CAPACITY, options.CleanRentedBuffers)
    {
    }

    /// <summary>
    /// Rents a byte array from <see cref="ArrayPool{T}.Shared"/> and tracks it for automatic cleanup.
    /// The rented buffer will be returned to the pool when this manager is disposed.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <returns>A byte array that is at least <paramref name="minimumLength"/> in size.</returns>
    public byte[] Rent(int minimumLength)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(minimumLength);
        _rentedBuffers.Add(buffer);
        return buffer;
    }

    /// <summary>
    /// Returns all tracked buffers to <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var buffer in _rentedBuffers)
        {
            ArrayPool<byte>.Shared.Return(buffer, _cleanRentedBuffers);
        }

        _rentedBuffers.Clear();
        _disposed = true;
    }
}

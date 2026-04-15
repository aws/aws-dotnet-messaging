// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration;

/// <summary>
/// Configuration options for rented buffer management used during message serialization.
/// </summary>
public class RentedBufferOptions
{
    /// <summary>
    /// When set to true, it will clean the rented buffers after each use.
    /// </summary>
    /// <remarks>
    /// Setting this to false can improve performance in high-throughput scenarios at cost of potential security issues. Consumers should only set this to false if they are sure that the buffers will not contain sensitive data and that the buffers will be reused in a way that does not cause data leaks. The default value is true, which means that the buffers will be cleaned after each use to prevent potential security issues. Consumers should carefully consider the implications of setting this to false before doing so.
    /// <br/>
    /// Custom implementations of IMessageSerializer relying on rented buffers should also respect this setting and clean the buffers if this is set to true.
    /// </remarks>
    public bool CleanRentedBuffers { get; set; } = true;

    /// <summary>
    /// Determines the initial size of the buffer rented from the ArrayPool when serializing messages.
    /// The default value is 2048 bytes. Must be greater than zero.
    /// </summary>
    /// <remarks>
    /// Setting this value too low may result in more frequent buffer rentals,
    /// while setting it too high may result in increased memory usage and costly buffer cleaning operations.
    /// Custom implementations of <see cref="AWS.Messaging.Serialization.IMessageSerializer"/> relying on
    /// rented buffers should also respect this setting when renting buffers for serialization.
    /// </remarks>
    public int InitialBufferSize
    {
        get => _initialBufferSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            _initialBufferSize = value;
        }
    }

    private int _initialBufferSize = 2048;
}

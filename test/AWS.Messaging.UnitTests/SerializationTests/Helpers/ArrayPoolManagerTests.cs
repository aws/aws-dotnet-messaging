// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization.Helpers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Helpers;

public class ArrayPoolManagerTests
{
    [Fact]
    public void Rent_ReturnsBufferOfAtLeastRequestedSize()
    {
        using var manager = new ArrayPoolManager();

        var buffer = manager.Rent(256);

        Assert.NotNull(buffer);
        Assert.True(buffer.Length >= 256);
    }

    [Fact]
    public void Rent_MultipleBuffers_ReturnsDifferentInstances()
    {
        using var manager = new ArrayPoolManager();

        var buffer1 = manager.Rent(128);
        var buffer2 = manager.Rent(128);
        var buffer3 = manager.Rent(128);

        Assert.NotSame(buffer1, buffer2);
        Assert.NotSame(buffer1, buffer3);
        Assert.NotSame(buffer2, buffer3);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var manager = new ArrayPoolManager();
        manager.Rent(128);

        manager.Dispose();
        manager.Dispose();
    }

    [Fact]
    public void Constructor_WithZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayPoolManager(initialRentCapacity: 0));

        Assert.Equal("initialRentCapacity", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayPoolManager(initialRentCapacity: -5));

        Assert.Equal("initialRentCapacity", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithRentedBufferOptions_CanRent()
    {
        var options = new RentedBufferOptions { CleanRentedBuffers = false };
        using var manager = new ArrayPoolManager(options);

        var buffer = manager.Rent(128);

        Assert.NotNull(buffer);
        Assert.True(buffer.Length >= 128);
    }
}

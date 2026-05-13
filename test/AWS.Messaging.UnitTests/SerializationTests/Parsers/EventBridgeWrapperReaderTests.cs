// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Helpers;
using AWS.Messaging.Serialization.Parsers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Parsers;

public class EventBridgeWrapperReaderTests
{
    private readonly EventBridgeWrapperReader _reader = new();

    [Fact]
    public void Extract_WithObjectDetail_ReturnsInnerBodyAndMetadata()
    {
        var json = """
        {
            "version": "0",
            "id": "eb-evt-1",
            "source": "my.source",
            "detail-type": "MyDetailType",
            "time": "2024-03-15T10:00:00Z",
            "account": "123456789012",
            "region": "us-east-1",
            "resources": ["arn:aws:resource:1"],
            "detail": { "id": "1", "data": "hello" }
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, metadata) = _reader.Extract(json, new Message(), poolManager);

        var innerJson = Encoding.UTF8.GetString(innerBody.Span);
        Assert.Contains("\"id\"", innerJson);
        Assert.Contains("\"data\"", innerJson);

        // No buffer return needed - object/array detail returns a zero-copy slice of the input

        Assert.NotNull(metadata.EventBridgeMetadata);
        Assert.Equal("eb-evt-1", metadata.EventBridgeMetadata.EventId);
        Assert.Equal("my.source", metadata.EventBridgeMetadata.Source);
        Assert.Equal("MyDetailType", metadata.EventBridgeMetadata.DetailType);
        Assert.Equal("123456789012", metadata.EventBridgeMetadata.AWSAccount);
        Assert.Equal("us-east-1", metadata.EventBridgeMetadata.AWSRegion);
        Assert.Equal(DateTimeOffset.Parse("2024-03-15T10:00:00Z"), metadata.EventBridgeMetadata.Time);
        Assert.NotNull(metadata.EventBridgeMetadata.Resources);
        Assert.Single(metadata.EventBridgeMetadata.Resources);
        Assert.Equal("arn:aws:resource:1", metadata.EventBridgeMetadata.Resources[0]);
    }

    [Fact]
    public void Extract_WithStringDetail_ReturnsDecodedInnerBody()
    {
        var json = """
        {
            "detail": "{\"key\":\"value\"}",
            "detail-type": "Test",
            "source": "test",
            "time": "2024-03-15T10:00:00Z"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, metadata) = _reader.Extract(json, new Message(), poolManager);

        var innerJson = Encoding.UTF8.GetString(innerBody.Span);
        Assert.Equal("{\"key\":\"value\"}", innerJson);
    }

    [Fact]
    public void Extract_WithMissingDetail_Throws()
    {
        var json = Encoding.UTF8.GetBytes("""
        {
            "detail-type": "Test",
            "source": "test",
            "time": "2024-03-15T10:00:00Z"
        }
        """);

        using var poolManager = new ArrayPoolManager();
        Assert.Throws<InvalidOperationException>(() => _reader.Extract(json, new Message(), poolManager));
    }

    [Fact]
    public void Extract_WithNullDetail_Throws()
    {
        var json = Encoding.UTF8.GetBytes("""
        {
            "detail": null,
            "detail-type": "Test",
            "source": "test",
            "time": "2024-03-15T10:00:00Z"
        }
        """);

        using var poolManager = new ArrayPoolManager();
        Assert.Throws<InvalidOperationException>(() => _reader.Extract(json, new Message(), poolManager));
    }

    [Fact]
    public void Extract_WithMissingOptionalFields_ReturnsPartialMetadata()
    {
        var json = """
        {
            "detail": { "data": 1 }
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, metadata) = _reader.Extract(json, new Message(), poolManager);

        Assert.False(innerBody.IsEmpty);
        // No buffer return needed - object/array detail returns a zero-copy slice of the input

        Assert.NotNull(metadata.EventBridgeMetadata);
        Assert.Null(metadata.EventBridgeMetadata.EventId);
        Assert.Null(metadata.EventBridgeMetadata.Source);
        Assert.Null(metadata.EventBridgeMetadata.AWSAccount);
        Assert.Null(metadata.EventBridgeMetadata.AWSRegion);
        Assert.Null(metadata.EventBridgeMetadata.Resources);
    }

    [Fact]
    public void Validate_AlwaysReturnsTrue()
    {
        var result = new WrapperClassificationResult(WrapperType.EventBridge, 0, null);

        Assert.True(_reader.Validate(result));
    }

    [Fact]
    public void Extract_SkipsUnknownProperties()
    {
        var json = """
        {
            "detail": { "x": 1 },
            "unknown_field": "ignored",
            "another_unknown": { "deep": true }
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, _) = _reader.Extract(json, new Message(), poolManager);

        Assert.False(innerBody.IsEmpty);
        // No buffer return needed - object/array detail returns a zero-copy slice of the input
    }

    private static void ReturnRentedBuffer(ReadOnlyMemory<byte> memory)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null)
            ArrayPool<byte>.Shared.Return(segment.Array);
    }
}

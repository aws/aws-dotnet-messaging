// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization;
using AWS.Messaging.Serialization.Helpers;
using AWS.Messaging.Serialization.Parsers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Parsers;

public class SNSWrapperReaderTests
{
    private readonly SNSWrapperReader _reader = new();

    // -------------------------------------------------------------------------
    // Extract
    // -------------------------------------------------------------------------

    [Fact]
    public void Extract_WithValidMessage_ReturnsInnerBodyAndMetadata()
    {
        var json = """
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123456789012:MyTopic",
            "Subject": "Test Subject",
            "Timestamp": "2024-03-15T10:00:00.000Z",
            "UnsubscribeURL": "https://sns.us-east-1.amazonaws.com/unsub",
            "Message": "{\"id\":\"1\",\"data\":\"hello\"}"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, metadata) = _reader.Extract(json, new Message(), poolManager);

        var innerJson = Encoding.UTF8.GetString(innerBody.Span);
        Assert.Equal("{\"id\":\"1\",\"data\":\"hello\"}", innerJson);

        ReturnRentedBuffer(innerBody);

        Assert.NotNull(metadata.SNSMetadata);
        Assert.Equal("sns-msg-1", metadata.SNSMetadata.MessageId);
        Assert.Equal("arn:aws:sns:us-east-1:123456789012:MyTopic", metadata.SNSMetadata.TopicArn);
        Assert.Equal("Test Subject", metadata.SNSMetadata.Subject);
        Assert.Equal("https://sns.us-east-1.amazonaws.com/unsub", metadata.SNSMetadata.UnsubscribeURL);
        Assert.Equal(DateTimeOffset.Parse("2024-03-15T10:00:00.000Z"), metadata.SNSMetadata.Timestamp);
    }

    [Fact]
    public void Extract_WithMissingMessage_Throws()
    {
        var json = Encoding.UTF8.GetBytes("""
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123:topic"
        }
        """);

        using var poolManager = new ArrayPoolManager();
        Assert.Throws<InvalidOperationException>(() => _reader.Extract(json, new Message(), poolManager));
    }

    [Fact]
    public void Extract_WithMinimalFields_ReturnsBodyAndPartialMetadata()
    {
        var json = """
        {
            "Message": "plain text body"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, metadata) = _reader.Extract(json, new Message(), poolManager);

        Assert.False(innerBody.IsEmpty);
        Assert.NotNull(metadata.SNSMetadata);
        Assert.Null(metadata.SNSMetadata.MessageId);
        Assert.Null(metadata.SNSMetadata.TopicArn);
    }

    [Fact]
    public void Extract_SkipsUnknownProperties()
    {
        var json = """
        {
            "Type": "Notification",
            "UnknownField": { "nested": true },
            "Message": "body",
            "AnotherUnknown": [1, 2, 3]
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var (innerBody, _) = _reader.Extract(json, new Message(), poolManager);

        Assert.False(innerBody.IsEmpty);
    }

    // -------------------------------------------------------------------------
    // Validate
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WithNotificationType_ReturnsTrue()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, "Notification");
        Assert.True(_reader.Validate(result));
    }

    [Fact]
    public void Validate_WithNonNotificationType_ReturnsFalse()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, "SubscriptionConfirmation");
        Assert.False(_reader.Validate(result));
    }

    [Fact]
    public void Validate_WithNullType_ReturnsFalse()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, null);
        Assert.False(_reader.Validate(result));
    }

    // -------------------------------------------------------------------------
    // IWrapperInlineExtractor.TryCaptureProperty
    // -------------------------------------------------------------------------

    [Fact]
    public void TryCaptureProperty_WithLowercaseProperty_ReturnsFalse()
    {
        var json = """{"source": "test"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.False(result);
        Assert.True(body.IsEmpty);
        Assert.Null(meta);
        Assert.False(fallback);
    }

    [Fact]
    public void TryCaptureProperty_WithUnknownPascalCaseProperty_ReturnsFalse()
    {
        var json = """{"Zebra": "value"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.False(result);
        Assert.True(body.IsEmpty);
    }

    [Fact]
    public void TryCaptureProperty_WithMessageProperty_CapturesBody()
    {
        var json = """{"Message": "inner-body"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.Equal("inner-body", Encoding.UTF8.GetString(body.Span));
        Assert.Null(meta);
    }

    [Fact]
    public void TryCaptureProperty_WithEscapedMessage_UnescapesBody()
    {
        var json = """{"Message": "{\"id\":\"1\"}"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.Equal("{\"id\":\"1\"}", Encoding.UTF8.GetString(body.Span));
    }

    [Fact]
    public void TryCaptureProperty_WithMessageId_SetsMetadataAndBitmapBit()
    {
        var json = """{"MessageId": "msg-123"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;
        var keyMap = new Dictionary<string, ulong> { ["MessageId"] = 0b01UL }.ToFrozenDictionary();

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, keyMap, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.NotNull(meta?.SNSMetadata);
        Assert.Equal("msg-123", meta!.SNSMetadata!.MessageId);
        Assert.Equal(0b01UL, bitmap);
    }

    [Fact]
    public void TryCaptureProperty_WithTopicArn_SetsMetadataAndBitmapBit()
    {
        var json = """{"TopicArn": "arn:aws:sns:us-east-1:123:topic"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;
        var keyMap = new Dictionary<string, ulong> { ["TopicArn"] = 0b10UL }.ToFrozenDictionary();

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, keyMap, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.NotNull(meta?.SNSMetadata);
        Assert.Equal("arn:aws:sns:us-east-1:123:topic", meta!.SNSMetadata!.TopicArn);
        Assert.Equal(0b10UL, bitmap);
    }

    [Fact]
    public void TryCaptureProperty_WithSubject_SetsMetadata()
    {
        var json = """{"Subject": "my-subject"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.Equal("my-subject", meta?.SNSMetadata?.Subject);
    }

    [Fact]
    public void TryCaptureProperty_WithUnsubscribeURL_SetsMetadata()
    {
        var json = """{"UnsubscribeURL": "https://unsubscribe.example.com"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.Equal("https://unsubscribe.example.com", meta?.SNSMetadata?.UnsubscribeURL);
    }

    [Fact]
    public void TryCaptureProperty_WithTimestamp_SetsMetadata()
    {
        var json = """{"Timestamp": "2024-03-15T10:00:00Z"}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.Equal(new DateTimeOffset(2024, 3, 15, 10, 0, 0, TimeSpan.Zero), meta?.SNSMetadata?.Timestamp);
    }

    [Fact]
    public void TryCaptureProperty_WithMessageAttributes_SetsRequiresFallback()
    {
        var json = """{"MessageAttributes": {"attr": {"Type": "String", "Value": "v"}}}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        var result = _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.True(result);
        Assert.True(fallback);
    }

    [Fact]
    public void TryCaptureProperty_WithNullMessageId_SetsNull()
    {
        var json = """{"MessageId": null}"""u8;
        var reader = new Utf8JsonReader(json);
        reader.Read(); reader.Read();

        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;

        _reader.TryCaptureProperty(ref reader, poolManager, ref bitmap, FrozenDictionary<string, ulong>.Empty, ref body, ref meta, ref fallback);

        Assert.NotNull(meta?.SNSMetadata);
        Assert.Null(meta!.SNSMetadata!.MessageId);
    }

    [Fact]
    public void TryCaptureProperty_MultipleCalls_AccumulateIntoSameMetadataInstance()
    {
        using var poolManager = new ArrayPoolManager();
        ulong bitmap = 0;
        ReadOnlyMemory<byte> body = default;
        MessageMetadata? meta = null;
        bool fallback = false;
        var keyMap = new Dictionary<string, ulong> { ["MessageId"] = 1UL, ["TopicArn"] = 2UL }.ToFrozenDictionary();

        var json1 = """{"MessageId": "msg-1"}"""u8;
        var r1 = new Utf8JsonReader(json1);
        r1.Read(); r1.Read();
        _reader.TryCaptureProperty(ref r1, poolManager, ref bitmap, keyMap, ref body, ref meta, ref fallback);

        var firstInstance = meta;

        var json2 = """{"TopicArn": "arn:aws:sns:us-east-1:123:topic"}"""u8;
        var r2 = new Utf8JsonReader(json2);
        r2.Read(); r2.Read();
        _reader.TryCaptureProperty(ref r2, poolManager, ref bitmap, keyMap, ref body, ref meta, ref fallback);

        Assert.Same(firstInstance, meta);
        Assert.Equal("msg-1", meta!.SNSMetadata!.MessageId);
        Assert.Equal("arn:aws:sns:us-east-1:123:topic", meta.SNSMetadata.TopicArn);
    }

    // -------------------------------------------------------------------------
    // IWrapperInlineExtractor.IsCaptureSufficient
    // -------------------------------------------------------------------------

    [Fact]
    public void IsCaptureSufficient_WithBodyAndNoFallback_ReturnsTrue()
    {
        var body = new ReadOnlyMemory<byte>(new byte[] { 1 });
        Assert.True(_reader.IsCaptureSufficient(body, new MessageMetadata(), requiresFallback: false));
    }

    [Fact]
    public void IsCaptureSufficient_WithEmptyBody_ReturnsFalse()
    {
        Assert.False(_reader.IsCaptureSufficient(ReadOnlyMemory<byte>.Empty, null, requiresFallback: false));
    }

    [Fact]
    public void IsCaptureSufficient_WithBodyAndFallbackRequired_ReturnsFalse()
    {
        var body = new ReadOnlyMemory<byte>(new byte[] { 1 });
        Assert.False(_reader.IsCaptureSufficient(body, null, requiresFallback: true));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void ReturnRentedBuffer(ReadOnlyMemory<byte> memory)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null)
            ArrayPool<byte>.Shared.Return(segment.Array);
    }
}

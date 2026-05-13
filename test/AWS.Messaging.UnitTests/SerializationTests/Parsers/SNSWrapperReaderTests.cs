// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization;
using AWS.Messaging.Serialization.Parsers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Parsers;

public class SNSWrapperReaderTests
{
    private readonly SNSWrapperReader _reader = new();

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
        """u8;

        var (innerBody, metadata) = _reader.Extract(json, new Message());

        // Verify inner body was extracted correctly
        var innerJson = Encoding.UTF8.GetString(innerBody.Span);
        Assert.Equal("{\"id\":\"1\",\"data\":\"hello\"}", innerJson);

        // Return rented buffer
        ReturnRentedBuffer(innerBody);

        // Verify SNS metadata
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

        Assert.Throws<InvalidOperationException>(() => _reader.Extract(json, new Message()));
    }

    [Fact]
    public void Extract_WithMinimalFields_ReturnsBodyAndPartialMetadata()
    {
        var json = """
        {
            "Message": "plain text body"
        }
        """u8;

        var (innerBody, metadata) = _reader.Extract(json, new Message());

        Assert.False(innerBody.IsEmpty);
        ReturnRentedBuffer(innerBody);

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
        """u8;

        var (innerBody, metadata) = _reader.Extract(json, new Message());

        Assert.False(innerBody.IsEmpty);
        ReturnRentedBuffer(innerBody);
    }

    [Fact]
    public void Validate_WithNotificationType_ReturnsTrue()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, 0, "Notification");

        Assert.True(_reader.Validate(result));
    }

    [Fact]
    public void Validate_WithNonNotificationType_ReturnsFalse()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, 0, "SubscriptionConfirmation");

        Assert.False(_reader.Validate(result));
    }

    [Fact]
    public void Validate_WithNullType_ReturnsFalse()
    {
        var result = new WrapperClassificationResult(WrapperType.Sns, 0, null);

        Assert.False(_reader.Validate(result));
    }

    private static void ReturnRentedBuffer(ReadOnlyMemory<byte> memory)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null)
            ArrayPool<byte>.Shared.Return(segment.Array);
    }
}

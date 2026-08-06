// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using System.Text.Json;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Serialization.Helpers;
using AWS.Messaging.Serialization.Parsers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Parsers;

public class MessageTypeClassifierTests
{
    private readonly IMessageTypeClassifier _classifier;

    public MessageTypeClassifierTests()
    {
        IWrapperReader[] readers = [new SNSWrapperReader(), new EventBridgeWrapperReader()];
        _classifier = new MessageTypeClassifier(readers);
    }

    [Fact]
    public void Classify_WithSNSMessage_ReturnsSnsType()
    {
        var json = """
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123:topic",
            "Message": "hello"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sns, result.WrapperType);
        Assert.Equal("Notification", result.TypeValue);
    }

    [Fact]
    public void Classify_WithSNSMessage_CapturesMetadataAndInnerBodyInSinglePass()
    {
        var json = """
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123:topic",
            "Subject": "my-subject",
            "UnsubscribeURL": "https://unsubscribe.example.com",
            "Timestamp": "2024-03-15T10:00:00Z",
            "Message": "inner-body"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sns, result.WrapperType);
        Assert.NotNull(result.CapturedMetadata);
        Assert.False(result.CapturedInnerBody.IsEmpty);

        var snsMetadata = result.CapturedMetadata!.SNSMetadata;
        Assert.NotNull(snsMetadata);
        Assert.Equal("sns-msg-1", snsMetadata!.MessageId);
        Assert.Equal("arn:aws:sns:us-east-1:123:topic", snsMetadata.TopicArn);
        Assert.Equal("my-subject", snsMetadata.Subject);
        Assert.Equal("https://unsubscribe.example.com", snsMetadata.UnsubscribeURL);
        Assert.Equal(new DateTimeOffset(2024, 3, 15, 10, 0, 0, TimeSpan.Zero), snsMetadata.Timestamp);

        var innerBody = Encoding.UTF8.GetString(result.CapturedInnerBody.Span);
        Assert.Equal("inner-body", innerBody);
    }

    [Fact]
    public void Classify_WithSNSMessage_WithMessageAttributes_FallsBackToSecondPass()
    {
        // MessageAttributes present — classifier should NOT populate CapturedMetadata
        // so the caller falls through to the full SNSWrapperReader pass
        var json = """
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123:topic",
            "Message": "inner-body",
            "MessageAttributes": {
                "attr1": { "Type": "String", "Value": "val1" }
            }
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sns, result.WrapperType);
        Assert.Null(result.CapturedMetadata);
        Assert.True(result.CapturedInnerBody.IsEmpty);
    }

    [Fact]
    public void Classify_WithNonNotificationType_FallsBackToSqs()
    {
        var json = """
        {
            "Type": "SubscriptionConfirmation",
            "MessageId": "sns-msg-1",
            "TopicArn": "arn:aws:sns:us-east-1:123:topic",
            "Message": "hello"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sqs, result.WrapperType);
    }

    [Fact]
    public void Classify_WithEventBridgeMessage_ReturnsEventBridgeType()
    {
        var json = """
        {
            "detail": { "key": "value" },
            "detail-type": "MyDetailType",
            "source": "my.source",
            "time": "2024-03-15T10:00:00Z"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.EventBridge, result.WrapperType);
    }

    [Fact]
    public void Classify_WithPlainEnvelope_ReturnsSquType()
    {
        var json = """
        {
            "id": "123",
            "source": "/test",
            "specversion": "1.0",
            "type": "MyApp.MyMessage",
            "time": "2024-03-15T10:00:00Z",
            "data": {}
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sqs, result.WrapperType);
    }

    [Fact]
    public void Classify_WithPartialSNSKeys_FallsBackToSqs()
    {
        // Missing TopicArn — not a full SNS match
        var json = """
        {
            "Type": "Notification",
            "MessageId": "sns-msg-1",
            "Message": "hello"
        }
        """u8.ToArray();

        using var poolManager = new ArrayPoolManager();
        var result = _classifier.Classify(json, poolManager);

        Assert.Equal(WrapperType.Sqs, result.WrapperType);
    }

    [Fact]
    public void GetReader_WithRegisteredType_ReturnsReader()
    {
        var reader = _classifier.GetReader(WrapperType.Sns);

        Assert.IsType<SNSWrapperReader>(reader);
    }

    [Fact]
    public void GetReader_WithUnregisteredType_Throws()
    {
        // SQS is not registered as a wrapper reader
        Assert.Throws<InvalidOperationException>(() => _classifier.GetReader(WrapperType.Sqs));
    }
}

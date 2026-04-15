// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using System.Text.Json;
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
        """u8;

        var result = _classifier.Classify(json);

        Assert.Equal(WrapperType.Sns, result.WrapperType);
        Assert.Equal("Notification", result.TypeValue);
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
        """u8;

        var result = _classifier.Classify(json);

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
        """u8;

        var result = _classifier.Classify(json);

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
        """u8;

        var result = _classifier.Classify(json);

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
        """u8;

        var result = _classifier.Classify(json);

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

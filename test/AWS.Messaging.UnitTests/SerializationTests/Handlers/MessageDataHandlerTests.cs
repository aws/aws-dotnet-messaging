// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Handlers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Handlers;

public class MessageMetadataHandlerTests
{
    [Fact]
    public void CreateSQSMetadata_WithBasicMessage_ReturnsCorrectMetadata()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            ReceiptHandle = "test-receipt-handle",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                { "TestAttribute", new MessageAttributeValue { StringValue = "TestValue" } }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Equal("test-message-id", metadata.MessageID);
        Assert.Equal("test-receipt-handle", metadata.ReceiptHandle);
        Assert.NotNull(metadata.MessageAttributes);
        Assert.Single(metadata.MessageAttributes);
        Assert.Equal("TestValue", metadata.MessageAttributes["TestAttribute"].StringValue);
    }

    [Fact]
    public void CreateSQSMetadata_WithFIFOAttributes_ReturnsCorrectMetadata()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = new Dictionary<string, string>
            {
                { "MessageGroupId", "group-1" },
                { "MessageDeduplicationId", "dedup-1" }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Equal("group-1", metadata.MessageGroupId);
        Assert.Equal("dedup-1", metadata.MessageDeduplicationId);
    }

    [Fact]
    public void CreateSQSMetadata_WithValidSentTimestamp_ReturnsCorrectMetadata()
    {
        // Arrange
        // 1609459200000 = 2021-01-01T00:00:00Z in epoch milliseconds
        var expectedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(1609459200000);
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = new Dictionary<string, string>
            {
                { "SentTimestamp", "1609459200000" }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.NotNull(metadata.SentTimestamp);
        Assert.Equal(expectedTimestamp, metadata.SentTimestamp);
    }

    [Fact]
    public void CreateSQSMetadata_WithMissingSentTimestamp_ReturnsNullSentTimestamp()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = new Dictionary<string, string>
            {
                { "MessageGroupId", "group-1" }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Null(metadata.SentTimestamp);
    }

    [Fact]
    public void CreateSQSMetadata_WithEmptySentTimestamp_ReturnsNullSentTimestamp()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = new Dictionary<string, string>
            {
                { "SentTimestamp", "" }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Null(metadata.SentTimestamp);
    }

    [Fact]
    public void CreateSQSMetadata_WithInvalidSentTimestamp_ReturnsNullSentTimestamp()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = new Dictionary<string, string>
            {
                { "SentTimestamp", "not-a-number" }
            }
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Null(metadata.SentTimestamp);
    }

    [Fact]
    public void CreateSQSMetadata_WithNullAttributes_ReturnsNullSentTimestamp()
    {
        // Arrange
        var message = new Message
        {
            MessageId = "test-message-id",
            Attributes = null
        };

        // Act
        var metadata = MessageMetadataHandler.CreateSQSMetadata(message);

        // Assert
        Assert.Null(metadata.SentTimestamp);
    }

    [Fact]
    public void CreateSNSMetadata_WithValidJson_ReturnsCorrectMetadata()
    {
        // Arrange
        var json = @"{
            ""MessageId"": ""test-message-id"",
            ""TopicArn"": ""arn:aws:sns:region:account:topic"",
            ""Timestamp"": ""2024-03-15T10:00:00.000Z"",
            ""UnsubscribeURL"": ""https://sns.region.amazonaws.com/unsubscribe"",
            ""Subject"": ""Test Subject"",
            ""MessageAttributes"": {
                ""TestAttribute"": {
                    ""Type"": ""String"",
                    ""Value"": ""TestValue""
                }
            }
        }";
        var root = JsonDocument.Parse(json).RootElement;

        // Act
        var metadata = MessageMetadataHandler.CreateSNSMetadata(root);

        // Assert
        Assert.Equal("test-message-id", metadata.MessageId);
        Assert.Equal("arn:aws:sns:region:account:topic", metadata.TopicArn);
        Assert.Equal(DateTimeOffset.Parse("2024-03-15T10:00:00.000Z"), metadata.Timestamp);
        Assert.Equal("https://sns.region.amazonaws.com/unsubscribe", metadata.UnsubscribeURL);
        Assert.Equal("Test Subject", metadata.Subject);
        Assert.NotNull(metadata.MessageAttributes);
        Assert.Single(metadata.MessageAttributes);
    }

    [Fact]
    public void CreateSNSMetadata_WithMissingOptionalFields_ReturnsPartialMetadata()
    {
        // Arrange
        var json = @"{
            ""MessageId"": ""test-message-id"",
            ""TopicArn"": ""arn:aws:sns:region:account:topic""
        }";
        var root = JsonDocument.Parse(json).RootElement;

        // Act
        var metadata = MessageMetadataHandler.CreateSNSMetadata(root);

        // Assert
        Assert.Equal("test-message-id", metadata.MessageId);
        Assert.Equal("arn:aws:sns:region:account:topic", metadata.TopicArn);
        Assert.Equal(default, metadata.Timestamp);
        Assert.Null(metadata.UnsubscribeURL);
        Assert.Null(metadata.Subject);
        Assert.Null(metadata.MessageAttributes);
    }

    [Fact]
    public void CreateEventBridgeMetadata_WithValidJson_ReturnsCorrectMetadata()
    {
        // Arrange
        var json = @"{
            ""id"": ""test-event-id"",
            ""detail-type"": ""test-detail-type"",
            ""source"": ""test-source"",
            ""account"": ""123456789012"",
            ""time"": ""2024-03-15T10:00:00Z"",
            ""region"": ""us-east-1"",
            ""resources"": [
                ""resource1"",
                ""resource2""
            ]
        }";
        var root = JsonDocument.Parse(json).RootElement;

        // Act
        var metadata = MessageMetadataHandler.CreateEventBridgeMetadata(root);

        // Assert
        Assert.Equal("test-event-id", metadata.EventId);
        Assert.Equal("test-detail-type", metadata.DetailType);
        Assert.Equal("test-source", metadata.Source);
        Assert.Equal("123456789012", metadata.AWSAccount);
        Assert.Equal(DateTimeOffset.Parse("2024-03-15T10:00:00Z"), metadata.Time);
        Assert.Equal("us-east-1", metadata.AWSRegion);
        Assert.NotNull(metadata.Resources);
        Assert.Equal(2, metadata.Resources.Count);
        Assert.Contains("resource1", metadata.Resources);
        Assert.Contains("resource2", metadata.Resources);
    }

    [Fact]
    public void CreateEventBridgeMetadata_WithMissingOptionalFields_ReturnsPartialMetadata()
    {
        // Arrange
        var json = @"{
            ""id"": ""test-event-id"",
            ""source"": ""test-source""
        }";
        var root = JsonDocument.Parse(json).RootElement;

        // Act
        var metadata = MessageMetadataHandler.CreateEventBridgeMetadata(root);

        // Assert
        Assert.Equal("test-event-id", metadata.EventId);
        Assert.Equal("test-source", metadata.Source);
        Assert.Null(metadata.DetailType);
        Assert.Null(metadata.AWSAccount);
        Assert.Equal(default, metadata.Time);
        Assert.Null(metadata.AWSRegion);
        Assert.Null(metadata.Resources);
    }

    [Fact]
    public void CreateEventBridgeMetadata_WithEmptyResources_ReturnsEmptyResourcesList()
    {
        // Arrange
        var json = @"{
            ""id"": ""test-event-id"",
            ""resources"": []
        }";
        var root = JsonDocument.Parse(json).RootElement;

        // Act
        var metadata = MessageMetadataHandler.CreateEventBridgeMetadata(root);

        // Assert
        Assert.NotNull(metadata.Resources);
        Assert.Empty(metadata.Resources);
    }
}

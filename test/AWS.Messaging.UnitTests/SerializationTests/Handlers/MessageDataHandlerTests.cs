// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
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
}

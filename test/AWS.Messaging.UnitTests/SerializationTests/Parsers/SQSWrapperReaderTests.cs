// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Text;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Parsers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Parsers;

public class SQSWrapperReaderTests
{
    private readonly SQSWrapperReader _reader = new();

    [Fact]
    public void Extract_ReturnsBodyUnchanged()
    {
        var body = Encoding.UTF8.GetBytes("{\"id\":\"1\",\"type\":\"Test\"}");
        var memory = new System.ReadOnlyMemory<byte>(body);
        var message = new Message
        {
            MessageId = "msg-1",
            ReceiptHandle = "rh-1"
        };

        var (innerBody, metadata) = _reader.Extract(memory, message);

        // Body should be passed through unchanged
        Assert.Equal(body.Length, innerBody.Length);
        Assert.Equal("{\"id\":\"1\",\"type\":\"Test\"}", Encoding.UTF8.GetString(innerBody.Span));
    }

    [Fact]
    public void Extract_CreatesSQSMetadata()
    {
        var body = new System.ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}"));
        var message = new Message
        {
            MessageId = "msg-42",
            ReceiptHandle = "rh-42",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["Key1"] = new() { StringValue = "Value1" }
            }
        };

        var (_, metadata) = _reader.Extract(body, message);

        Assert.NotNull(metadata.SQSMetadata);
        Assert.Equal("msg-42", metadata.SQSMetadata.MessageID);
        Assert.Equal("rh-42", metadata.SQSMetadata.ReceiptHandle);
        Assert.NotNull(metadata.SQSMetadata.MessageAttributes);
        Assert.Single(metadata.SQSMetadata.MessageAttributes);
    }

    [Fact]
    public void Extract_WithFifoAttributes_PopulatesGroupAndDedup()
    {
        var body = new System.ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}"));
        var message = new Message
        {
            MessageId = "msg-fifo",
            ReceiptHandle = "rh-fifo",
            Attributes = new Dictionary<string, string>
            {
                ["MessageGroupId"] = "group-1",
                ["MessageDeduplicationId"] = "dedup-1"
            }
        };

        var (_, metadata) = _reader.Extract(body, message);

        Assert.Equal("group-1", metadata.SQSMetadata!.MessageGroupId);
        Assert.Equal("dedup-1", metadata.SQSMetadata.MessageDeduplicationId);
    }
}

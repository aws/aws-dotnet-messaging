// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Handlers;

/// <summary>
/// Handles the creation of metadata objects from various AWS messaging services.
/// </summary>
internal static class MessageMetadataHandler
{
    /// <summary>
    /// Creates SQS metadata from an SQS message.
    /// </summary>
    /// <param name="message">The SQS message containing metadata information.</param>
    /// <returns>An SQSMetadata object containing the extracted metadata.</returns>
    public static SQSMetadata CreateSQSMetadata(Message message)
    {
        var metadata = new SQSMetadata
        {
            MessageID = message.MessageId,
            ReceiptHandle = message.ReceiptHandle,
            MessageAttributes = message.MessageAttributes,
        };

        if (message.Attributes != null)
        {
            metadata.MessageGroupId = JsonPropertyHelper.GetAttributeValue(message.Attributes, "MessageGroupId");
            metadata.MessageDeduplicationId = JsonPropertyHelper.GetAttributeValue(message.Attributes, "MessageDeduplicationId");

            var sentTimestamp = JsonPropertyHelper.GetAttributeValue(message.Attributes, "SentTimestamp");
            if (!string.IsNullOrEmpty(sentTimestamp) && long.TryParse(sentTimestamp, out var epochMilliseconds))
            {
                metadata.SentTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds);
            }

            var approximateReceiveCount = JsonPropertyHelper.GetAttributeValue(message.Attributes, "ApproximateReceiveCount");
            if (!string.IsNullOrEmpty(approximateReceiveCount) && int.TryParse(approximateReceiveCount, out var receiveCount))
            {
                metadata.ApproximateReceiveCount = receiveCount;
            }
        }

        return metadata;
    }
}

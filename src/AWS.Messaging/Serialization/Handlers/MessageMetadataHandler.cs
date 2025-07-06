// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Internal;
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
        }

        return metadata;
    }

    /// <summary>
    /// Creates SNS metadata from a JSON element representing an SNS message.
    /// </summary>
    /// <param name="root">The root JSON element containing SNS metadata information.</param>
    /// <returns>An SNSMetadata object containing the extracted metadata.</returns>
    public static SNSMetadata CreateSNSMetadata(JsonElement root)
    {
        var metadata = new SNSMetadata
        {
            MessageId = JsonPropertyHelper.GetStringProperty(root, "MessageId"),
            TopicArn = JsonPropertyHelper.GetStringProperty(root, "TopicArn"),
            Timestamp = JsonPropertyHelper.GetDateTimeOffsetProperty(root, "Timestamp") ?? default,
            UnsubscribeURL = JsonPropertyHelper.GetStringProperty(root, "UnsubscribeURL"),
            Subject = JsonPropertyHelper.GetStringProperty(root, "Subject"),
        };

        if (root.TryGetProperty("MessageAttributes", out var messageAttributes))
        {
            metadata.MessageAttributes = messageAttributes.Deserialize(MessagingJsonSerializerContext.Default.DictionarySNSMessageAttributeValue);

        }

        return metadata;
    }

    /// <summary>
    /// Creates EventBridge metadata from a JSON element representing an EventBridge event.
    /// </summary>
    /// <param name="root">The root JSON element containing EventBridge metadata information.</param>
    /// <returns>An EventBridgeMetadata object containing the extracted metadata.</returns>
    public static EventBridgeMetadata CreateEventBridgeMetadata(JsonElement root)
    {
        var metadata = new EventBridgeMetadata
        {
            EventId = JsonPropertyHelper.GetStringProperty(root, "id"),
            DetailType = JsonPropertyHelper.GetStringProperty(root, "detail-type"),
            Source = JsonPropertyHelper.GetStringProperty(root, "source"),
            AWSAccount = JsonPropertyHelper.GetStringProperty(root, "account"),
            Time = JsonPropertyHelper.GetDateTimeOffsetProperty(root, "time") ?? default,
            AWSRegion = JsonPropertyHelper.GetStringProperty(root, "region"),
        };

        if (root.TryGetProperty("resources", out var resources))
        {
            metadata.Resources = resources.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .ToList()!;
        }

        return metadata;
    }
}

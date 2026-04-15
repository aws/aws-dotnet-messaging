// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Internal;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Reader for messages originating from Amazon Simple Notification Service (SNS).
/// Detects SNS wrappers via "Type", "MessageId", "TopicArn" discriminators and
/// extracts the inner message body plus SNS metadata using a single Utf8JsonReader pass.
/// </summary>
internal sealed class SNSWrapperReader : IWrapperReader
{
    private static readonly byte[] s_type = Encoding.UTF8.GetBytes("Type");
    private static readonly byte[] s_messageId = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] s_topicArn = Encoding.UTF8.GetBytes("TopicArn");
    private static readonly byte[] s_message = Encoding.UTF8.GetBytes("Message");
    private static readonly byte[] s_timestamp = Encoding.UTF8.GetBytes("Timestamp");
    private static readonly byte[] s_unsubscribeUrl = Encoding.UTF8.GetBytes("UnsubscribeURL");
    private static readonly byte[] s_subject = Encoding.UTF8.GetBytes("Subject");
    private static readonly byte[] s_messageAttributes = Encoding.UTF8.GetBytes("MessageAttributes");

    /// <inheritdoc/>
    public WrapperType WrapperType => WrapperType.Sns;

    /// <inheritdoc/>
    public byte[][] GetDiscriminatorKeys() => new[] { s_type, s_messageId, s_topicArn };

    /// <inheritdoc/>
    public bool Validate(in WrapperClassificationResult result)
    {
        return string.Equals(result.TypeValue, "Notification", StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public (string InnerBody, MessageMetadata Metadata) Extract(
        ReadOnlySpan<byte> utf8Body, Message originalMessage)
    {
        var reader = new Utf8JsonReader(utf8Body);
        var snsMetadata = new SNSMetadata();
        string? innerMessage = null;

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.CurrentDepth > 1) reader.Skip();
                continue;
            }

            if (reader.ValueTextEquals(s_message))
            {
                reader.Read();
                innerMessage = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_messageId))
            {
                reader.Read();
                snsMetadata.MessageId = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_topicArn))
            {
                reader.Read();
                snsMetadata.TopicArn = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_timestamp))
            {
                reader.Read();
                snsMetadata.Timestamp = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals(s_unsubscribeUrl))
            {
                reader.Read();
                snsMetadata.UnsubscribeURL = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_subject))
            {
                reader.Read();
                snsMetadata.Subject = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_messageAttributes))
            {
                reader.Read();
                snsMetadata.MessageAttributes = JsonSerializer.Deserialize(
                    ref reader,
                    MessagingJsonSerializerContext.Default.DictionarySNSMessageAttributeValue);
            }
            else
            {
                // Skip unknown property value
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartObject ||
                    reader.TokenType == JsonTokenType.StartArray)
                    reader.Skip();
            }
        }

        if (string.IsNullOrEmpty(innerMessage))
            throw new InvalidOperationException("SNS message does not contain a valid Message property");

        var metadata = new MessageMetadata { SNSMetadata = snsMetadata };
        return (innerMessage, metadata);
    }
}

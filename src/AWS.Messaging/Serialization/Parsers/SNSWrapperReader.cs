// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
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
    private static readonly byte[] s_type = "Type"u8.ToArray();
    private static readonly byte[] s_messageId = "MessageId"u8.ToArray();
    private static readonly byte[] s_topicArn = "TopicArn"u8.ToArray();
    private static readonly byte[] s_message = "Message"u8.ToArray();
    private static readonly byte[] s_timestamp = "Timestamp"u8.ToArray();
    private static readonly byte[] s_unsubscribeUrl = "UnsubscribeURL"u8.ToArray();
    private static readonly byte[] s_subject = "Subject"u8.ToArray();
    private static readonly byte[] s_messageAttributes = "MessageAttributes"u8.ToArray();

    /// <inheritdoc/>
    public WrapperType WrapperType => WrapperType.Sns;

    /// <inheritdoc/>
    public byte[][] GetDiscriminatorKeys() => [s_type, s_messageId, s_topicArn];

    /// <inheritdoc/>
    public bool Validate(in WrapperClassificationResult result)
    {
        // Use interned constant for comparison
        return string.Equals(result.TypeValue, CloudEventConstants.SnsNotification, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public (ReadOnlyMemory<byte> InnerBodyUtf8, MessageMetadata Metadata) Extract(
        ReadOnlySpan<byte> utf8Body, Message originalMessage)
    {
        var reader = new Utf8JsonReader(utf8Body);
        var snsMetadata = new SNSMetadata();
        ReadOnlyMemory<byte> innerBodyUtf8 = default;

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
                // CopyString decodes JSON-escaped UTF-8 directly into a byte buffer,
                // avoiding the intermediate string allocation from GetString().
                int maxBytes = reader.ValueSpan.Length; // un-escaped is always <= escaped length
                byte[] buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
                int written = reader.CopyString(buffer);
                innerBodyUtf8 = buffer.AsMemory(0, written);
            }
            else if (reader.ValueTextEquals(s_messageId))
            {
                reader.Read();
                // Only allocate string if value is not null
                snsMetadata.MessageId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_topicArn))
            {
                reader.Read();
                snsMetadata.TopicArn = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_timestamp))
            {
                reader.Read();
                snsMetadata.Timestamp = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals(s_unsubscribeUrl))
            {
                reader.Read();
                snsMetadata.UnsubscribeURL = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_subject))
            {
                reader.Read();
                snsMetadata.Subject = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
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

        if (innerBodyUtf8.IsEmpty)
            throw new InvalidOperationException("SNS message does not contain a valid Message property");

        var metadata = new MessageMetadata { SNSMetadata = snsMetadata };
        return (innerBodyUtf8, metadata);
    }
}

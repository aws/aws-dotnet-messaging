// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Internal;
using AWS.Messaging.Serialization.Helpers;

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

    private enum PropertyType
    {
        Unknown,
        Message,
        MessageId,
        TopicArn,
        Timestamp,
        UnsubscribeUrl,
        Subject,
        MessageAttributes
    }

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
        ReadOnlyMemory<byte> utf8Body, Message originalMessage, ArrayPoolManager poolManager)
    {
        var reader = new Utf8JsonReader(utf8Body.Span);
        var snsMetadata = new SNSMetadata();
        ReadOnlyMemory<byte> innerBodyUtf8 = default;

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.CurrentDepth > 1) reader.Skip();
                continue;
            }

            switch (IdentifyProperty(ref reader))
            {
                case PropertyType.Message:
                    innerBodyUtf8 = ReadMessage(ref reader, poolManager);
                    break;

                case PropertyType.MessageId:
                    snsMetadata.MessageId = WrapperReaderHelpers.ReadNullableString(ref reader);
                    break;

                case PropertyType.TopicArn:
                    snsMetadata.TopicArn = WrapperReaderHelpers.ReadNullableString(ref reader);
                    break;

                case PropertyType.Timestamp:
                    ReadTimestamp(ref reader, snsMetadata);
                    break;

                case PropertyType.UnsubscribeUrl:
                    snsMetadata.UnsubscribeURL = WrapperReaderHelpers.ReadNullableString(ref reader);
                    break;

                case PropertyType.Subject:
                    snsMetadata.Subject = WrapperReaderHelpers.ReadNullableString(ref reader);
                    break;

                case PropertyType.MessageAttributes:
                    ReadMessageAttributes(ref reader, snsMetadata);
                    break;

                case PropertyType.Unknown:
                default:
                    WrapperReaderHelpers.SkipUnknownProperty(ref reader);
                    break;
            }
        }

        if (innerBodyUtf8.IsEmpty)
            throw new InvalidOperationException("SNS message does not contain a valid Message property");

        var metadata = new MessageMetadata { SNSMetadata = snsMetadata };
        return (innerBodyUtf8, metadata);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PropertyType IdentifyProperty(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals(s_message)) return PropertyType.Message;
        if (reader.ValueTextEquals(s_messageId)) return PropertyType.MessageId;
        if (reader.ValueTextEquals(s_topicArn)) return PropertyType.TopicArn;
        if (reader.ValueTextEquals(s_timestamp)) return PropertyType.Timestamp;
        if (reader.ValueTextEquals(s_unsubscribeUrl)) return PropertyType.UnsubscribeUrl;
        if (reader.ValueTextEquals(s_subject)) return PropertyType.Subject;
        if (reader.ValueTextEquals(s_messageAttributes)) return PropertyType.MessageAttributes;

        return PropertyType.Unknown;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<byte> ReadMessage(ref Utf8JsonReader reader, ArrayPoolManager poolManager)
    {
        reader.Read();
        // CopyString decodes JSON-escaped UTF-8 directly into a byte buffer,
        // avoiding the intermediate string allocation from GetString().
        int maxBytes = reader.ValueSpan.Length; // un-escaped is always <= escaped length
        byte[] buffer = poolManager.Rent(maxBytes);
        int written = reader.CopyString(buffer);
        return buffer.AsMemory(0, written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadTimestamp(ref Utf8JsonReader reader, SNSMetadata metadata)
    {
        reader.Read();
        metadata.Timestamp = reader.GetDateTimeOffset();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadMessageAttributes(ref Utf8JsonReader reader, SNSMetadata metadata)
    {
        reader.Read();
        metadata.MessageAttributes = JsonSerializer.Deserialize(
            ref reader,
            MessagingJsonSerializerContext.Default.DictionarySNSMessageAttributeValue);
    }
}

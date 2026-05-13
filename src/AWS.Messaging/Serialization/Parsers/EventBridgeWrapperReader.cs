// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Text.Json;
using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Reader for messages originating from Amazon EventBridge.
/// Detects EventBridge wrappers via "detail", "detail-type", "source", "time" discriminators and
/// extracts the inner message body plus EventBridge metadata using a single Utf8JsonReader pass.
/// </summary>
internal sealed class EventBridgeWrapperReader : IWrapperReader
{
    private static readonly byte[] s_detail = "detail"u8.ToArray();
    private static readonly byte[] s_detailType = "detail-type"u8.ToArray();
    private static readonly byte[] s_source = "source"u8.ToArray();
    private static readonly byte[] s_time = "time"u8.ToArray();
    private static readonly byte[] s_id = "id"u8.ToArray();
    private static readonly byte[] s_account = "account"u8.ToArray();
    private static readonly byte[] s_region = "region"u8.ToArray();
    private static readonly byte[] s_resources = "resources"u8.ToArray();

    /// <inheritdoc/>
    public WrapperType WrapperType => WrapperType.EventBridge;

    /// <inheritdoc/>
    public byte[][] GetDiscriminatorKeys() => [s_detail, s_detailType, s_source, s_time];

    /// <inheritdoc/>
    public bool Validate(in WrapperClassificationResult result)
    {
        // All four discriminator keys matched — no additional value-level check needed.
        return true;
    }

    /// <inheritdoc/>
    public (ReadOnlyMemory<byte> InnerBodyUtf8, MessageMetadata Metadata) Extract(
        ReadOnlyMemory<byte> utf8Body, Message originalMessage, ArrayPoolManager poolManager)
    {
        var reader = new Utf8JsonReader(utf8Body.Span);
        var ebMetadata = new EventBridgeMetadata();
        ReadOnlyMemory<byte> innerBodyUtf8 = default;

        string? id = null, account = null, region = null;
        List<string>? resources = null;

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.CurrentDepth > 1)
                    reader.Skip();
                continue;
            }

            if (reader.ValueTextEquals(s_detail))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                {
                    // detail is a JSON string — decode escaped UTF-8 into a rented buffer
                    var maxBytes = reader.ValueSpan.Length;
                    var buffer = poolManager.Rent(maxBytes);
                    var written = reader.CopyString(buffer);
                    innerBodyUtf8 = buffer.AsMemory(0, written);
                }
                else if (reader.TokenType == JsonTokenType.Null)
                {
                    // detail is null — will throw below
                }
                else
                {
                    // detail is an object/array — return a slice of the input buffer (zero-copy)
                    var start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    var length = (int)reader.BytesConsumed - start;
                    innerBodyUtf8 = utf8Body.Slice(start, length);
                }
            }
            else if (reader.ValueTextEquals(s_detailType))
            {
                reader.Read();
                ebMetadata.DetailType = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_source))
            {
                reader.Read();
                ebMetadata.Source = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_time))
            {
                reader.Read();
                ebMetadata.Time = reader.GetDateTimeOffset();
            }
            else if (reader.ValueTextEquals(s_id))
            {
                reader.Read();
                // Only allocate string if value is not null
                id = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_account))
            {
                reader.Read();
                account = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_region))
            {
                reader.Read();
                region = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            }
            else if (reader.ValueTextEquals(s_resources))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    resources = new List<string>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var val = reader.GetString();
                            if (val != null)
                                resources.Add(val);
                        }
                    }
                }
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
            throw new InvalidOperationException("EventBridge message does not contain a valid detail property");

        ebMetadata.EventId = id;
        ebMetadata.AWSAccount = account;
        ebMetadata.AWSRegion = region;
        ebMetadata.Resources = resources;

        var metadata = new MessageMetadata { EventBridgeMetadata = ebMetadata };
        return (innerBodyUtf8, metadata);
    }
}

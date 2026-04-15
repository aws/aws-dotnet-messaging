// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Reader for messages originating from Amazon EventBridge.
/// Detects EventBridge wrappers via "detail", "detail-type", "source", "time" discriminators and
/// extracts the inner message body plus EventBridge metadata using a single Utf8JsonReader pass.
/// </summary>
internal sealed class EventBridgeWrapperReader : IWrapperReader
{
    private static readonly byte[] s_detail = Encoding.UTF8.GetBytes("detail");
    private static readonly byte[] s_detailType = Encoding.UTF8.GetBytes("detail-type");
    private static readonly byte[] s_source = Encoding.UTF8.GetBytes("source");
    private static readonly byte[] s_time = Encoding.UTF8.GetBytes("time");
    private static readonly byte[] s_id = Encoding.UTF8.GetBytes("id");
    private static readonly byte[] s_account = Encoding.UTF8.GetBytes("account");
    private static readonly byte[] s_region = Encoding.UTF8.GetBytes("region");
    private static readonly byte[] s_resources = Encoding.UTF8.GetBytes("resources");

    /// <inheritdoc/>
    public WrapperType WrapperType => WrapperType.EventBridge;

    /// <inheritdoc/>
    public byte[][] GetDiscriminatorKeys() => new[] { s_detail, s_detailType, s_source, s_time };

    /// <inheritdoc/>
    public bool Validate(in WrapperClassificationResult result)
    {
        // All four discriminator keys matched — no additional value-level check needed.
        return true;
    }

    /// <inheritdoc/>
    public (string InnerBody, MessageMetadata Metadata) Extract(
        ReadOnlySpan<byte> utf8Body, Message originalMessage)
    {
        var reader = new Utf8JsonReader(utf8Body);
        var ebMetadata = new EventBridgeMetadata();
        string? innerMessage = null;

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
                    innerMessage = reader.GetString();
                }
                else if (reader.TokenType == JsonTokenType.Null)
                {
                    // detail is null — will throw below
                }
                else
                {
                    // detail is an object/array — capture raw text
                    int start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    int length = (int)reader.BytesConsumed - start;
                    innerMessage = Encoding.UTF8.GetString(utf8Body.Slice(start, length));
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
                id = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_account))
            {
                reader.Read();
                account = reader.GetString();
            }
            else if (reader.ValueTextEquals(s_region))
            {
                reader.Read();
                region = reader.GetString();
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

        if (string.IsNullOrEmpty(innerMessage))
            throw new InvalidOperationException("EventBridge message does not contain a valid detail property");

        ebMetadata.EventId = id;
        ebMetadata.AWSAccount = account;
        ebMetadata.AWSRegion = region;
        ebMetadata.Resources = resources;

        var metadata = new MessageMetadata { EventBridgeMetadata = ebMetadata };
        return (innerMessage, metadata);
    }
}

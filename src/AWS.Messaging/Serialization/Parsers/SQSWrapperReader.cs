// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Handlers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Fallback reader for plain SQS messages where the body IS the CloudEvents envelope.
/// This is NOT an <see cref="IWrapperReader"/> because it handles the "no wrapper" case.
/// Registered as a singleton directly in DI.
/// </summary>
internal sealed class SQSWrapperReader : ISQSWrapperReader
{
    /// <summary>
    /// For SQS fallback: the body IS the envelope. No JSON parsing needed.
    /// Returns the pre-encoded UTF-8 body bytes directly and creates SQS metadata from the original message.
    /// </summary>
    /// <param name="utf8Body">The UTF-8 encoded body bytes (already converted by the caller).</param>
    /// <param name="originalMessage">The original SQS message (for SQS-level metadata).</param>
    /// <returns>The envelope UTF-8 bytes and SQS metadata.</returns>
    public (ReadOnlyMemory<byte> InnerBodyUtf8, MessageMetadata Metadata) Extract(ReadOnlyMemory<byte> utf8Body, Message originalMessage)
    {
        var metadata = new MessageMetadata
        {
            SQSMetadata = MessageMetadataHandler.CreateSQSMetadata(originalMessage)
        };

        return (utf8Body, metadata);
    }
}

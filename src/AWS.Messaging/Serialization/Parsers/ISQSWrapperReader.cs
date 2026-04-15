// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Fallback reader for plain SQS messages where the body IS the CloudEvents envelope.
/// This is NOT an <see cref="IWrapperReader"/> because it handles the "no wrapper" case.
/// </summary>
internal interface ISQSWrapperReader
{
    /// <summary>
    /// For SQS fallback: the body IS the envelope. No JSON parsing needed.
    /// Returns the message body as UTF-8 bytes and creates SQS metadata from the original message.
    /// </summary>
    /// <param name="utf8Body">The UTF-8 encoded body bytes (already converted by the caller).</param>
    /// <param name="originalMessage">The original SQS message (for SQS-level metadata).</param>
    /// <returns>The envelope UTF-8 bytes and SQS metadata.</returns>
    (ReadOnlyMemory<byte> InnerBodyUtf8, MessageMetadata Metadata) Extract(ReadOnlyMemory<byte> utf8Body, Message originalMessage);
}

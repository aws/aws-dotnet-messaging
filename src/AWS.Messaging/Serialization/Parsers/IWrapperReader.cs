// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS.Model;
using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Declares discriminator keys for wrapper detection and provides metadata/body extraction
/// from the raw UTF-8 bytes of the outer SQS message body.
/// </summary>
internal interface IWrapperReader
{
    /// <summary>
    /// Returns the wrapper type this reader handles.
    /// </summary>
    WrapperType WrapperType { get; }

    /// <summary>
    /// Returns the UTF-8 property names that, when all present at JSON depth 1,
    /// indicate this wrapper type. The classifier assigns each name a bit position.
    /// </summary>
    byte[][] GetDiscriminatorKeys();

    /// <summary>
    /// Called by the classifier after bitmap matching. Allows the reader to perform
    /// additional value-level validation (e.g., SNS "Type" == "Notification").
    /// </summary>
    /// <param name="result">The classification result containing the key bitmap and any captured values.</param>
    /// <returns>True if this reader should handle the message; false to fall through.</returns>
    bool Validate(in WrapperClassificationResult result);

    /// <summary>
    /// Extracts the inner envelope body as UTF-8 bytes and wrapper-specific metadata from the
    /// raw UTF-8 bytes. Called only when classification + validation succeeds.
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the outer SQS message body.</param>
    /// <param name="originalMessage">The original SQS message (for SQS-level metadata).</param>
    /// <param name="poolManager">Manager for renting/tracking ArrayPool buffers that will be returned when processing completes.</param>
    /// <returns>The inner envelope UTF-8 bytes and wrapper metadata.</returns>
    (ReadOnlyMemory<byte> InnerBodyUtf8, MessageMetadata Metadata) Extract(ReadOnlyMemory<byte> utf8Body, Message originalMessage, ArrayPoolManager poolManager);
}

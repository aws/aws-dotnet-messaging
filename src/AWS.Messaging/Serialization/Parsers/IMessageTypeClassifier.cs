// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Classifies incoming SQS message bodies by scanning first-level JSON property names
/// against registered wrapper reader discriminator keys.
/// </summary>
internal interface IMessageTypeClassifier
{
    /// <summary>
    /// Scans the first-level JSON property names in <paramref name="utf8Body"/> to build
    /// a key bitmap, then matches it against registered reader masks.
    /// Falls back to <see cref="WrapperType.Sqs"/> if no reader matches.
    /// When the message is SNS-wrapped without <c>MessageAttributes</c>, also captures all
    /// simple SNS fields and the inner body into <paramref name="poolManager"/>-rented buffers,
    /// populating <see cref="WrapperClassificationResult.CapturedMetadata"/> and
    /// <see cref="WrapperClassificationResult.CapturedInnerBody"/> so the caller can skip the
    /// dedicated <c>SNSWrapperReader</c> pass entirely.
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the SQS message body.</param>
    /// <param name="poolManager">Manager for renting/tracking ArrayPool buffers.</param>
    /// <returns>The classification result.</returns>
    WrapperClassificationResult Classify(ReadOnlyMemory<byte> utf8Body, ArrayPoolManager poolManager);

    /// <summary>
    /// Returns the <see cref="IWrapperReader"/> for the given wrapper type.
    /// </summary>
    /// <param name="wrapperType">The wrapper type to look up.</param>
    /// <returns>The matching reader.</returns>
    /// <exception cref="InvalidOperationException">If no reader is registered for the type.</exception>
    IWrapperReader GetReader(WrapperType wrapperType);
}

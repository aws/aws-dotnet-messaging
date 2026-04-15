// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

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
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the SQS message body.</param>
    /// <returns>The classification result.</returns>
    WrapperClassificationResult Classify(ReadOnlySpan<byte> utf8Body);

    /// <summary>
    /// Returns the <see cref="IWrapperReader"/> for the given wrapper type.
    /// </summary>
    /// <param name="wrapperType">The wrapper type to look up.</param>
    /// <returns>The matching reader.</returns>
    /// <exception cref="InvalidOperationException">If no reader is registered for the type.</exception>
    IWrapperReader GetReader(WrapperType wrapperType);
}

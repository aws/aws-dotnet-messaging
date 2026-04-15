// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Holds the result of the bitmap-based message classification pass.
/// Contains the detected wrapper type and any captured values needed for validation.
/// </summary>
internal readonly struct WrapperClassificationResult
{
    /// <summary>
    /// The wrapper type determined by the classifier.
    /// </summary>
    public WrapperType WrapperType { get; }

    /// <summary>
    /// The bitmap of matched discriminator keys.
    /// Each bit position corresponds to a key registered by an <see cref="IWrapperReader"/>.
    /// </summary>
    public ulong KeyBitmap { get; }

    /// <summary>
    /// The captured value of the "Type" property (if present at depth 1).
    /// Used by SNS reader to verify <c>"Type" == "Notification"</c>.
    /// </summary>
    public string? TypeValue { get; }

    /// <summary>
    /// Creates a new classification result.
    /// </summary>
    /// <param name="wrapperType">The determined wrapper type.</param>
    /// <param name="keyBitmap">The bitmap of matched keys.</param>
    /// <param name="typeValue">The captured "Type" property value, if any.</param>
    public WrapperClassificationResult(WrapperType wrapperType, ulong keyBitmap, string? typeValue)
    {
        WrapperType = wrapperType;
        KeyBitmap = keyBitmap;
        TypeValue = typeValue;
    }
}

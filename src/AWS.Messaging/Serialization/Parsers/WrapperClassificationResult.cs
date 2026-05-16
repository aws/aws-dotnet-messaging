// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Holds the result of the bitmap-based message classification pass.
/// Contains the detected wrapper type, any captured values needed for validation,
/// and optionally pre-extracted fields that allow downstream readers to skip a
/// second full pass over the outer body.
/// </summary>
/// <param name="WrapperType">The wrapper type determined by the classifier.</param>
/// <param name="TypeValue">
/// The captured value of the "Type" property (if present at depth 1).
/// Used by SNS reader to verify <c>"Type" == "Notification"</c>.
/// </param>
/// <param name="CapturedMetadata">
/// Fully constructed <see cref="MessageMetadata"/> built during the classify pass.
/// Non-null only when the classifier completed a zero-second-pass extraction
/// (i.e., SNS without <c>MessageAttributes</c>).
/// </param>
/// <param name="CapturedInnerBody">
/// The unescaped inner CloudEvent body decoded into a rented buffer during the classify pass.
/// Non-empty only when <see cref="CapturedMetadata"/> is non-null.
/// </param>
internal readonly record struct WrapperClassificationResult(
    WrapperType WrapperType,
    string? TypeValue,
    MessageMetadata? CapturedMetadata = null,
    ReadOnlyMemory<byte> CapturedInnerBody = default);

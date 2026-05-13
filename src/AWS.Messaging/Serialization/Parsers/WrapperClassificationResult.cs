// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Holds the result of the bitmap-based message classification pass.
/// Contains the detected wrapper type and any captured values needed for validation.
/// </summary>
/// <param name="WrapperType">The wrapper type determined by the classifier.</param>
/// <param name="TypeValue">
/// The captured value of the "Type" property (if present at depth 1).
/// Used by SNS reader to verify <c>"Type" == "Notification"</c>.
/// </param>
internal readonly record struct WrapperClassificationResult(
    WrapperType WrapperType,
    string? TypeValue);

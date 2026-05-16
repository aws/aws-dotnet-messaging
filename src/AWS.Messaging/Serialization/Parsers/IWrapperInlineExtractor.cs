// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json;
using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Implemented by wrapper readers that can capture their fields speculatively
/// during the classifier's single-pass scan, avoiding a dedicated second pass
/// over the outer body when all required data has already been observed.
/// </summary>
internal interface IWrapperInlineExtractor
{
    /// <summary>
    /// Attempts to recognise the current depth-1 property name and, if recognised,
    /// advances <paramref name="reader"/> past the value and updates the
    /// <paramref name="capturedBody"/>, <paramref name="capturedMetadata"/> and
    /// <paramref name="requiresFallback"/> accumulators.
    /// </summary>
    /// <param name="reader">
    /// The <see cref="Utf8JsonReader"/> positioned on a depth-1 <see cref="JsonTokenType.PropertyName"/> token.
    /// Implementors MUST advance past the corresponding value before returning <see langword="true"/>.
    /// </param>
    /// <param name="poolManager">Manager for renting/tracking ArrayPool buffers.</param>
    /// <param name="bitmap">
    /// The classifier's running discriminator bitmap. Implementors MUST set the relevant bit(s)
    /// for any property that is also a registered discriminator key.
    /// </param>
    /// <param name="keyToBitMask">Read-only map from UTF-8 key name to bit mask, for discriminator bit lookup.</param>
    /// <param name="capturedBody">
    /// Set to the unescaped inner body bytes when the payload field is encountered.
    /// Untouched for all other property names.
    /// </param>
    /// <param name="capturedMetadata">
    /// Lazily initialised with a <see cref="MessageMetadata"/> instance once the first
    /// relevant property is found; further calls update the same instance.
    /// </param>
    /// <param name="requiresFallback">
    /// Set to <see langword="true"/> when a property is encountered whose value cannot
    /// be captured inline (e.g., a complex sub-object), signalling that the dedicated
    /// <see cref="IWrapperReader.Extract"/> pass is still required.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the property was recognised and consumed; the caller
    /// must then skip its own generic skip/bitmap check for this property.
    /// <see langword="false"/> if the property is unknown to this extractor.
    /// </returns>
    bool TryCaptureProperty(
        ref Utf8JsonReader reader,
        ArrayPoolManager poolManager,
        ref ulong bitmap,
        FrozenDictionary<string, ulong> keyToBitMask,
        ref ReadOnlyMemory<byte> capturedBody,
        ref MessageMetadata? capturedMetadata,
        ref bool requiresFallback);

    /// <summary>
    /// Returns <see langword="true"/> when the inline capture produced a complete result
    /// that allows the dedicated <see cref="IWrapperReader.Extract"/> pass to be skipped.
    /// </summary>
    /// <param name="capturedBody">The body captured during the classify pass.</param>
    /// <param name="capturedMetadata">The metadata captured during the classify pass.</param>
    /// <param name="requiresFallback">Whether a complex property forced a fallback flag.</param>
    bool IsCaptureSufficient(
        ReadOnlyMemory<byte> capturedBody,
        MessageMetadata? capturedMetadata,
        bool requiresFallback);
}

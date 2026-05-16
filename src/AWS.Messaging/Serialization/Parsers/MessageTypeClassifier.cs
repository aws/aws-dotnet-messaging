// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json;
using AWS.Messaging.Serialization.Helpers;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Classifies incoming SQS message bodies by scanning depth-1 JSON property names
/// against a bitmap built from <see cref="IWrapperReader"/> discriminator keys.
/// Resolves readers from DI via <see cref="IEnumerable{IWrapperReader}"/>.
/// </summary>
internal sealed class MessageTypeClassifier : IMessageTypeClassifier
{
    // Pre-encoded UTF-8 bytes for the "Type" property name, used to capture the SNS "Type" value.
    private static readonly byte[] s_typePropertyUtf8 = "Type"u8.ToArray();

    private readonly ReaderEntry[] _entries;
    private readonly FrozenDictionary<string, ulong> _keyToBitMask;
    private readonly KeyEntry[] _keyEntries;

    /// <summary>
    /// Creates a classifier from the DI-injected wrapper readers.
    /// Each reader's discriminator keys are assigned sequential bit positions.
    /// Readers that also implement <see cref="IWrapperInlineExtractor"/> will have their
    /// fields captured speculatively during the classify pass.
    /// </summary>
    /// <param name="readers">The wrapper readers registered in DI.</param>
    public MessageTypeClassifier(IEnumerable<IWrapperReader> readers)
    {
        var readerList = new List<ReaderEntry>();
        var keyToBitMask = new Dictionary<string, ulong>();
        var keyList = new List<KeyEntry>();
        byte bitPosition = 0;

        foreach (var reader in readers)
        {
            var keys = reader.GetDiscriminatorKeys();
            ulong requiredMask = 0;

            foreach (var key in keys)
            {
                if (bitPosition >= 64)
                    throw new InvalidOperationException("Too many discriminator keys registered (max 64).");

                var bitMask = 1UL << bitPosition;

                var keyString = System.Text.Encoding.UTF8.GetString(key);
                keyToBitMask[keyString] = bitMask;
                keyList.Add(new KeyEntry(key, bitMask));

                requiredMask |= bitMask;
                bitPosition++;
            }

            readerList.Add(new ReaderEntry(reader, keys, requiredMask, reader as IWrapperInlineExtractor));
        }

        _entries = [.. readerList];
        _keyToBitMask = keyToBitMask.ToFrozenDictionary();
        _keyEntries = [.. keyList];
    }

    /// <inheritdoc/>
    public WrapperClassificationResult Classify(ReadOnlyMemory<byte> utf8Body, ArrayPoolManager poolManager)
    {
        ulong bitmap = 0;
        string? typeValue = null;

        // Per-reader inline capture accumulators — only populated when a reader implements
        // IWrapperInlineExtractor and its TryCaptureProperty recognises the current property.
        ReadOnlyMemory<byte> capturedBody = default;
        MessageMetadata? capturedMetadata = null;
        bool requiresFallback = false;

        var reader = new Utf8JsonReader(utf8Body.Span);

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.CurrentDepth > 1)
                    reader.Skip();
                continue;
            }

            // Capture the "Type" discriminator value regardless of which reader uses it.
            if (reader.ValueTextEquals(s_typePropertyUtf8))
            {
                reader.Read();
                typeValue = reader.GetString();

                if (_keyToBitMask.TryGetValue("Type", out var typeBitMask))
                    bitmap |= typeBitMask;
                continue;
            }

            // Give each inline extractor a chance to claim the property.
            // If one does, it has already advanced the reader past the value (and set any discriminator bits).
            bool capturedByExtractor = false;
            foreach (var entry in _entries)
            {
                if (entry.InlineExtractor is { } extractor
                    && extractor.TryCaptureProperty(ref reader, poolManager, ref bitmap, _keyToBitMask, ref capturedBody, ref capturedMetadata, ref requiresFallback))
                {
                    capturedByExtractor = true;
                    break;
                }
            }
            if (capturedByExtractor)
                continue;

            // Match remaining properties against registered discriminator keys.
            foreach (var keyEntry in _keyEntries)
            {
                if (reader.ValueTextEquals(keyEntry.Utf8Key))
                {
                    bitmap |= keyEntry.BitMask;
                    break;
                }
            }

            // Skip the property value.
            reader.Read();
            if (reader.TokenType == JsonTokenType.StartObject ||
                reader.TokenType == JsonTokenType.StartArray)
                reader.Skip();
        }

        // Check each reader for a full bitmap match + validation.
        foreach (var entry in _entries)
        {
            if ((bitmap & entry.RequiredMask) == entry.RequiredMask)
            {
                var result = new WrapperClassificationResult(entry.Reader.WrapperType, typeValue);
                if (!entry.Reader.Validate(result))
                    continue;

                // If this reader also performed inline capture and the result is complete,
                // return the pre-built metadata + body and let the caller skip Extract entirely.
                if (entry.InlineExtractor is { } extractor
                    && extractor.IsCaptureSufficient(capturedBody, capturedMetadata, requiresFallback))
                {
                    return new WrapperClassificationResult(entry.Reader.WrapperType, typeValue, capturedMetadata, capturedBody);
                }

                return result;
            }
        }

        // Fallback: plain SQS message (body IS the envelope).
        return new WrapperClassificationResult(WrapperType.Sqs, typeValue);
    }

    /// <inheritdoc/>
    public IWrapperReader GetReader(WrapperType wrapperType)
    {
        foreach (var entry in _entries)
        {
            if (entry.Reader.WrapperType == wrapperType)
                return entry.Reader;
        }

        throw new InvalidOperationException($"No wrapper reader registered for type '{wrapperType}'.");
    }

    private readonly record struct ReaderEntry(
        IWrapperReader Reader,
        byte[][] Keys,
        ulong RequiredMask,
        IWrapperInlineExtractor? InlineExtractor);

    private readonly record struct KeyEntry(
        byte[] Utf8Key,
        ulong BitMask);
}

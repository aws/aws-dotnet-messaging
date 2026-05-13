// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json;

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

                // Convert byte[] key to string for dictionary lookup
                var keyString = System.Text.Encoding.UTF8.GetString(key);
                keyToBitMask[keyString] = bitMask;
                keyList.Add(new KeyEntry(key, bitMask));

                requiredMask |= bitMask;
                bitPosition++;
            }

            readerList.Add(new ReaderEntry(reader, keys, requiredMask));
        }

        _entries = [.. readerList];
        _keyToBitMask = keyToBitMask.ToFrozenDictionary();
        _keyEntries = [.. keyList];
    }

    /// <summary>
    /// Performs a single-pass Utf8JsonReader scan over the UTF-8 message body,
    /// matching depth-1 property names against registered discriminator keys.
    /// Returns the classification result (wrapper type + captured values).
    /// Falls back to <see cref="WrapperType.Sqs"/> if no reader matches.
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the SQS message body.</param>
    /// <returns>The classification result.</returns>
    public WrapperClassificationResult Classify(ReadOnlyMemory<byte> utf8Body)
    {
        ulong bitmap = 0;
        string? typeValue = null;

        var reader = new Utf8JsonReader(utf8Body.Span);

        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.CurrentDepth > 1)
                    reader.Skip();
                continue;
            }

            // Check if this property is "Type" to capture its value
            if (reader.ValueTextEquals(s_typePropertyUtf8))
            {
                reader.Read();
                typeValue = reader.GetString();

                // Also mark the bit for "Type" if it's a registered discriminator
                if (_keyToBitMask.TryGetValue("Type", out var typeBitMask))
                {
                    bitmap |= typeBitMask;
                }
                continue;
            }

            // Check if this property name matches any registered discriminator key
            // Use ValueTextEquals to avoid allocating strings for non-matching properties
            foreach (var keyEntry in _keyEntries)
            {
                if (reader.ValueTextEquals(keyEntry.Utf8Key))
                {
                    bitmap |= keyEntry.BitMask;
                    break;
                }
            }

            // Skip the property value
            reader.Read();
            if (reader.TokenType == JsonTokenType.StartObject ||
                reader.TokenType == JsonTokenType.StartArray)
                reader.Skip();
        }

        // Check each reader for a full bitmap match + validation
        foreach (var entry in _entries)
        {
            if ((bitmap & entry.RequiredMask) == entry.RequiredMask)
            {
                var result = new WrapperClassificationResult(entry.Reader.WrapperType, bitmap, typeValue);
                if (entry.Reader.Validate(result))
                    return result;
            }
        }

        // Fallback: plain SQS message (body IS the envelope)
        return new WrapperClassificationResult(WrapperType.Sqs, bitmap, typeValue);
    }

    /// <summary>
    /// Gets the <see cref="IWrapperReader"/> for the given wrapper type.
    /// </summary>
    /// <param name="wrapperType">The wrapper type to look up.</param>
    /// <returns>The matching reader.</returns>
    /// <exception cref="InvalidOperationException">If no reader is registered for the type.</exception>
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
        ulong RequiredMask);

    private readonly record struct KeyEntry(
        byte[] Utf8Key,
        ulong BitMask);
}

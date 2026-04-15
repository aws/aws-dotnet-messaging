// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

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

    /// <summary>
    /// Creates a classifier from the DI-injected wrapper readers.
    /// Each reader's discriminator keys are assigned sequential bit positions.
    /// </summary>
    /// <param name="readers">The wrapper readers registered in DI.</param>
    public MessageTypeClassifier(IEnumerable<IWrapperReader> readers)
    {
        var readerList = new List<ReaderEntry>();
        int bitPosition = 0;

        foreach (var reader in readers)
        {
            var keys = reader.GetDiscriminatorKeys();
            ulong requiredMask = 0;

            foreach (var key in keys)
            {
                if (bitPosition >= 64)
                    throw new InvalidOperationException("Too many discriminator keys registered (max 64).");

                requiredMask |= 1UL << bitPosition;
                bitPosition++;
            }

            readerList.Add(new ReaderEntry(reader, keys, requiredMask));
        }

        _entries = readerList.ToArray();
    }

    /// <summary>
    /// Performs a single-pass Utf8JsonReader scan over the UTF-8 message body,
    /// matching depth-1 property names against registered discriminator keys.
    /// Returns the classification result (wrapper type + captured values).
    /// Falls back to <see cref="WrapperType.Sqs"/> if no reader matches.
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the SQS message body.</param>
    /// <returns>The classification result.</returns>
    public WrapperClassificationResult Classify(ReadOnlySpan<byte> utf8Body)
    {
        ulong bitmap = 0;
        string? typeValue = null;

        var reader = new Utf8JsonReader(utf8Body);

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

                // Also mark the bit for any entry that includes "Type" as a discriminator
                SetBitsForKey(s_typePropertyUtf8, ref bitmap);
                continue;
            }

            // Try to match against all registered discriminator keys
            int globalBit = 0;
            bool matched = false;
            foreach (var entry in _entries)
            {
                foreach (var key in entry.Keys)
                {
                    if (reader.ValueTextEquals(key))
                    {
                        bitmap |= 1UL << globalBit;
                        matched = true;
                    }
                    globalBit++;
                    if (matched) break;
                }
                if (matched) break;
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

    private void SetBitsForKey(byte[] key, ref ulong bitmap)
    {
        int globalBit = 0;
        foreach (var entry in _entries)
        {
            foreach (var entryKey in entry.Keys)
            {
                if (key.AsSpan().SequenceEqual(entryKey))
                {
                    bitmap |= 1UL << globalBit;
                }
                globalBit++;
            }
        }
    }

    private readonly record struct ReaderEntry(
        IWrapperReader Reader,
        byte[][] Keys,
        ulong RequiredMask);
}

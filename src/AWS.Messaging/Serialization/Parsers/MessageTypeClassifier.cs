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

    // SNS field keys captured speculatively during the single classify pass.
    // Bare SQS envelopes never have these property names, so no wasted work on the common path.
    private static readonly byte[] s_snsMessage          = "Message"u8.ToArray();
    private static readonly byte[] s_snsMessageId        = "MessageId"u8.ToArray();
    private static readonly byte[] s_snsTopicArn         = "TopicArn"u8.ToArray();
    private static readonly byte[] s_snsSubject          = "Subject"u8.ToArray();
    private static readonly byte[] s_snsUnsubscribeUrl   = "UnsubscribeURL"u8.ToArray();
    private static readonly byte[] s_snsTimestamp        = "Timestamp"u8.ToArray();
    private static readonly byte[] s_snsMessageAttributes = "MessageAttributes"u8.ToArray();

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
    /// <para>
    /// For SNS messages without <c>MessageAttributes</c>, all simple fields and the inner
    /// body are captured inline, populating <see cref="WrapperClassificationResult.CapturedMetadata"/>
    /// and <see cref="WrapperClassificationResult.CapturedInnerBody"/> so the caller can skip
    /// the dedicated <see cref="SNSWrapperReader"/> pass entirely.
    /// </para>
    /// </summary>
    /// <param name="utf8Body">The raw UTF-8 bytes of the SQS message body.</param>
    /// <param name="poolManager">Manager for renting/tracking ArrayPool buffers.</param>
    /// <returns>The classification result.</returns>
    public WrapperClassificationResult Classify(ReadOnlyMemory<byte> utf8Body, ArrayPoolManager poolManager)
    {
        ulong bitmap = 0;
        string? typeValue = null;

        // SNS field capture — populated speculatively during the pass.
        // These properties do not exist in bare SQS envelopes, so branches are never taken
        // on the non-SNS path and there is no wasted allocation.
        SNSMetadata? snsCandidate = null;
        ReadOnlyMemory<byte> capturedInnerBody = default;
        bool hasMessageAttributes = false;

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

            // All SNS outer-envelope property names are PascalCase (Message, MessageId, TopicArn, …).
            // CloudEvent, SQS, and EventBridge discriminator keys are all camelCase (id, source, detail, …).
            // A single first-byte uppercase check gates every SNS comparison — zero cost on the SQS fast path.
            if (reader.ValueSpan[0] >= 'A' && reader.ValueSpan[0] <= 'Z')
            {
                // SNS "Message": unescape directly into a rented buffer — no intermediate string
                if (reader.ValueTextEquals(s_snsMessage))
                {
                    reader.Read();
                    int maxBytes = reader.ValueSpan.Length;
                    byte[] buf = poolManager.Rent(maxBytes);
                    int written = reader.CopyString(buf);
                    capturedInnerBody = buf.AsMemory(0, written);
                    continue;
                }

                // Remaining SNS fields — lazy-init SNSMetadata only if at least one is found
                if (reader.ValueTextEquals(s_snsMessageId))
                {
                    reader.Read();
                    (snsCandidate ??= new SNSMetadata()).MessageId = reader.TokenType != JsonTokenType.Null ? reader.GetString() : null;
                    if (_keyToBitMask.TryGetValue("MessageId", out var messageIdBit)) bitmap |= messageIdBit;
                    continue;
                }
                if (reader.ValueTextEquals(s_snsTopicArn))
                {
                    reader.Read();
                    (snsCandidate ??= new SNSMetadata()).TopicArn = reader.TokenType != JsonTokenType.Null ? reader.GetString() : null;
                    if (_keyToBitMask.TryGetValue("TopicArn", out var topicArnBit)) bitmap |= topicArnBit;
                    continue;
                }
                if (reader.ValueTextEquals(s_snsSubject))
                {
                    reader.Read();
                    (snsCandidate ??= new SNSMetadata()).Subject = reader.TokenType != JsonTokenType.Null ? reader.GetString() : null;
                    continue;
                }
                if (reader.ValueTextEquals(s_snsUnsubscribeUrl))
                {
                    reader.Read();
                    (snsCandidate ??= new SNSMetadata()).UnsubscribeURL = reader.TokenType != JsonTokenType.Null ? reader.GetString() : null;
                    continue;
                }
                if (reader.ValueTextEquals(s_snsTimestamp))
                {
                    reader.Read();
                    (snsCandidate ??= new SNSMetadata()).Timestamp = reader.TokenType != JsonTokenType.Null ? reader.GetDateTimeOffset() : default;
                    continue;
                }
                if (reader.ValueTextEquals(s_snsMessageAttributes))
                {
                    // Complex sub-object — flag its presence and skip; SNSWrapperReader handles it in fallback
                    hasMessageAttributes = true;
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                        reader.Skip();
                    continue;
                }
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
                var result = new WrapperClassificationResult(entry.Reader.WrapperType, typeValue);
                if (!entry.Reader.Validate(result))
                    continue;

                // SNS fast path: all simple fields captured inline — build MessageMetadata directly
                // and skip the dedicated SNSWrapperReader pass. Falls back when MessageAttributes
                // are present (requires a dedicated sub-object parse pass).
                if (entry.Reader.WrapperType == WrapperType.Sns
                    && !capturedInnerBody.IsEmpty
                    && !hasMessageAttributes)
                {
                    var capturedMetadata = new MessageMetadata
                    {
                        SNSMetadata = snsCandidate ?? new SNSMetadata()
                    };
                    return new WrapperClassificationResult(WrapperType.Sns, typeValue, capturedMetadata, capturedInnerBody);
                }

                return result;
            }
        }

        // Fallback: plain SQS message (body IS the envelope)
        return new WrapperClassificationResult(WrapperType.Sqs, typeValue);
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

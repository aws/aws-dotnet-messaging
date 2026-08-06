// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Provides optimized deserialization of application messages directly from UTF-8 encoded JSON bytes,
/// avoiding the intermediate string allocation and UTF-16 conversion that occurs with
/// <see cref="IMessageSerializer.Deserialize(string, Type)"/>.
/// Complements the write optimizations provided by <see cref="IMessageSerializerUtf8JsonWriter"/>.
/// </summary>
internal interface IMessageSerializerUtf8JsonReader
{
    /// <summary>
    /// Deserializes an application message directly from UTF-8 encoded JSON bytes,
    /// eliminating the intermediate string allocation and UTF-16 conversion that occurs
    /// with <see cref="IMessageSerializer.Deserialize(string, Type)"/>.
    /// </summary>
    /// <param name="utf8Json">The raw UTF-8 JSON bytes representing the serialized message.</param>
    /// <param name="deserializedType">The target .NET type for deserialization.</param>
    /// <returns>The deserialized message object.</returns>
    object DeserializeFromUtf8Bytes(ReadOnlySpan<byte> utf8Json, Type deserializedType);
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Supports deserialization of domain-specific application messages directly from a <see cref="JsonElement"/>,
/// avoiding the intermediate string allocation and re-parse that occurs with <see cref="IMessageSerializer.Deserialize(string, Type)"/>.
/// This interface extends <see cref="IMessageSerializer"/> to provide allocation-free deserialization,
/// mirroring the pattern established by <see cref="IMessageSerializerUtf8JsonWriter"/> for serialization.
/// </summary>
public interface IMessageSerializerUtf8JsonReader
{
    /// <summary>
    /// Deserializes the .NET message object directly from a <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="element">The <see cref="JsonElement"/> containing the data to deserialize.</param>
    /// <param name="deserializedType">The .NET type to deserialize the element into.</param>
    /// <returns>The deserialized object.</returns>
    object DeserializeFromElement(JsonElement element, Type deserializedType);
}

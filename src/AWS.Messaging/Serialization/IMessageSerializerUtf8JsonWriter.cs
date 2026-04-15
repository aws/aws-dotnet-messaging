// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace AWS.Messaging.Serialization;

/// <summary>
/// Supports serialization and deserialization of domain-specific application messages.
/// This interface extends <see cref="IMessageSerializer"/> to provide a methods for allocation-free serialization/deserialization.
/// </summary>
public interface IMessageSerializerUtf8JsonWriter
{
    /// <summary>
    /// Serializes the .NET message object into a UTF-8 JSON string using a Utf8JsonWriter.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter to write the serialized data.</param>
    /// <param name="value">The .NET object that will be serialized.</param>
    void SerializeToBuffer<T>(Utf8JsonWriter writer, T value);

    /// <summary>
    /// Gets the MIME type of the content.
    /// </summary>
    string ContentType { get; }
}

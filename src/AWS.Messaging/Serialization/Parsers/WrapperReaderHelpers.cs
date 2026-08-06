// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AWS.Messaging.Serialization.Parsers;

/// <summary>
/// Shared helper methods for wrapper reader implementations.
/// Provides common JSON reading operations used across EventBridge, SNS, and other wrapper readers.
/// </summary>
internal static class WrapperReaderHelpers
{
    /// <summary>
    /// Reads a nullable string value from the current JSON reader position.
    /// Advances the reader and returns null if the token is null, otherwise returns the string value.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at a property name.</param>
    /// <returns>The string value or null if the JSON value is null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? ReadNullableString(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
    }

    /// <summary>
    /// Skips an unknown property value in the JSON stream.
    /// Advances the reader and handles both simple values and complex objects/arrays.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at a property name.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SkipUnknownProperty(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.StartObject ||
            reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Skip();
        }
    }
}

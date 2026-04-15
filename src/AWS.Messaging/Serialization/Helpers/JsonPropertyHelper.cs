// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Serialization.Helpers;

/// <summary>
/// Provides helper methods for safely extracting values from dictionary objects.
/// </summary>
internal static class JsonPropertyHelper
{
    /// <summary>
    /// Safely extracts a value from a dictionary.
    /// </summary>
    /// <param name="attributes">The dictionary containing the value.</param>
    /// <param name="key">The key of the value to extract.</param>
    /// <returns>The value or null if the key doesn't exist.</returns>
    public static string? GetAttributeValue(Dictionary<string, string> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value) ? value : null;
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace AWS.Messaging.Serialization.Helpers;

/// <summary>
/// Provides helper methods for safely extracting values from JsonElement and Dictionary objects.
/// </summary>
internal static class JsonPropertyHelper
{
    /// <summary>
    /// Safely extracts a value from a JsonElement using the provided conversion function.
    /// </summary>
    /// <typeparam name="T">The type to convert the property value to.</typeparam>
    /// <param name="root">The root JsonElement containing the property.</param>
    /// <param name="propertyName">The name of the property to extract.</param>
    /// <param name="getValue">The function to convert the property value to type T.</param>
    /// <returns>The converted value or default if the property doesn't exist.</returns>
    public static T? GetPropertyValue<T>(JsonElement root, string propertyName, Func<JsonElement, T> getValue)
    {
        ArgumentNullException.ThrowIfNull(getValue);

        return root.TryGetProperty(propertyName, out var property) ? getValue(property) : default;
    }

    /// <summary>
    /// Extracts a required value from a JsonElement using the provided conversion function.
    /// </summary>
    /// <typeparam name="T">The type to convert the property value to.</typeparam>
    /// <param name="root">The root JsonElement containing the property.</param>
    /// <param name="propertyName">The name of the property to extract.</param>
    /// <param name="getValue">The function to convert the property value to type T.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the property is missing or conversion fails.</exception>
    public static T GetRequiredProperty<T>(JsonElement root, string propertyName, Func<JsonElement, T> getValue)
    {
        if (root.TryGetProperty(propertyName, out var property))
        {
            try
            {
                return getValue(property);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to get or convert property '{propertyName}'", ex);
            }
        }
        throw new InvalidDataException($"Required property '{propertyName}' is missing");
    }

    /// <summary>
    /// Safely extracts a string value from a JsonElement.
    /// </summary>
    /// <param name="root">The root JsonElement containing the property.</param>
    /// <param name="propertyName">The name of the property to extract.</param>
    /// <returns>The string value or null if the property doesn't exist.</returns>
    public static string? GetStringProperty(JsonElement root, string propertyName)
        => GetPropertyValue(root, propertyName, element => element.GetString());

    /// <summary>
    /// Safely extracts a DateTimeOffset value from a JsonElement.
    /// </summary>
    /// <param name="root">The root JsonElement containing the property.</param>
    /// <param name="propertyName">The name of the property to extract.</param>
    /// <returns>The DateTimeOffset value or null if the property doesn't exist.</returns>
    public static DateTimeOffset? GetDateTimeOffsetProperty(JsonElement root, string propertyName)
        => GetPropertyValue(root, propertyName, element => element.GetDateTimeOffset());

    /// <summary>
    /// Safely extracts a Uri value from a JsonElement.
    /// </summary>
    /// <param name="root">The root JsonElement containing the property.</param>
    /// <param name="propertyName">The name of the property to extract.</param>
    /// <returns>The Uri value or null if the property doesn't exist.</returns>
    public static Uri? GetUriProperty(JsonElement root, string propertyName)
        => GetPropertyValue(root, propertyName, element => new Uri(element.GetString()!, UriKind.RelativeOrAbsolute));

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

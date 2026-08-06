// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using AWS.Messaging.Serialization.Helpers;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests.Helpers;

public class JsonPropertyHelperTests
{
    [Fact]
    public void GetAttributeValue_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var attributes = new Dictionary<string, string>
        {
            { "testKey", "testValue" }
        };

        // Act
        var result = JsonPropertyHelper.GetAttributeValue(attributes, "testKey");

        // Assert
        Assert.Equal("testValue", result);
    }

    [Fact]
    public void GetAttributeValue_WithMissingKey_ReturnsNull()
    {
        // Arrange
        var attributes = new Dictionary<string, string>
        {
            { "otherKey", "value" }
        };

        // Act
        var result = JsonPropertyHelper.GetAttributeValue(attributes, "testKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAttributeValue_WithEmptyDictionary_ReturnsNull()
    {
        // Arrange
        var attributes = new Dictionary<string, string>();

        // Act
        var result = JsonPropertyHelper.GetAttributeValue(attributes, "testKey");

        // Assert
        Assert.Null(result);
    }
}

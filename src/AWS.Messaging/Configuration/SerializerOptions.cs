// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AWS.Messaging.Configuration;

/// <summary>
/// This class serves as a container to hold various serializer options that can control
/// the serialization/de-serialization logic of the application message.
/// </summary>
public class SerializationOptions
{
    /// <summary>
    /// This is an instance of <see cref="JsonSerializerOptions"/> that controls the serialization/de-serialization logic of the application message.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    public JsonSerializerOptions? SystemTextJsonOptions { get; set; } = new JsonSerializerOptions
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// When set to true, it will clean the rented buffers after each use.
    /// </summary>
    /// <remarks>
    /// Setting this to false can improve performance in high-throughput scenarios at cost of potential security issues
    /// </remarks>
    public bool CleanRentedBuffers { get; set; } = true;

    /// <summary>
    /// Default constructor
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050",
        Justification = "Consumers relying on trimming would have been required to call the AddAWSMessageBus overload that takes in JsonSerializerContext that will be used here to avoid the call that requires unreferenced code.")]
    public SerializationOptions()
    {

    }
}

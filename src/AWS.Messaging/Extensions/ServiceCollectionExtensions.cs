// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds extension methods to <see cref="IServiceCollection"/> to allow the configuration of the framework at application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="IMessageBusBuilder"/> to the dependency injection framework
    /// to allow access to the framework components throughout the application.
    /// Allows the configuration of the messaging framework by exposing methods to add publishers and subscribers.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="builder">An action to define the message framework configuration using <see cref="IMessageBusBuilder"/></param>
    [RequiresUnreferencedCode("This API requires using unreferenced code for reflection based JSON serialization. Use AddAWSMessageBus overload that takes JsonSerializerContext parameter to avoid using unreferenced code.")]
    public static IServiceCollection AddAWSMessageBus(this IServiceCollection services, Action<IMessageBusBuilder> builder)
    {
        return ConfigureMessagingServices(services, new NullMessageJsonSerializerContextContainer(), builder);
    }

    /// <summary>
    /// Adds the <see cref="IMessageBusBuilder"/> to the dependency injection framework
    /// to allow access to the framework components throughout the application.
    /// Allows the configuration of the messaging framework by exposing methods to add publishers and subscribers.
    /// <para>
    /// When this overload is called with a <see cref="JsonSerializerContext"/> the serialization and deserialization of .NET types to JSON messages will use
    /// .NET's source generator implementation. Passing in a <see cref="JsonSerializerContext"/> is required when using this library in Native AOT or other trimming environments.
    /// </para>
    /// <para>
    /// The <see cref="JsonSerializerContext"/> must have <see cref="JsonSerializableAttribute"/> attributes for all .NET types used for serialization and deserialization into
    /// messages. If the .NET types have any enum properties and the string representation of the enum should be used then add the
    /// "[JsonSourceGenerationOptions(UseStringEnumConverter = true)]" attribute to the <see cref="JsonSerializerContext"/>.
    /// </para>
    /// <para>
    /// For more information about JSON source generator and the <see cref="JsonSerializerContext"/> visit this link: 
    /// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
    /// </para>
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="jsonSerializerContext">The <see cref="JsonSerializerContext"/> that will be used for serializing and deserializing the .NET types defined by consumers for representing messages.</param>
    /// <param name="builder">An action to define the message framework configuration using <see cref="MessageBusBuilder"/></param>
    public static IServiceCollection AddAWSMessageBus(this IServiceCollection services, JsonSerializerContext jsonSerializerContext, Action<MessageBusBuilder> builder)
    {
        return ConfigureMessagingServices(services, new DefaultMessageJsonSerializerContextContainer(jsonSerializerContext), builder);
    }

    private static IServiceCollection ConfigureMessagingServices(IServiceCollection services, IMessageJsonSerializerContextContainer messageJsonSerializerContextFactory, Action<MessageBusBuilder> builder)
    {
        services.AddSingleton<IMessageJsonSerializerContextContainer>(messageJsonSerializerContextFactory);

        var messageBusBuilder = new MessageBusBuilder(services);

        builder(messageBusBuilder);
        messageBusBuilder.Build();

        return services;
    }
}

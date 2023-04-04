// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Configuration;

/// <summary>
/// This builder interface is used to configure the AWS messaging framework, including adding publishers and subscribers.
/// </summary>
public interface IMessageBusBuilder
{
    /// <summary>
    /// Adds an SQS Publisher to the framework which will handle publishing
    /// the defined message type to the specified SQS queues URL.
    /// </summary>
    /// <param name="queueUrl">The SQS queue URL to publish the message to.</param>
    /// <param name="messageTypeIdentifier">The language-agnostic message type identifier. If not specified, the .NET type will be used.</param>
    IMessageBusBuilder AddSQSPublisher<TMessage>(string queueUrl, string? messageTypeIdentifier = null);

    /// <summary>
    /// Adds an SNS Publisher to the framework which will handle publishing
    /// the defined message type to the specified SNS topic URL.
    /// </summary>
    /// <param name="topicUrl">The SNS topic URL to publish the message to.</param>
    /// <param name="messageTypeIdentifier">The language-agnostic message type identifier. If not specified, the .NET type will be used.</param>
    IMessageBusBuilder AddSNSPublisher<TMessage>(string topicUrl, string? messageTypeIdentifier = null);

    /// <summary>
    /// Adds an EventBridge Publisher to the framework which will handle publishing the defined message type to the specified EventBridge event bus name.
    /// If you are specifying a global endpoint ID via <see cref="EventBridgePublishOptions"/>, then you must also include the <see href="https://www.nuget.org/packages/AWSSDK.Extensions.CrtIntegration">AWSSDK.Extensions.CrtIntegration</see> package in your application.
    /// </summary>
    /// <param name="eventBusName">The EventBridge event bus name or ARN where the message will be published.</param>
    /// <param name="messageTypeIdentifier">The language-agnostic message type identifier. If not specified, the .NET type will be used.</param>
    /// <param name="options">Contains additional properties that can be set while configuring an EventBridge publisher</param>
    IMessageBusBuilder AddEventBridgePublisher<TMessage>(string eventBusName, string? messageTypeIdentifier = null, EventBridgePublishOptions? options = null);

    /// <summary>
    /// Add a message handler for a given message type.
    /// The message handler contains the business logic of how to process a given message type.
    /// </summary>
    /// <param name="messageTypeIdentifier">The language-agnostic message type identifier. If not specified, the .NET type will be used.</param>
    IMessageBusBuilder AddMessageHandler<THandler, TMessage>(string? messageTypeIdentifier = null)
        where THandler : IMessageHandler<TMessage>;

    /// <summary>
    /// Adds an SQS queue to poll for messages.
    /// </summary>
    /// <param name="queueUrl">The SQS queue to poll for messages.</param>
    /// <param name="options">Optional configuration for polling message from SQS.</param>
    IMessageBusBuilder AddSQSPoller(string queueUrl, Action<SQSMessagePollerOptions>? options = null);

    /// <summary>
    /// Configures an instance of <see cref="SerializationOptions"/> to control the serialization/de-serialization logic for the application message.
    /// </summary>
    IMessageBusBuilder ConfigureSerializationOptions(Action<SerializationOptions> options);
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Configuration;
using AWS.Messaging.Lambda;
using AWS.Messaging.Lambda.Services;
using AWS.Messaging.Lambda.Services.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding support for AWS Lambda functions to use AWS.Messaging for message processing.
/// </summary>
public static class MessageBusBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="ILambdaMessaging"/> service to the services collection. The <see cref="ILambdaMessaging"/> is used in the Lambda function to pass in the incoming SQS events into
    /// AWS Messaging framework and dispatched to the registered IMessageHandler types.
    /// </summary>
    /// <param name="messageBusBuilder"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public static IMessageBusBuilder AddLambdaMessageProcessor(this IMessageBusBuilder messageBusBuilder, Action<LambdaMessagingOptions>? options = null)
    {
        var lambdaMessagingOptions = new LambdaMessagingOptions();

        options?.Invoke(lambdaMessagingOptions);

        lambdaMessagingOptions.Validate();

        messageBusBuilder.AddAdditionalService(new ServiceDescriptor(typeof(LambdaMessagingOptions), lambdaMessagingOptions));
        messageBusBuilder.AddAdditionalService(new ServiceDescriptor(typeof(ILambdaMessaging), typeof(DefaultLambdaMessaging), ServiceLifetime.Singleton));
        messageBusBuilder.AddAdditionalService(new ServiceDescriptor(typeof(ILambdaMessageProcessorFactory), typeof(DefaultLambdaMessageProcessorFactory), ServiceLifetime.Singleton));

        LambdaContextServiceHolder.Register(messageBusBuilder);

        return messageBusBuilder;
    }
}


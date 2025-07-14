// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using AWS.Messaging.Telemetry;
using AWS.Messaging.UnitTests.MessageHandlers;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests;

/// <summary>
/// Tests for <see cref="HandlerInvoker"/>
/// </summary>
public class HandlerInvokerTests
{
    /// <summary>
    /// Tests that a single handler can be invoked successfully
    /// </summary>
    [Fact]
    public async Task HandlerInvoker_HappyPath()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider));

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);
    }

    /// <summary>
    /// Tests that the correct methods are invoked on a single type that
    /// implements the handler for multiple message types
    /// </summary>
    [Fact]
    public async Task HandlerInvoker_DualHandler_InvokesCorrectMethod()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<DualHandler, ChatMessage>("sqsQueueUrl");
                builder.AddMessageHandler<DualHandler, AddressInfo>("sqsQueueUrl");
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider));

        // Assert that ChatMessage is routed to the right handler method, which always succeeds
        var chatEnvelope = new MessageEnvelope<ChatMessage>();
        var chatSubscriberMapping = SubscriberMapping.Create<DualHandler, ChatMessage>();
        var chatMessageProcessStatus = await handlerInvoker.InvokeAsync(chatEnvelope, chatSubscriberMapping);

        Assert.True(chatMessageProcessStatus.IsSuccess);

        // Assert that AddressInfo is routed to the right handler method, which always fails
        var addressEnvelope = new MessageEnvelope<AddressInfo>();
        var addressSubscriberMapping = SubscriberMapping.Create<DualHandler, AddressInfo>();
        var addressMessageProcessStatus = await handlerInvoker.InvokeAsync(addressEnvelope, addressSubscriberMapping);

        Assert.True(addressMessageProcessStatus.IsFailed);
    }

    /// <summary>
    /// Tests that a exception thrown by a handler is logged correctly, since
    /// the handler is invoked via reflection we want to log the inner exception and
    /// not the TargetInvocationException
    /// </summary>
    [Fact]
    public async Task HandlerInvoker_UnwrapsTargetInvocationException()
    {
        var mockLogger = new Mock<ILogger<HandlerInvoker>>();

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatExceptionHandler, ChatMessage>("sqsQueueUrl");
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            mockLogger.Object,
            new DefaultTelemetryFactory(serviceProvider));
        var envelope = new MessageEnvelope<ChatMessage>()
        {
            Id = "123"
        };
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandler, ChatMessage>();

        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        mockLogger.VerifyLogError(typeof(CustomHandlerException), "A handler exception occurred while handling message ID 123.");
    }

    /// <summary>
    /// Tests that all message handlers are registered and retrieved as scoped dependencies in the service collection.
    /// </summary>
    [Fact]
    public async Task HandlerInvoker_VerifyHandlersAreRetrievedAsScopedDependencies()
    {
        // ARRANGE
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<GreetingHandler, string>("sqsQueueUrl");
            });

        serviceCollection.AddScoped<IGreeter, Greeter>();
        serviceCollection.AddSingleton<TempStorage<string>>();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider));

        // ACT and ASSERT - Invoke the GreetingHandler multiple times and verify that a new instance of IGreeter is created each time.
        var envelope = new MessageEnvelope<string>();
        var subscriberMapping = SubscriberMapping.Create<GreetingHandler, string>();

        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        var tempStorage = serviceProvider.GetRequiredService<TempStorage<string>>();
        var messageStorage2 = new HashSet<string>();
        foreach (var item in tempStorage.Messages)
        {
            messageStorage2.Add(item.Message);
        }
        Assert.Equal(3, messageStorage2.Count);
    }

    [Fact]
    public async Task HandlerInvoker_VerifyHandlersFatalErrorWhenDIFails()
    {
        var serviceCollection = new ServiceCollection()
           .AddAWSMessageBus(builder =>
           {
               builder.AddMessageHandler<ChatMessageHandlerWithDependencies, ChatMessage>();
           }).AddSingleton<IDependentThing>(x =>
           {
               var thingDoer = x.GetRequiredService<IThingDoer>();
               throw new InvalidOperationException("Blah blah"); // intentionally make the DI fail.
           });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider));

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandlerWithDependencies, ChatMessage>();
        await Assert.ThrowsAsync<InvalidMessageHandlerSignatureException>(async () =>
        {
            await handlerInvoker.InvokeAsync(envelope, subscriberMapping);
        });
    }

    [Fact]
    public async Task HandlerInvoker_ScopeDispose()
    {
        var serviceCollection = new ServiceCollection()
           .AddAWSMessageBus(builder =>
           {
               builder.AddMessageHandler<ChatMessageHandlerWithDisposableServices, ChatMessage>();
           });

        serviceCollection.AddScoped<ChatMessageHandlerWithDisposableServices.TestDisposableService, ChatMessageHandlerWithDisposableServices.TestDisposableService>();
        serviceCollection.AddScoped<ChatMessageHandlerWithDisposableServices.TestDisposableServiceAsync, ChatMessageHandlerWithDisposableServices.TestDisposableServiceAsync>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        ChatMessageHandlerWithDisposableServices.TestDisposableService.CallCount = 0;
        ChatMessageHandlerWithDisposableServices.TestDisposableServiceAsync.CallCount = 0;

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider));

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandlerWithDisposableServices, ChatMessage>();
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(1, ChatMessageHandlerWithDisposableServices.TestDisposableService.CallCount);
        Assert.Equal(1, ChatMessageHandlerWithDisposableServices.TestDisposableServiceAsync.CallCount);
    }
}

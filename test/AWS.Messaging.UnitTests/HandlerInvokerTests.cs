// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
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
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

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
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

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
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());
        var envelope = new MessageEnvelope<ChatMessage>()
        {
            Id = "123"
        };
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandler, ChatMessage>();

        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        mockLogger.VerifyLogError(typeof(CustomHandlerException), "An unexpected exception occurred while handling message ID 123.");
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
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

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
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

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
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandlerWithDisposableServices, ChatMessage>();
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(1, ChatMessageHandlerWithDisposableServices.TestDisposableService.CallCount);
        Assert.Equal(1, ChatMessageHandlerWithDisposableServices.TestDisposableServiceAsync.CallCount);
    }

    /// <summary>
    /// Tests that middleware is executed in the order of registration.
    /// </summary>
    [Fact]
    public async Task Middleware_IsExecutedInOrderOfRegistration()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>("sqsQueueUrl");

                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.A>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.B>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.C>();
            });

        var middlewareTracker = new SubscriberMiddlewareModels.MiddlewareTracker();
        serviceCollection.AddSingleton(middlewareTracker);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);

        Assert.Equal(4, middlewareTracker.Executed.Count);
        Assert.Equal(typeof(SubscriberMiddlewareModels.A), middlewareTracker.Executed[0]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.B), middlewareTracker.Executed[1]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.C), middlewareTracker.Executed[2]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>), middlewareTracker.Executed[3]);
    }

    /// <summary>
    /// Tests that middleware can propagate the message process status.
    /// </summary>
    [Fact]
    public async Task Middleware_MessageProcessStatusIsPropagated()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<SubscriberMiddlewareModels.FailMessageHandler<ChatMessage>, ChatMessage>("sqsQueueUrl");

                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.A>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.B>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.C>();
            });

        var middlewareTracker = new SubscriberMiddlewareModels.MiddlewareTracker();
        serviceCollection.AddSingleton(middlewareTracker);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<SubscriberMiddlewareModels.FailMessageHandler<ChatMessage>, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Failed(), messageProcessStatus);

        Assert.Equal(4, middlewareTracker.Executed.Count);
        Assert.Equal(typeof(SubscriberMiddlewareModels.A), middlewareTracker.Executed[0]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.B), middlewareTracker.Executed[1]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.C), middlewareTracker.Executed[2]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.FailMessageHandler<ChatMessage>), middlewareTracker.Executed[3]);
    }

    /// <summary>
    /// Tests that handlers that do not throw exceptions do not trigger the message error handler.
    /// </summary>
    [Fact]
    public async Task MessageErrorHandler_HandlerWithoutException_DoesNotExecuteMessageErrorHandler()
    {
        var mockMessageErrorHandler = new Mock<IMessageErrorHandler>();

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");
                builder.AddAdditionalService(ServiceDescriptor.Singleton(mockMessageErrorHandler.Object));
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that when the message error handler returns a failed response, the handler does not retry and that response is returned by the invoker.
    /// </summary>
    [Fact]
    public async Task MessageErrorHandler_WithFailed_DoesNotRetry()
    {
        var mockMessageErrorHandler = new Mock<IMessageErrorHandler>();
        mockMessageErrorHandler.Setup(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Failed));

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatExceptionHandler, ChatMessage>("sqsQueueUrl");
                builder.AddAdditionalService(ServiceDescriptor.Singleton(mockMessageErrorHandler.Object));
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandler, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Failed(), messageProcessStatus);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that when the message error handler returns a success response, the handler does not retry and that response is returned by the invoker.
    /// </summary>
    [Fact]
    public async Task MessageErrorHandler_WithSuccess_DoesNotRetry()
    {
        var mockMessageErrorHandler = new Mock<IMessageErrorHandler>();
        mockMessageErrorHandler.Setup(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Success));

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatExceptionHandler, ChatMessage>("sqsQueueUrl");
                builder.AddAdditionalService(ServiceDescriptor.Singleton(mockMessageErrorHandler.Object));
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandler, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that when the message error handler returns a retry response, the pipeline is re-executed.
    /// </summary>
    [Fact]
    public async Task MessageErrorHandler_WithRetry_ReExecutesPipeline()
    {
        var mockMessageErrorHandler = new Mock<IMessageErrorHandler>();
        mockMessageErrorHandler.SetupSequence(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Retry))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Retry))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Success));

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatExceptionHandler, ChatMessage>("sqsQueueUrl");
                builder.AddAdditionalService(ServiceDescriptor.Singleton(mockMessageErrorHandler.Object));
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            new MessageConfiguration());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandler, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), 1, It.IsAny<CancellationToken>()), Times.Once);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), 2, It.IsAny<CancellationToken>()), Times.Once);
        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that when the message error handler returns a retry response, the pipeline is executed in a new DI scope.
    /// </summary>
    [Fact]
    public async Task MessageErrorHandler_Retry_UsesNewScope()
    {
        var mockMessageErrorHandler = new Mock<IMessageErrorHandler>();
        mockMessageErrorHandler.SetupSequence(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Retry))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Retry))
            .Returns(ValueTask.FromResult(MessageErrorHandlerResponse.Success));

        var serviceCollection = new ServiceCollection()
           .AddAWSMessageBus(builder =>
           {
               builder.AddMessageHandler<ChatExceptionHandlerAndDisposableServices, ChatMessage>();
               builder.AddAdditionalService(ServiceDescriptor.Singleton(mockMessageErrorHandler.Object));
           });

        serviceCollection.AddScoped<ChatExceptionHandlerAndDisposableServices.TestDisposableService, ChatExceptionHandlerAndDisposableServices.TestDisposableService>();
        serviceCollection.AddScoped<ChatExceptionHandlerAndDisposableServices.TestDisposableServiceAsync, ChatExceptionHandlerAndDisposableServices.TestDisposableServiceAsync>();

        var serviceProvider = serviceCollection.BuildServiceProvider();

        ChatExceptionHandlerAndDisposableServices.TestDisposableService.CallCount = 0;
        ChatExceptionHandlerAndDisposableServices.TestDisposableServiceAsync.CallCount = 0;

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatExceptionHandlerAndDisposableServices, ChatMessage>();
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        mockMessageErrorHandler.Verify(x => x.OnHandleError(It.IsAny<MessageEnvelope<ChatMessage>>(), It.IsAny<Exception>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.Equal(3, ChatExceptionHandlerAndDisposableServices.TestDisposableService.CallCount);
        Assert.Equal(3, ChatExceptionHandlerAndDisposableServices.TestDisposableServiceAsync.CallCount);
    }

    /// <summary>
    /// Tests that middleware works correctly with multiple message types, verifying the
    /// delegate dispatch routes each type through the shared middleware to the correct handler.
    /// </summary>
    [Fact]
    public async Task Middleware_WithMultipleMessageTypes_RoutesCorrectly()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>("chatMessage");
                builder.AddMessageHandler<SubscriberMiddlewareModels.FailMessageHandler<AddressInfo>, AddressInfo>("addressInfo");

                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.A>();
            });

        var middlewareTracker = new SubscriberMiddlewareModels.MiddlewareTracker();
        serviceCollection.AddSingleton(middlewareTracker);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        // Process a ChatMessage - should route to SuccessMessageHandler
        var chatEnvelope = new MessageEnvelope<ChatMessage>();
        var chatMapping = SubscriberMapping.Create<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>();
        var chatResult = await handlerInvoker.InvokeAsync(chatEnvelope, chatMapping);

        Assert.Equal(MessageProcessStatus.Success(), chatResult);
        Assert.Equal(2, middlewareTracker.Executed.Count);
        Assert.Equal(typeof(SubscriberMiddlewareModels.A), middlewareTracker.Executed[0]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>), middlewareTracker.Executed[1]);

        // Process an AddressInfo - should route to FailMessageHandler through the same middleware
        var addressEnvelope = new MessageEnvelope<AddressInfo>();
        var addressMapping = SubscriberMapping.Create<SubscriberMiddlewareModels.FailMessageHandler<AddressInfo>, AddressInfo>();
        var addressResult = await handlerInvoker.InvokeAsync(addressEnvelope, addressMapping);

        Assert.Equal(MessageProcessStatus.Failed(), addressResult);
        Assert.Equal(4, middlewareTracker.Executed.Count);
        Assert.Equal(typeof(SubscriberMiddlewareModels.A), middlewareTracker.Executed[2]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.FailMessageHandler<AddressInfo>), middlewareTracker.Executed[3]);
    }

    /// <summary>
    /// Tests that middleware can short-circuit the pipeline by returning a result without calling next().
    /// Downstream middleware and the handler should NOT be executed.
    /// </summary>
    [Fact]
    public async Task Middleware_ShortCircuit_DoesNotExecuteDownstream()
    {
        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>("sqsQueueUrl");

                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.A>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.ShortCircuit>();
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.B>();
            });

        var middlewareTracker = new SubscriberMiddlewareModels.MiddlewareTracker();
        serviceCollection.AddSingleton(middlewareTracker);

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<SubscriberMiddlewareModels.SuccessMessageHandler<ChatMessage>, ChatMessage>();
        var messageProcessStatus = await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        Assert.Equal(MessageProcessStatus.Success(), messageProcessStatus);

        // Only A and ShortCircuit should execute - B and the handler should NOT
        Assert.Equal(2, middlewareTracker.Executed.Count);
        Assert.Equal(typeof(SubscriberMiddlewareModels.A), middlewareTracker.Executed[0]);
        Assert.Equal(typeof(SubscriberMiddlewareModels.ShortCircuit), middlewareTracker.Executed[1]);
    }

    /// <summary>
    /// Tests that the cancellation token is propagated through the middleware pipeline.
    /// </summary>
    [Fact]
    public async Task Middleware_CancellationToken_IsPropagatedThroughPipeline()
    {
        SubscriberMiddlewareModels.CancellationAwareMiddleware.Reset();

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.CancellationAwareMiddleware>();
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel the token

        // The cancelled token should be propagated to the middleware
        // Note: the framework catches exceptions, so the cancelled token may result in Failed status
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping, cts.Token);

        Assert.True(SubscriberMiddlewareModels.CancellationAwareMiddleware.TokenWasReceived, "Middleware should receive a non-default CancellationToken");
        Assert.True(SubscriberMiddlewareModels.CancellationAwareMiddleware.TokenWasCancelled, "Middleware should see the token is cancelled");
    }

    /// <summary>
    /// Tests that singleton middleware resolves the same instance across multiple invocations,
    /// while scoped middleware resolves a new instance per invocation.
    /// </summary>
    [Fact]
    public async Task Middleware_SingletonVsScoped_LifetimeBehavior()
    {
        SubscriberMiddlewareModels.InstanceTrackingMiddleware.Reset();

        var serviceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");

                // Register as Scoped - each invocation should get a new DI scope and thus a new instance
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.InstanceTrackingMiddleware>(ServiceLifetime.Scoped);
            });

        var serviceProvider = serviceCollection.BuildServiceProvider();

        var handlerInvoker = new HandlerInvoker(
            serviceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(serviceProvider),
            serviceProvider.GetRequiredService<IMessageConfiguration>());

        var envelope = new MessageEnvelope<ChatMessage>();
        var subscriberMapping = SubscriberMapping.Create<ChatMessageHandler, ChatMessage>();

        // Invoke 3 times - each should create a new scope and thus a new scoped middleware instance
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);
        await handlerInvoker.InvokeAsync(envelope, subscriberMapping);

        // With scoped lifetime, each invocation creates a new scope, so we should get different instance IDs
        Assert.Equal(3, SubscriberMiddlewareModels.InstanceTrackingMiddleware.ResolvedInstanceIds.Count);
        var uniqueIds = new HashSet<int>(SubscriberMiddlewareModels.InstanceTrackingMiddleware.ResolvedInstanceIds);
        Assert.Equal(3, uniqueIds.Count); // 3 different instances for scoped

        // Now test Singleton behavior
        SubscriberMiddlewareModels.InstanceTrackingMiddleware.Reset();

        var singletonServiceCollection = new ServiceCollection()
            .AddAWSMessageBus(builder =>
            {
                builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("sqsQueueUrl");
                builder.AddHandlerMiddleware<SubscriberMiddlewareModels.InstanceTrackingMiddleware>(ServiceLifetime.Singleton);
            });

        var singletonServiceProvider = singletonServiceCollection.BuildServiceProvider();

        var singletonInvoker = new HandlerInvoker(
            singletonServiceProvider,
            new NullLogger<HandlerInvoker>(),
            new DefaultTelemetryFactory(singletonServiceProvider),
            singletonServiceProvider.GetRequiredService<IMessageConfiguration>());

        await singletonInvoker.InvokeAsync(envelope, subscriberMapping);
        await singletonInvoker.InvokeAsync(envelope, subscriberMapping);
        await singletonInvoker.InvokeAsync(envelope, subscriberMapping);

        // With singleton lifetime, all invocations should resolve the same instance
        Assert.Equal(3, SubscriberMiddlewareModels.InstanceTrackingMiddleware.ResolvedInstanceIds.Count);
        var singletonUniqueIds = new HashSet<int>(SubscriberMiddlewareModels.InstanceTrackingMiddleware.ResolvedInstanceIds);
        Assert.Single(singletonUniqueIds); // All same instance for singleton
    }
}

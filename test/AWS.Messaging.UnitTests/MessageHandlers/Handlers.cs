// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AWS.Messaging.UnitTests.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AWS.Messaging.UnitTests.MessageHandlers;

public class ChatMessageHandler : IMessageHandler<ChatMessage>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Success());
    }
}

public class AddressInfoHandler : IMessageHandler<AddressInfo>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<AddressInfo> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Success());
    }
}

public class ChatMessageHandlerWithDependencies : IMessageHandler<ChatMessage>
{
    private readonly IDependentThing _thingDoer;

    public ChatMessageHandlerWithDependencies(IDependentThing thingDoer)
    {
        _thingDoer = thingDoer;
    }

    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        _thingDoer.DoThingWithThing();
        return Task.FromResult(MessageProcessStatus.Success());
    }
}

public class ChatMessageHandlerWithDisposableServices : IMessageHandler<ChatMessage>
{
    public ChatMessageHandlerWithDisposableServices(TestDisposableServiceAsync testDisposableServiceAsync, TestDisposableService testDisposable)
    {
    }

    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Success());
    }

    public class TestDisposableServiceAsync : IAsyncDisposable
    {
        public static long CallCount { get; set; } = 0;
        public ValueTask DisposeAsync()
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    public class TestDisposableService : IDisposable
    {
        public static long CallCount { get; set; } = 0;
        public void Dispose()
        {
            CallCount++;
        }
    }
}

public class PlainTextHandler : IMessageHandler<string>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<string> messageEnvelope, CancellationToken token = default)
    {
        // Simple handler implementation for test purposes
        return Task.FromResult(MessageProcessStatus.Success());
    }
}


/// <summary>
/// Implements handling for mutiple message types
/// </summary>
public class DualHandler : IMessageHandler<ChatMessage>, IMessageHandler<AddressInfo>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Success());
    }

    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<AddressInfo> messageEnvelope, CancellationToken token = default)
    {
        return Task.FromResult(MessageProcessStatus.Failed());
    }
}

/// <summary>
/// Custom exception to throw from a handler
/// </summary>
public class CustomHandlerException : Exception
{
    public CustomHandlerException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>
/// Always throws a custom exception, useful for testing error handling
/// </summary>
public class ChatExceptionHandler : IMessageHandler<ChatMessage>
{
    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<ChatMessage> messageEnvelope, CancellationToken token = default)
    {
        throw new CustomHandlerException($"Unable to process message {messageEnvelope.Id}");
    }
}

public class GreetingHandler : IMessageHandler<string>
{
    private readonly IGreeter _greeter;
    private readonly TempStorage<string> _tempStorage;

    public GreetingHandler(IServiceProvider serviceProvider, TempStorage<string> tempStorage)
    {
        // Retrieve the IGreeter instance from the serviceProvider and verify that all instances are the same.
        var greeter1 = serviceProvider.GetRequiredService<IGreeter>();
        var greeter2 = serviceProvider.GetRequiredService<IGreeter>();
        var greeter3 = serviceProvider.GetRequiredService<IGreeter>();
        var messageStorage = new HashSet<string>() { greeter1.Greet(), greeter2.Greet(), greeter3.Greet() };
        Assert.Single(messageStorage);

        _greeter = serviceProvider.GetRequiredService<IGreeter>();
        _tempStorage = tempStorage;
    }

    public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<string> messageEnvelope, CancellationToken token = default)
    {
        _tempStorage.Messages.Add(new MessageEnvelope<string>
        {
            Message = _greeter.Greet()
        });
        return Task.FromResult(MessageProcessStatus.Success());
    }
}

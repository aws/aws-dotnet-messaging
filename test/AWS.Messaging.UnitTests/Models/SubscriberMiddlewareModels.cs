// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Messaging.UnitTests.Models;

public static class SubscriberMiddlewareModels
{
    public class MiddlewareTracker
    {
        private readonly List<Type> _executed = [];

        public IReadOnlyList<Type> Executed => _executed.AsReadOnly();

        public void Add(object middleware)
        {
            _executed.Add(middleware.GetType());
        }
    }

    public class SuccessMessageHandler<T> : IMessageHandler<T>
    {
        private readonly MiddlewareTracker _tracker;

        public SuccessMessageHandler(MiddlewareTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<T> messageEnvelope, CancellationToken token = default)
        {
            _tracker.Add(this);
            return Task.FromResult(MessageProcessStatus.Success());
        }
    }

    public class FailMessageHandler<T> : IMessageHandler<T>
    {
        private readonly MiddlewareTracker _tracker;

        public FailMessageHandler(MiddlewareTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<MessageProcessStatus> HandleAsync(MessageEnvelope<T> messageEnvelope, CancellationToken token = default)
        {
            _tracker.Add(this);
            return Task.FromResult(MessageProcessStatus.Failed());
        }
    }

    public abstract class TrackedMiddleware : IHandlerMiddleware
    {
        private readonly MiddlewareTracker _tracker;

        protected TrackedMiddleware(MiddlewareTracker tracker)
        {
            _tracker = tracker;
        }

        public virtual Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken cancellationToken = default)
        {
            _tracker.Add(this);
            return next();
        }
    }

    public class A : TrackedMiddleware
    {
        public A(MiddlewareTracker tracker) : base(tracker) { }
    }

    public class B : TrackedMiddleware
    {
        public B(MiddlewareTracker tracker) : base(tracker) { }
    }

    public class C : TrackedMiddleware
    {
        public C(MiddlewareTracker tracker) : base(tracker) { }
    }

    public class Error : IHandlerMiddleware
    {
        private readonly MiddlewareTracker _tracker;

        public Error(MiddlewareTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken cancellationToken = default)
        {
            _tracker.Add(this);
            throw new Exception("Error in middleware");
        }
    }

    /// <summary>
    /// Middleware that short-circuits the pipeline by returning a result without calling next().
    /// </summary>
    public class ShortCircuit : IHandlerMiddleware
    {
        private readonly MiddlewareTracker _tracker;

        public ShortCircuit(MiddlewareTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken cancellationToken = default)
        {
            _tracker.Add(this);
            // Intentionally NOT calling next() - short-circuiting the pipeline
            return Task.FromResult(MessageProcessStatus.Success());
        }
    }

    /// <summary>
    /// Middleware that tracks its instance ID to verify scoped vs singleton behavior.
    /// </summary>
    public class InstanceTrackingMiddleware : IHandlerMiddleware
    {
        private static int _instanceCounter;
        public int InstanceId { get; }

        public static List<int> ResolvedInstanceIds { get; } = new();

        public InstanceTrackingMiddleware()
        {
            InstanceId = Interlocked.Increment(ref _instanceCounter);
        }

        public static void Reset()
        {
            _instanceCounter = 0;
            ResolvedInstanceIds.Clear();
        }

        public Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken cancellationToken = default)
        {
            ResolvedInstanceIds.Add(InstanceId);
            return next();
        }
    }

    /// <summary>
    /// Middleware that verifies the cancellation token is received.
    /// </summary>
    public class CancellationAwareMiddleware : IHandlerMiddleware
    {
        public static bool TokenWasCancelled { get; set; }
        public static bool TokenWasReceived { get; set; }

        public static void Reset()
        {
            TokenWasCancelled = false;
            TokenWasReceived = false;
        }

        public Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken cancellationToken = default)
        {
            TokenWasReceived = cancellationToken != default;
            TokenWasCancelled = cancellationToken.IsCancellationRequested;
            return next();
        }
    }
}

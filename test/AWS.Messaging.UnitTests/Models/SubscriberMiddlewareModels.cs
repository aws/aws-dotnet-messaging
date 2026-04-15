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

    public abstract class TrackedMiddleware : IMiddleware
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

    public class Error : IMiddleware
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
}

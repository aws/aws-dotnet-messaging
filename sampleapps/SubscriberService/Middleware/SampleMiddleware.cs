// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging;

namespace SubscriberService.Middleware;

public class SampleMiddleware : IMiddleware
{
    public Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, RequestDelegate next, CancellationToken token = default)
    {
        // This middleware does not do anything, but exists to demonstrate how to implement a middleware

        return next();
    }
}

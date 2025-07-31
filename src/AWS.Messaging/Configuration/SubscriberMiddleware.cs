// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Tracks the <see cref="IMiddleware"/> to be processed by the <see cref="Services.IHandlerInvoker"/> implementation and its <see cref="ServiceLifetime"/>.
/// </summary>
public class SubscriberMiddleware
{
    /// <summary>
    /// Constructs an instance of <see cref="SubscriberMiddleware"/>
    /// </summary>
    /// <param name="type">The type that implements <see cref="IMiddleware"/>.</param>
    /// <param name="serviceLifetime">The lifetime of the middleware.</param>
    internal SubscriberMiddleware([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, ServiceLifetime serviceLifetime)
    {
        Type = type;
        ServiceLifetime = serviceLifetime;
    }

    /// <summary>
    /// Type that implements <see cref="IMiddleware"/>.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Type { get; }

    /// <summary>
    /// Service lifetime of the middleware.
    /// </summary>
    public ServiceLifetime ServiceLifetime { get; }

    /// <summary>
    /// Creates a SubscriberMiddleware from the generic parameters for the middleware.
    /// </summary>
    /// <typeparam name="TMiddleware">The type that implements <see cref="IMiddleware"/></typeparam>
    /// <param name="serviceLifetime">The lifetime of the middleware.</param>
    /// <returns><see cref="SubscriberMapping"/></returns>
    public static SubscriberMiddleware Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMiddleware>(ServiceLifetime serviceLifetime = ServiceLifetime.Singleton)
        where TMiddleware : class, IMiddleware
    {
        return new SubscriberMiddleware(typeof(TMiddleware), serviceLifetime);
    }
}

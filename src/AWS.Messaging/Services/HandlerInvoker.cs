// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AWS.Messaging.Configuration;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Services;

/// <inheritdoc cref="IHandlerInvoker"/>
public class HandlerInvoker : IHandlerInvoker
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HandlerInvoker> _logger;
    private readonly ITelemetryFactory _telemetryFactory;
    private readonly IMessageConfiguration _messageConfiguration;

    /// <summary>
    /// Constructs an instance of HandlerInvoker
    /// </summary>
    /// <param name="serviceProvider">Service provider used to resolve handler objects</param>
    /// <param name="logger">Logger for debugging information</param>
    /// <param name="telemetryFactory">Factory for telemetry data</param>
    /// <param name="messageConfiguration">Messaging configuration holding middleware configuration</param>
    public HandlerInvoker(
        IServiceProvider serviceProvider,
        ILogger<HandlerInvoker> logger,
        ITelemetryFactory telemetryFactory,
        IMessageConfiguration messageConfiguration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _telemetryFactory = telemetryFactory;
        _messageConfiguration = messageConfiguration;
    }

    public Task<MessageProcessStatus> InvokeAsync(MessageEnvelope messageEnvelope, SubscriberMapping subscriberMapping, CancellationToken token = default)
    {
        // redirect to the generic version of InvokeAsync
        return subscriberMapping.HandlerInvoker(this, messageEnvelope, subscriberMapping, token);
    }

    /// <inheritdoc/>
    public async Task<MessageProcessStatus> InvokeAsync<T>(MessageEnvelope<T> messageEnvelope, SubscriberMapping subscriberMapping, CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Processing message", messageEnvelope))
        {
            try
            {
                trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelope.Id);
                trace.AddMetadata(TelemetryKeys.MessageType, messageEnvelope.MessageTypeIdentifier);
                trace.AddMetadata(TelemetryKeys.HandlerType, subscriberMapping.HandlerType.FullName!);
                if (!string.IsNullOrEmpty(messageEnvelope.SQSMetadata?.MessageID))
                {
                    trace.AddMetadata(TelemetryKeys.SqsMessageId, messageEnvelope.SQSMetadata.MessageID);
                }

                var attempt = 0;
                while (true)
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    try
                    {

                        IMessageHandler<T> handler;
                        try
                        {
                            handler = (IMessageHandler<T>)scope.ServiceProvider.GetRequiredService(subscriberMapping.HandlerType);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unable to resolve a handler for {HandlerType} while handling message ID {MessageEnvelopeId}.", subscriberMapping.HandlerType, messageEnvelope.Id);
                            throw new InvalidMessageHandlerSignatureException($"Unable to resolve a handler for {subscriberMapping.HandlerType} while handling message ID {messageEnvelope.Id}.", ex);
                        }

                        var middlewares = _messageConfiguration.SubscriberMiddleware.Select(type => (IMiddleware)scope.ServiceProvider.GetRequiredService(type.Type)!).ToList();
                        return await ExecutePipelineAsync(messageEnvelope, middlewares, handler, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not InvalidMessageHandlerSignatureException)
                    {
                        if (ex is TargetInvocationException targetInvocationException)
                        {
                            // Since we are invoking HandleAsync via reflection, we need to unwrap the TargetInvocationException
                            // containing application exceptions that happened inside the IMessageHandler
                            if (targetInvocationException.InnerException != null)
                            {
                                ex = targetInvocationException.InnerException;
                            }
                        }
                        else
                        {
                            trace.AddException(ex, false);
                        }

                        _logger.LogError(ex, "An unexpected exception occurred while handling message ID {MessageId}.", messageEnvelope.Id);

                        try
                        {
                            var retryHandler = scope.ServiceProvider.GetService<IMessageErrorHandler>();
                            if (retryHandler != null)
                            {
                                switch (await retryHandler!.OnHandleError(messageEnvelope, ex, ++attempt, token))
                                {
                                    case MessageErrorHandlerResponse.Failed:
                                        _logger.LogError(ex, "An unexpected exception occurred while determining if message ID {MessageId} should be retried.", messageEnvelope.Id);
                                        return MessageProcessStatus.Failed();

                                    case MessageErrorHandlerResponse.Success:
                                        return MessageProcessStatus.Success();

                                    case MessageErrorHandlerResponse.Retry:
                                        trace.AddMetadata(TelemetryKeys.Retry, attempt);
                                        continue;

                                    default:
                                        throw new NotImplementedException();
                                }
                            }
                        }
                        catch (Exception retryException)
                        {
                            _logger.LogError(retryException, "An unexpected exception occurred while determining if message ID {MessageId} should be retried.", messageEnvelope.Id);
                            return MessageProcessStatus.Failed();
                        }

                        return MessageProcessStatus.Failed();
                    }
                }
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }

    private static async Task<MessageProcessStatus> ExecutePipelineAsync<T>(MessageEnvelope<T> messageEnvelope, List<IMiddleware> middlewares, IMessageHandler<T> handler, CancellationToken token)
    {
        RequestDelegate next = () => handler.HandleAsync(messageEnvelope, token);

        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var capturedNext = next;
            var middleware = middlewares[i];
            next = () => middleware.InvokeAsync(messageEnvelope, capturedNext, token);
        }

        return await next().ConfigureAwait(false);
    }
}

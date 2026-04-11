// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

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

    /// <summary>
    /// Constructs an instance of HandlerInvoker
    /// </summary>
    /// <param name="serviceProvider">Service provider used to resolve handler objects</param>
    /// <param name="logger">Logger for debugging information</param>
    /// <param name="telemetryFactory">Factory for telemetry data</param>
    public HandlerInvoker(
        IServiceProvider serviceProvider,
        ILogger<HandlerInvoker> logger,
        ITelemetryFactory telemetryFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _telemetryFactory = telemetryFactory;
    }

    /// <inheritdoc/>
    public async Task<MessageProcessStatus> InvokeAsync(MessageEnvelope messageEnvelope, SubscriberMapping subscriberMapping, CancellationToken token = default)
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

                await using (var scope = _serviceProvider.CreateAsyncScope())
                {
                    object handler;
                    try
                    {
                        handler = scope.ServiceProvider.GetRequiredService(subscriberMapping.HandlerType);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Unable to resolve a handler for {HandlerType} while handling message ID {MessageEnvelopeId}.", subscriberMapping.HandlerType, messageEnvelope.Id);
                        throw new InvalidMessageHandlerSignatureException($"Unable to resolve a handler for {subscriberMapping.HandlerType} " +
                                                                          $"while handling message ID {messageEnvelope.Id}.", e);
                    }

                    try
                    {
                        return await subscriberMapping.HandlerInvokerFunc(handler, messageEnvelope, token);
                    }
                    catch (Exception ex)
                    {
                        trace.AddException(ex, false);

                        _logger.LogError(ex, "A handler exception occurred while handling message ID {MessageId}.", messageEnvelope.Id);
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
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Publishers.SQS;

/// <summary>
/// The SQS message publisher allows sending messages to AWS SQS.
/// </summary>
internal class SQSPublisher : ISQSPublisher
{
    private readonly IAWSClientProvider _awsClientProvider;
    private readonly ILogger<ISQSPublisher> _logger;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly IEnvelopeSerializer _envelopeSerializer;
    private readonly ITelemetryFactory _telemetryFactory;
    private IAmazonSQS? _sqsClient;

    private const string FIFO_SUFFIX = ".fifo";
    private const int MAX_BATCH_SIZE = 10;

    /// <summary>
    /// Creates an instance of <see cref="SQSPublisher"/>.
    /// </summary>
    public SQSPublisher(
        IAWSClientProvider awsClientProvider,
        ILogger<ISQSPublisher> logger,
        IMessageConfiguration messageConfiguration,
        IEnvelopeSerializer envelopeSerializer,
        ITelemetryFactory telemetryFactory)
    {
        _awsClientProvider = awsClientProvider;
        _logger = logger;
        _messageConfiguration = messageConfiguration;
        _envelopeSerializer = envelopeSerializer;
        _telemetryFactory = telemetryFactory;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="message">The application message that will be serialized and sent to an SQS queue</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    /// <exception cref="FailedToPublishException">If the message failed to publish.</exception>
    /// <exception cref="InvalidMessageException">If the message is null or invalid.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<IPublishResponse> SendAsync<T>(T message, CancellationToken token = default)
    {
        return await SendAsync(message, null, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="message">The application message that will be serialized and sent to an SQS queue</param>
    /// <param name="sqsOptions">Contains additional parameters that can be set while sending a message to an SQS queue</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    /// <exception cref="FailedToPublishException">If the message failed to publish.</exception>
    /// <exception cref="InvalidMessageException">If the message is null or invalid.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<SQSSendResponse> SendAsync<T>(T message, SQSOptions? sqsOptions, CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Publish to AWS SQS"))
        {
            try
            {
                trace.AddMetadata(TelemetryKeys.ObjectType, typeof(T).FullName!);

                _logger.LogDebug("Publishing the message of type '{MessageType}' using the {PublisherType}.", typeof(T), nameof(SQSPublisher));

                if (message == null)
                {
                    _logger.LogError("A message of type '{MessageType}' has a null value.", typeof(T));
                    throw new InvalidMessageException("The message cannot be null.");
                }

                var queueUrl = GetPublisherEndpoint(trace, typeof(T), sqsOptions);

                _logger.LogDebug("Creating the message envelope for the message of type '{MessageType}'.", typeof(T));
                var messageEnvelope = await _envelopeSerializer.CreateEnvelopeAsync(message);

                trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelope.Id);
                trace.RecordTelemetryContext(messageEnvelope);

                var messageBody = await _envelopeSerializer.SerializeAsync(messageEnvelope);

                var client = ResolveClient(sqsOptions);

                _logger.LogDebug("Sending the message of type '{MessageType}' to SQS. Publisher Endpoint: {Endpoint}", typeof(T), queueUrl);
                var sendMessageRequest = CreateSendMessageRequest(queueUrl, messageBody, sqsOptions);
                var response = await client.SendMessageAsync(sendMessageRequest, token);
                _logger.LogDebug("The message of type '{MessageType}' has been pushed to SQS.", typeof(T));
                return new SQSSendResponse
                {
                    MessageId = response.MessageId
                };
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                if (ex is AmazonSQSException)
                    throw new FailedToPublishException("Message failed to publish.", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <exception cref="FailedToPublishException">If the batch request failed due to an SQS SDK exception.</exception>
    /// <exception cref="InvalidMessageException">If any message in the batch is null.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<T> messages, SQSOptions? sqsOptions = null, CancellationToken token = default)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages), "The messages collection cannot be null.");
        }

        var entries = messages.Select(m => new SQSBatchEntry<T>(m, null));
        return await SendBatchAsync(entries, sqsOptions, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <exception cref="FailedToPublishException">If the batch request failed due to an SQS SDK exception.</exception>
    /// <exception cref="InvalidMessageException">If any message in the batch is null.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If cannot find the publisher configuration for the message type.</exception>
    public async Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<SQSBatchEntry<T>> entries, SQSOptions? sqsOptions = null, CancellationToken token = default)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries), "The entries collection cannot be null.");
        }

        using (var trace = _telemetryFactory.Trace("Publish batch to AWS SQS"))
        {
            // Declared outside try so partial results can be captured in the catch block
            var aggregatedResponse = new SQSSendBatchResponse();

            try
            {
                trace.AddMetadata(TelemetryKeys.ObjectType, typeof(T).FullName!);

                _logger.LogDebug("Publishing a batch of messages of type '{MessageType}' using the {PublisherType}.", typeof(T), nameof(SQSPublisher));

                var queueUrl = GetPublisherEndpoint(trace, typeof(T), sqsOptions);
                var client = ResolveClient(sqsOptions);

                // Serialize all messages and build batch request entries
                var batchRequestEntries = new List<SendMessageBatchRequestEntry>();
                foreach (var entry in entries)
                {
                    if (entry.Message == null)
                    {
                        _logger.LogError("A message of type '{MessageType}' in the batch has a null value.", typeof(T));
                        throw new InvalidMessageException("A message in the batch cannot be null.");
                    }

                    var messageEnvelope = await _envelopeSerializer.CreateEnvelopeAsync(entry.Message);

                    // Record telemetry context for each envelope to support downstream trace correlation
                    trace.RecordTelemetryContext(messageEnvelope);

                    var messageBody = await _envelopeSerializer.SerializeAsync(messageEnvelope);

                    // Use the MessageEnvelope.Id as the batch entry Id so callers can correlate
                    // successes/failures in the response back to their original input messages
                    var batchEntry = CreateSendMessageBatchRequestEntry(messageEnvelope.Id, queueUrl, messageBody, sqsOptions, entry.Options);
                    batchRequestEntries.Add(batchEntry);
                }

                if (batchRequestEntries.Count == 0)
                {
                    _logger.LogDebug("No messages to send in the batch of type '{MessageType}'.", typeof(T));
                    return new SQSSendBatchResponse();
                }

                _logger.LogDebug("Sending a batch of {MessageCount} message(s) of type '{MessageType}' to SQS. Publisher Endpoint: {Endpoint}",
                    batchRequestEntries.Count, typeof(T), queueUrl);

                // Chunk into groups of MAX_BATCH_SIZE (10) and send each chunk.
                // Note: If an AmazonSQSException occurs on a later chunk, earlier chunks may have already
                // succeeded. The partial results are surfaced on the thrown FailedToPublishBatchException.
                var chunks = Chunk(batchRequestEntries, MAX_BATCH_SIZE);

                foreach (var chunk in chunks)
                {
                    var request = new SendMessageBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = chunk
                    };

                    var response = await client.SendMessageBatchAsync(request, token);

                    // Aggregate successful entries
                    if (response.Successful != null)
                    {
                        foreach (var success in response.Successful)
                        {
                            aggregatedResponse.Successful.Add(new SQSSendBatchResponseEntry
                            {
                                Id = success.Id,
                                MessageId = success.MessageId
                            });
                        }
                    }

                    // Aggregate failed entries
                    if (response.Failed != null)
                    {
                        foreach (var failure in response.Failed)
                        {
                            _logger.LogError(
                                "Failed to send batch entry with Id '{BatchEntryId}' to SQS. Error Code: {ErrorCode}, Error Message: {ErrorMessage}",
                                failure.Id, failure.Code, failure.Message);

                            aggregatedResponse.Failed.Add(new SQSSendBatchResponseFailedEntry
                            {
                                Id = failure.Id,
                                Code = failure.Code,
                                Message = failure.Message,
                                SenderFault = failure.SenderFault ?? false
                            });
                        }
                    }
                }

                _logger.LogDebug(
                    "Batch send of type '{MessageType}' completed. {SuccessCount} message(s) succeeded, {FailCount} message(s) failed.",
                    typeof(T), aggregatedResponse.Successful.Count, aggregatedResponse.Failed.Count);

                return aggregatedResponse;
            }
            catch (Exception ex) when (ex is AmazonSQSException)
            {
                trace.AddException(ex);
                // Surface partial results from chunks that may have succeeded before this failure
                throw new FailedToPublishBatchException(
                    "Message batch failed to publish. Some messages in earlier chunks may have been sent successfully. " +
                    "Check the PartialResponse property for details.",
                    ex, aggregatedResponse);
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }

    private IAmazonSQS ResolveClient(SQSOptions? sqsOptions)
    {
        if (sqsOptions?.OverrideClient != null)
        {
            return sqsOptions.OverrideClient;
        }

        _sqsClient ??= _awsClientProvider.GetServiceClient<IAmazonSQS>();
        return _sqsClient;
    }

    private SendMessageBatchRequestEntry CreateSendMessageBatchRequestEntry(
        string entryId,
        string queueUrl,
        string messageBody,
        SQSOptions? sharedOptions,
        SQSOptions? entryOptions)
    {
        // Resolve effective per-message options: entry-level overrides shared-level
        var effectiveMessageGroupId = entryOptions?.MessageGroupId ?? sharedOptions?.MessageGroupId;
        var effectiveMessageDeduplicationId = entryOptions?.MessageDeduplicationId ?? sharedOptions?.MessageDeduplicationId;
        var effectiveDelaySeconds = entryOptions?.DelaySeconds ?? sharedOptions?.DelaySeconds;
        var effectiveMessageAttributes = entryOptions?.MessageAttributes ?? sharedOptions?.MessageAttributes;

        if (queueUrl.EndsWith(FIFO_SUFFIX) && string.IsNullOrEmpty(effectiveMessageGroupId))
        {
            var errorMessage =
                $"You are attempting to send to a FIFO SQS queue but the request does not include a message group ID. " +
                $"Please specify a message group ID via {nameof(SQSOptions.MessageGroupId)} on either the shared " +
                $"{nameof(SQSOptions)} parameter or the per-entry {nameof(SQSBatchEntry<object>.Options)}. " +
                $"Additionally, {nameof(SQSOptions.MessageDeduplicationId)} must also be specified if content based de-duplication is not enabled on the queue.";

            _logger.LogError(errorMessage);
            throw new InvalidFifoPublishingRequestException(errorMessage);
        }

        var entry = new SendMessageBatchRequestEntry
        {
            Id = entryId,
            MessageBody = messageBody
        };

        if (!string.IsNullOrEmpty(effectiveMessageGroupId))
            entry.MessageGroupId = effectiveMessageGroupId;

        if (!string.IsNullOrEmpty(effectiveMessageDeduplicationId))
            entry.MessageDeduplicationId = effectiveMessageDeduplicationId;

        if (effectiveDelaySeconds.HasValue)
            entry.DelaySeconds = (int)effectiveDelaySeconds;

        if (effectiveMessageAttributes is not null)
            entry.MessageAttributes = effectiveMessageAttributes;

        return entry;
    }

    private static List<List<T>> Chunk<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }
        return chunks;
    }

    private SendMessageRequest CreateSendMessageRequest(string queueUrl, string messageBody, SQSOptions? sqsOptions)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody,
        };

        if (queueUrl.EndsWith(FIFO_SUFFIX) && string.IsNullOrEmpty(sqsOptions?.MessageGroupId))
        {
            var errorMessage =
                $"You are attempting to send to a FIFO SQS queue but the request does not include a message group ID. " +
                $"Please use {nameof(ISQSPublisher)} from the service collection to send to FIFO queues. " +
                $"It exposes a {nameof(SendAsync)} method that accepts {nameof(SQSOptions)} as a parameter. " +
                $"A message group ID must be specified via {nameof(SQSOptions.MessageGroupId)}. " +
                $"Additionally, {nameof(SQSOptions.MessageDeduplicationId)} must also be specified if content based de-duplication is not enabled on the queue.";

            _logger.LogError(errorMessage);
            throw new InvalidFifoPublishingRequestException(errorMessage);
        }

        if (sqsOptions is null)
            return request;

        if (!string.IsNullOrEmpty(sqsOptions.MessageDeduplicationId))
            request.MessageDeduplicationId = sqsOptions.MessageDeduplicationId;

        if (!string.IsNullOrEmpty(sqsOptions.MessageGroupId))
            request.MessageGroupId = sqsOptions.MessageGroupId;

        if (sqsOptions.DelaySeconds.HasValue)
            request.DelaySeconds = (int)sqsOptions.DelaySeconds;

        if (sqsOptions.MessageAttributes is not null)
            request.MessageAttributes = sqsOptions.MessageAttributes;

        return request;
    }

    private string GetPublisherEndpoint(ITelemetryTrace trace, Type messageType, SQSOptions? sqsOptions)
    {
        var mapping = _messageConfiguration.GetPublisherMapping(messageType);
        if (mapping is null)
        {
            _logger.LogError("Cannot find a configuration for the message of type '{MessageType}'.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"The framework is not configured to accept messages of type '{messageType.FullName}'.");
        }

        if (mapping.PublishTargetType != PublisherTargetType.SQS_PUBLISHER)
        {
            _logger.LogError("Messages of type '{MessageType}' are not configured for publishing to SQS.", messageType.FullName);
            throw new MissingMessageTypeConfigurationException($"Messages of type '{messageType.FullName}' are not configured for publishing to SQS.");
        }

        var queueUrl = mapping.PublisherConfiguration.PublisherEndpoint;

        // Check if the queue was overriden on this message-specific publishing options
        if (!string.IsNullOrEmpty(sqsOptions?.QueueUrl))
        {
            queueUrl = sqsOptions.QueueUrl;
        }

        if (string.IsNullOrEmpty(queueUrl))
        {
            _logger.LogError("Unable to determine a destination queue for message of type '{MessageType}'.", messageType.FullName);
            throw new InvalidPublisherEndpointException($"Unable to determine a destination queue for message of type '{messageType.FullName}'.");
        }

        trace.AddMetadata(TelemetryKeys.MessageType, mapping.MessageTypeIdentifier);
        trace.AddMetadata(TelemetryKeys.QueueUrl, queueUrl);

        return queueUrl;
    }
}

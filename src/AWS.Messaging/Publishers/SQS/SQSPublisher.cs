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

                var queueUrl = GetPublisherEndpoint(trace, typeof(T), sqsOptions?.QueueUrl);

                _logger.LogDebug("Creating the message envelope for the message of type '{MessageType}'.", typeof(T));
                var messageEnvelope = await _envelopeSerializer.CreateEnvelopeAsync(message);

                trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelope.Id);
                trace.RecordTelemetryContext(messageEnvelope);

                var messageBody = await _envelopeSerializer.SerializeAsync(messageEnvelope);

                var client = ResolveClient(sqsOptions?.OverrideClient);

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
    /// <exception cref="FailedToPublishBatchException">If a SendMessageBatch request threw, for example because of an SQS error,
    /// a network error or a timeout. The original exception is the <see cref="Exception.InnerException"/>, and no later
    /// request is sent. <see cref="FailedToPublishBatchException.PartialResponse"/> accounts for every message: those of the
    /// earlier requests are in <see cref="SQSSendBatchResponse.Successful"/> or <see cref="SQSSendBatchResponse.Failed"/> (or in
    /// <see cref="SQSSendBatchResponse.Unprocessed"/> as <see cref="SQSSendBatchUnprocessedReason.NotReported"/> if SQS left
    /// them out of its response), those of the request that threw are in <see cref="SQSSendBatchResponse.Unprocessed"/> as
    /// <see cref="SQSSendBatchUnprocessedReason.OutcomeUnknown"/>, and those of the later requests as
    /// <see cref="SQSSendBatchUnprocessedReason.NotSent"/>.</exception>
    /// <exception cref="OperationCanceledException">If <paramref name="token"/> was cancelled. No partial results are available.
    /// Messages in earlier requests, and possibly those in the request that was in flight when <paramref name="token"/> was
    /// cancelled, may already have been delivered.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="messages"/> is null.</exception>
    /// <exception cref="InvalidMessageException">If any message is null. Nothing is sent in this case.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If <typeparamref name="T"/> is not configured to be published to SQS.
    /// Nothing is sent in this case.</exception>
    /// <exception cref="InvalidPublisherEndpointException">If no queue URL is configured for <typeparamref name="T"/>.
    /// Nothing is sent in this case.</exception>
    /// <exception cref="InvalidFifoPublishingRequestException">If the queue is a FIFO queue. Use the other overload to set a
    /// <see cref="SQSMessageOptions.MessageGroupId"/> on every entry. Nothing is sent in this case.</exception>
    /// <exception cref="FailedToSerializeMessageEnvelopeException">If a message cannot be serialized.
    /// Nothing is sent in this case.</exception>
    public async Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<T> messages, CancellationToken token = default)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages), "The messages collection cannot be null.");
        }

        var entries = messages.Select(m => new SQSBatchEntry<T>(m, null));
        return await SendBatchAsync(entries, null, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <exception cref="FailedToPublishBatchException">If a SendMessageBatch request threw, for example because of an SQS error,
    /// a network error or a timeout. The original exception is the <see cref="Exception.InnerException"/>, and no later
    /// request is sent. <see cref="FailedToPublishBatchException.PartialResponse"/> accounts for every entry: those of the
    /// earlier requests are in <see cref="SQSSendBatchResponse.Successful"/> or <see cref="SQSSendBatchResponse.Failed"/> (or in
    /// <see cref="SQSSendBatchResponse.Unprocessed"/> as <see cref="SQSSendBatchUnprocessedReason.NotReported"/> if SQS left
    /// them out of its response), those of the request that threw are in <see cref="SQSSendBatchResponse.Unprocessed"/> as
    /// <see cref="SQSSendBatchUnprocessedReason.OutcomeUnknown"/>, and those of the later requests as
    /// <see cref="SQSSendBatchUnprocessedReason.NotSent"/>.</exception>
    /// <exception cref="OperationCanceledException">If <paramref name="token"/> was cancelled. No partial results are available.
    /// Messages in earlier requests, and possibly those in the request that was in flight when <paramref name="token"/> was
    /// cancelled, may already have been delivered.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="entries"/> is null.</exception>
    /// <exception cref="InvalidMessageException">If the message of any entry is null. Nothing is sent in this case.</exception>
    /// <exception cref="MissingMessageTypeConfigurationException">If <typeparamref name="T"/> is not configured to be published to SQS.
    /// Nothing is sent in this case.</exception>
    /// <exception cref="InvalidPublisherEndpointException">If <paramref name="batchOptions"/> sets no queue URL and none is configured
    /// for <typeparamref name="T"/>. Nothing is sent in this case.</exception>
    /// <exception cref="InvalidFifoPublishingRequestException">If the queue is a FIFO queue and an entry has no
    /// <see cref="SQSMessageOptions.MessageGroupId"/>. Nothing is sent in this case.</exception>
    /// <exception cref="FailedToSerializeMessageEnvelopeException">If a message cannot be serialized.
    /// Nothing is sent in this case.</exception>
    public async Task<SQSSendBatchResponse> SendBatchAsync<T>(IEnumerable<SQSBatchEntry<T>> entries, SQSBatchOptions? batchOptions = null, CancellationToken token = default)
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

                var queueUrl = GetPublisherEndpoint(trace, typeof(T), batchOptions?.QueueUrl);
                var client = ResolveClient(batchOptions?.OverrideClient);

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

                    // Use the caller-supplied Id, or the MessageEnvelope.Id when none was given, as the batch entry Id
                    // so callers can correlate successes/failures in the response back to their original input messages
                    var batchEntryId = entry.Id ?? messageEnvelope.Id;
                    var batchEntry = CreateSendMessageBatchRequestEntry(batchEntryId, queueUrl, messageBody, entry.Options);
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
                // Note: If sending a chunk fails, earlier chunks may have already succeeded. The partial results, including
                // the entries of the failing chunk and of the chunks never sent, are surfaced on the thrown FailedToPublishBatchException.
                var chunks = Chunk(batchRequestEntries, MAX_BATCH_SIZE);

                for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var request = new SendMessageBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = chunk
                    };

                    SendMessageBatchResponse response;
                    try
                    {
                        response = await client.SendMessageBatchAsync(request, token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        // Any failure (SQS error, network error, timeout) is wrapped so the partial results are not lost.
                        // Cancellation requested by the caller propagates unchanged, following .NET convention.
                        trace.AddException(ex);

                        // The entries of this chunk may or may not have reached SQS, and later chunks were never sent
                        var unsentEntries = chunks.Skip(chunkIndex + 1).SelectMany(c => c);
                        aggregatedResponse.Unprocessed.AddRange(
                            CreateUnprocessedEntries(chunk, SQSSendBatchUnprocessedReason.OutcomeUnknown));
                        aggregatedResponse.Unprocessed.AddRange(
                            CreateUnprocessedEntries(unsentEntries, SQSSendBatchUnprocessedReason.NotSent));

                        throw new FailedToPublishBatchException(
                            "Message batch failed to publish. Some messages in earlier chunks may have been sent successfully. " +
                            "Check the PartialResponse property for details.",
                            ex, aggregatedResponse);
                    }

                    // Count the entries sent with each Id, so that each entry is matched by at most one result.
                    // A result whose Id was not sent in this chunk, or whose entries all have a result already, is ignored,
                    // so it cannot be mistaken for another entry or counted twice. An entry left without a result has an
                    // unknown outcome.
                    var unreportedIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var requestEntry in chunk)
                    {
                        if (requestEntry.Id != null)
                        {
                            unreportedIdCounts.TryGetValue(requestEntry.Id, out var count);
                            unreportedIdCounts[requestEntry.Id] = count + 1;
                        }
                    }

                    bool TryClaim(string? id)
                    {
                        if (id != null && unreportedIdCounts.TryGetValue(id, out var count) && count > 0)
                        {
                            unreportedIdCounts[id] = count - 1;
                            return true;
                        }

                        _logger.LogWarning(
                            "SQS reported a result for batch entry with Id '{BatchEntryId}', which was not in the request " +
                            "or was already reported. The result is ignored.",
                            id);
                        return false;
                    }

                    // Aggregate successful entries
                    if (response.Successful != null)
                    {
                        foreach (var success in response.Successful)
                        {
                            if (!TryClaim(success.Id))
                                continue;

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
                            if (!TryClaim(failure.Id))
                                continue;

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

                    // SQS should report every entry as successful or failed. An entry it did not report has an unknown outcome.
                    // Entries that share an Id cannot be told apart, so only as many of them as have no result are reported here.
                    foreach (var requestEntry in chunk)
                    {
                        if (requestEntry.Id != null)
                        {
                            var unreportedCount = unreportedIdCounts[requestEntry.Id];
                            if (unreportedCount == 0)
                                continue;

                            unreportedIdCounts[requestEntry.Id] = unreportedCount - 1;
                        }

                        _logger.LogWarning(
                            "SQS did not report a result for batch entry with Id '{BatchEntryId}'. Its outcome is unknown.",
                            requestEntry.Id);
                        aggregatedResponse.Unprocessed.Add(new SQSSendBatchResponseUnprocessedEntry
                        {
                            Id = requestEntry.Id,
                            Reason = SQSSendBatchUnprocessedReason.NotReported
                        });
                    }
                }

                _logger.LogDebug(
                    "Batch send of type '{MessageType}' completed. {SuccessCount} message(s) succeeded, " +
                    "{FailCount} message(s) failed, {UnprocessedCount} message(s) unprocessed.",
                    typeof(T), aggregatedResponse.Successful.Count, aggregatedResponse.Failed.Count, aggregatedResponse.Unprocessed.Count);

                return aggregatedResponse;
            }
            catch (Exception ex) when (ex is not FailedToPublishBatchException)
            {
                // A FailedToPublishBatchException has already recorded the exception that caused it on the trace
                trace.AddException(ex);
                throw;
            }
        }
    }

    private IAmazonSQS ResolveClient(IAmazonSQS? overrideClient)
    {
        if (overrideClient != null)
        {
            return overrideClient;
        }

        _sqsClient ??= _awsClientProvider.GetServiceClient<IAmazonSQS>();
        return _sqsClient;
    }

    private SendMessageBatchRequestEntry CreateSendMessageBatchRequestEntry(
        string entryId,
        string queueUrl,
        string messageBody,
        SQSMessageOptions? entryOptions)
    {
        if (queueUrl.EndsWith(FIFO_SUFFIX) && string.IsNullOrEmpty(entryOptions?.MessageGroupId))
        {
            var errorMessage =
                $"You are attempting to send to a FIFO SQS queue but the request does not include a message group ID. " +
                $"Please specify a message group ID via {nameof(SQSMessageOptions.MessageGroupId)} on the " +
                $"{nameof(SQSBatchEntry<object>.Options)} for each entry. " +
                $"Additionally, {nameof(SQSMessageOptions.MessageDeduplicationId)} must also be specified if content based de-duplication is not enabled on the queue.";

            _logger.LogError(errorMessage);
            throw new InvalidFifoPublishingRequestException(errorMessage);
        }

        var entry = new SendMessageBatchRequestEntry
        {
            Id = entryId,
            MessageBody = messageBody
        };

        if (entryOptions is null)
            return entry;

        if (!string.IsNullOrEmpty(entryOptions.MessageGroupId))
            entry.MessageGroupId = entryOptions.MessageGroupId;

        if (!string.IsNullOrEmpty(entryOptions.MessageDeduplicationId))
            entry.MessageDeduplicationId = entryOptions.MessageDeduplicationId;

        if (entryOptions.DelaySeconds.HasValue)
            entry.DelaySeconds = (int)entryOptions.DelaySeconds;

        if (entryOptions.MessageAttributes is not null)
            entry.MessageAttributes = entryOptions.MessageAttributes;

        return entry;
    }

    private static IEnumerable<SQSSendBatchResponseUnprocessedEntry> CreateUnprocessedEntries(
        IEnumerable<SendMessageBatchRequestEntry> requestEntries,
        SQSSendBatchUnprocessedReason reason)
    {
        return requestEntries.Select(e => new SQSSendBatchResponseUnprocessedEntry
        {
            Id = e.Id,
            Reason = reason
        });
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

    private string GetPublisherEndpoint(ITelemetryTrace trace, Type messageType, string? queueUrlOverride)
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

        // Check if the queue was overridden on this message-specific publishing options
        if (!string.IsNullOrEmpty(queueUrlOverride))
        {
            queueUrl = queueUrlOverride;
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

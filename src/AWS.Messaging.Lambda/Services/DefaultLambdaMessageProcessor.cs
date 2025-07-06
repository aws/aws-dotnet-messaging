// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.SQS;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Lambda.Services;

internal class DefaultLambdaMessageProcessor : ILambdaMessageProcessor, ISQSMessageCommunication
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<DefaultLambdaMessageProcessor> _logger;
    private readonly IMessageManager _messageManager;
    private readonly IEnvelopeSerializer _envelopeSerializer;
    private readonly LambdaMessageProcessorConfiguration _configuration;
    private readonly ITelemetryFactory _telemetryFactory;
    private readonly bool _isFifoEndpoint;

    private readonly SQSBatchResponse _sqsBatchResponse;

    // this is used to safely delete messages from _configuration.SQSBatchResponse.
    private readonly object _sqsBatchResponseLock = new object();

    /// <summary>
    /// Creates instance of <see cref="DefaultLambdaMessageProcessor" />
    /// </summary>
    /// <param name="logger">Logger for debugging information.</param>
    /// <param name="messageManagerFactory">The factory to create the message manager for processing messages.</param>
    /// <param name="awsClientProvider">Provides the AWS service client from the DI container.</param>
    /// <param name="configuration">The Lambda message processor configuration.</param>
    /// <param name="envelopeSerializer">Serializer used to deserialize the SQS messages</param>
    /// <param name="telemetryFactory">Factory for telemetry data</param>
    public DefaultLambdaMessageProcessor(
        ILogger<DefaultLambdaMessageProcessor> logger,
        IMessageManagerFactory messageManagerFactory,
        IAWSClientProvider awsClientProvider,
        LambdaMessageProcessorConfiguration configuration,
        IEnvelopeSerializer envelopeSerializer,
        ITelemetryFactory telemetryFactory)
    {
        _logger = logger;
        _sqsClient = awsClientProvider.GetServiceClient<IAmazonSQS>();
        _envelopeSerializer = envelopeSerializer;
        _configuration = configuration;
        _messageManager = messageManagerFactory.CreateMessageManager(this, new MessageManagerConfiguration
        {
            SupportExtendingVisibilityTimeout = false
        });
        _telemetryFactory = telemetryFactory;

        _sqsBatchResponse = new SQSBatchResponse();
        _isFifoEndpoint = _configuration.SubscriberEndpoint.EndsWith(".fifo");
    }

    /// <summary>
    /// The maximum amount of time a polling iteration should pause for while waiting
    /// for in flight messages to finish processing
    /// </summary>
    private static readonly TimeSpan CONCURRENT_CAPACITY_WAIT_TIMEOUT = TimeSpan.FromSeconds(30);


    public async Task<SQSBatchResponse?> ProcessMessagesAsync(CancellationToken token = default)
    {
        using (var trace = _telemetryFactory.Trace("Process Lambda messages"))
        {
            try
            {
                var sqsEvent = _configuration.SQSEvent;

                trace.AddMetadata(TelemetryKeys.QueueUrl, _configuration.SubscriberEndpoint);

                if (sqsEvent is null || sqsEvent.Records.Count == 0)
                {
                    return _sqsBatchResponse;
                }

                var messageEnvelopeResults = new List<ConvertToEnvelopeResult>();

                try
                {
                    foreach (var record in sqsEvent.Records)
                    {
                        var message = ConvertToStandardSQSMessage(record);
                        var messageEnvelopeResult = await _envelopeSerializer.ConvertToEnvelopeAsync(message);
                        messageEnvelopeResults.Add(messageEnvelopeResult);

                        trace.AddMetadata(TelemetryKeys.MessageId, messageEnvelopeResult.Envelope.Id);
                        trace.AddMetadata(TelemetryKeys.MessageType, messageEnvelopeResult.Envelope.MessageTypeIdentifier);
                    }

                    if (_isFifoEndpoint)
                        await ProcessInFifoMode(messageEnvelopeResults, token);
                    else
                        await ProcessInStandardMode(messageEnvelopeResults, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unknown exception initiating message handlers for incoming SQS messages.");

                    // If there are any errors queuing messages into the handlers then let the exception bubble up to allow Lambda to report a function invocation failure.
                    throw;
                }

                // If partial failure mode is not enabled then if there are any errors reported from the message handlers we
                // need to communicate up to Lambda via an exception that the invocation failed.
                if (!_configuration.UseBatchResponse && _sqsBatchResponse.BatchItemFailures?.Count > 0)
                {
                    throw new LambdaInvocationFailureException($"Lambda invocation failed because {_sqsBatchResponse.BatchItemFailures.Count} message reported failures during handling");
                }

                await ResetVisibilityTimeoutForFailures();

                return _configuration.UseBatchResponse ? _sqsBatchResponse : null;
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }

    private async Task ProcessInStandardMode(List<ConvertToEnvelopeResult> messageEnvelopeResults, CancellationToken token)
    {
        var taskList = new List<Task>(messageEnvelopeResults.Count);
        var index = 0;

        while (!token.IsCancellationRequested && index < messageEnvelopeResults.Count)
        {
            var concurrencyCapacity = _configuration.MaxNumberOfConcurrentMessages - _messageManager.ActiveMessageCount;

            // If already processing the maximum number of messages, wait for at least one to complete and then try again
            if (concurrencyCapacity <= 0)
            {
                _logger.LogTrace("The maximum number of {Max} concurrent messages is already being processed. " +
                    "Waiting for one or more to complete for a maximum of {Timeout} seconds before attempting to poll again.",
                    _configuration.MaxNumberOfConcurrentMessages, CONCURRENT_CAPACITY_WAIT_TIMEOUT.TotalSeconds);

                await _messageManager.WaitAsync(CONCURRENT_CAPACITY_WAIT_TIMEOUT);
                continue;
            }

            var task = _messageManager.ProcessMessageAsync(messageEnvelopeResults[index].Envelope, messageEnvelopeResults[index].Mapping, token);
            taskList.Add(task);
            index++;
        }

        await Task.WhenAll(taskList);
    }

    private async Task ProcessInFifoMode(List<ConvertToEnvelopeResult> messageEnvelopeResults, CancellationToken token)
    {
        var messageGroupMapping = new Dictionary<string, List<ConvertToEnvelopeResult>>();

        foreach (var messageEnvelopResult in messageEnvelopeResults)
        {
            var groupId = messageEnvelopResult.Envelope.SQSMetadata?.MessageGroupId;

            if (string.IsNullOrEmpty(groupId))
            {
                // This should never happen. But if it does, its an issue with the framework. This is not a customer induced error.
                var errorMessage = "This SQS message cannot be processed in FIFO mode because it does not have a valid message group ID";
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            if (messageGroupMapping.TryGetValue(groupId, out var messageGroup))
                messageGroup.Add(messageEnvelopResult);
            else
                messageGroupMapping[groupId] = new() { messageEnvelopResult };
        }

        var messageGroups = messageGroupMapping.Keys.ToList();
        var taskList = new List<Task>();
        var index = 0;

        while (!token.IsCancellationRequested && index < messageGroups.Count)
        {
            var concurrencyCapacity = _configuration.MaxNumberOfConcurrentMessages - _messageManager.ActiveMessageCount;

            // If already processing the maximum number of message groups, wait for at least one to complete and then try again
            if (concurrencyCapacity <= 0)
            {
                _logger.LogTrace("The maximum number of {Max} concurrent message groups are already being processed. " +
                    "Waiting for one or more to complete for a maximum of {Timeout} seconds before attempting to poll again.",
                    _configuration.MaxNumberOfConcurrentMessages, CONCURRENT_CAPACITY_WAIT_TIMEOUT.TotalSeconds);

                await _messageManager.WaitAsync(CONCURRENT_CAPACITY_WAIT_TIMEOUT);
                continue;
            }

            var groupId = messageGroups[index];
            var task = _messageManager.ProcessMessageGroupAsync(messageGroupMapping[groupId], groupId, token);
            taskList.Add(task);
            index++;
        }

        await Task.WhenAll(taskList);
    }

    /// <inheritdoc/>
    public async Task DeleteMessagesAsync(IEnumerable<MessageEnvelope> messages, CancellationToken token = default)
    {
        // If batch response is enabled then rely on Lambda to delete the messages that are not in the SQSBatchResponse returned for the Lambda function.
        if (!_configuration.DeleteMessagesWhenCompleted || _configuration.UseBatchResponse)
        {
            return;
        }

        if(!messages.Any())
        {
            return;
        }

        var request = new DeleteMessageBatchRequest
        {
            QueueUrl = _configuration.SubscriberEndpoint,
            Entries = new List<DeleteMessageBatchRequestEntry>()
        };

        foreach (var message in messages)
        {
            if (!string.IsNullOrEmpty(message.SQSMetadata?.ReceiptHandle))
            {
                _logger.LogTrace("Preparing to delete message {MessageId} with SQS receipt handle {ReceiptHandle} from queue {SubscriberEndpoint}",
                    message.Id, message.SQSMetadata.ReceiptHandle, _configuration.SubscriberEndpoint);
                request.Entries.Add(new DeleteMessageBatchRequestEntry()
                {
                    Id = message.Id,
                    ReceiptHandle = message.SQSMetadata.ReceiptHandle
                });
            }
            else
            {
                _logger.LogError("Attempted to delete message {MessageId} from {SubscriberEndpoint} without an SQS receipt handle.", message.Id, _configuration.SubscriberEndpoint);
                throw new MissingSQSMetadataException($"Attempted to delete message {message.Id} from {_configuration.SubscriberEndpoint} without an SQS receipt handle.");
            }
        }

        var response = await _sqsClient.DeleteMessageBatchAsync(request, token);

        if (response.Successful != null)
        {
            foreach (var successMessage in response.Successful)
            {
                _logger.LogTrace("Deleted message {MessageId} from queue {SubscriberEndpoint} successfully", successMessage.Id, _configuration.SubscriberEndpoint);
            }
        }

        if (response.Failed != null)
        {
            foreach (var failedMessage in response.Failed)
            {
                _logger.LogError("Failed to delete message {FailedMessageId} from queue {SubscriberEndpoint}: {FailedMessage}",
                    failedMessage.Id, _configuration.SubscriberEndpoint, failedMessage.Message);
            }
        }
    }

    /// <summary>
    /// If configured for <see cref="AWS.Messaging.Lambda.Services.LambdaMessageProcessorConfiguration.VisibilityTimeoutForBatchFailures"/>
    /// then change the visibility timeout on the failed items in the batch response.
    /// </summary>
    private async Task ResetVisibilityTimeoutForFailures()
    {
        if (_configuration.SQSEvent == null || !_configuration.UseBatchResponse || !_configuration.VisibilityTimeoutForBatchFailures.HasValue)
        {
            return;
        }
        var failureCount = _sqsBatchResponse.BatchItemFailures?.Count ?? 0;
        if (failureCount == 0)
        {
            return;
        }

        var lookup = _configuration.SQSEvent.Records.ToDictionary(x => x.MessageId);
        var visibilityTimeout = _configuration.VisibilityTimeoutForBatchFailures.Value;

        if (failureCount == 1)
        {
            _logger.LogInformation("ChangeMessageVisibility to {VisibilityTimeout}", visibilityTimeout);
            await _sqsClient.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
            {
                QueueUrl = _configuration.SubscriberEndpoint,
                ReceiptHandle = lookup[_sqsBatchResponse!.BatchItemFailures![0].ItemIdentifier].ReceiptHandle,
                VisibilityTimeout = visibilityTimeout
            });
            return;
        }

        _logger.LogInformation("ChangeMessageVisibilityBatch on {FailureCount} to {VisibilityTimeout}", failureCount, visibilityTimeout);
        await _sqsClient.ChangeMessageVisibilityBatchAsync(new ChangeMessageVisibilityBatchRequest
        {
            QueueUrl = _configuration.SubscriberEndpoint,
            Entries = _sqsBatchResponse!.BatchItemFailures!
                .Select(x => new ChangeMessageVisibilityBatchRequestEntry
                {
                    Id = x.ItemIdentifier,
                    ReceiptHandle = lookup[x.ItemIdentifier].ReceiptHandle,
                    VisibilityTimeout = visibilityTimeout
                })
                .ToList()
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is a no-op since visibility should match the length of the Lambda function timeout.
    /// </remarks>
    public Task ExtendMessageVisibilityTimeoutAsync(IEnumerable<MessageEnvelope> messages, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is a no-op when SQS event source mapping is not configured to use <see href="https://docs.aws.amazon.com/lambda/latest/dg/with-sqs.html#services-sqs-batchfailurereporting">partial batch responses.</see>
    /// </remarks>
    public ValueTask ReportMessageFailureAsync(MessageEnvelope message, CancellationToken token = default)
    {
        lock (_sqsBatchResponseLock)
        {
            if (string.IsNullOrEmpty(message.SQSMetadata?.MessageID))
            {
                _logger.LogError("The message envelope with ID {MessageEnvelopeID} was not added to the batchFailureItems list since it did not have a valid SQS message ID.", message.Id);
                throw new MissingSQSMetadataException($"The message envelope with ID {message.Id} was not added to the batchFailureItems list since it did not have a valid SQS message ID.");
            }

            var batchItemFailure = new SQSBatchResponse.BatchItemFailure
            {
                ItemIdentifier = message.SQSMetadata.MessageID
            };
            _sqsBatchResponse.BatchItemFailures.Add(batchItemFailure);
        }

        return ValueTask.CompletedTask;
    }

    internal static Message ConvertToStandardSQSMessage(SQSEvent.SQSMessage sqsEventMessage)
    {
        var sqsMessage = new Message
        {
            Attributes = sqsEventMessage.Attributes,
            Body = sqsEventMessage.Body,
            MD5OfBody = sqsEventMessage.Md5OfBody,
            MD5OfMessageAttributes = sqsEventMessage.Md5OfMessageAttributes,
            MessageId = sqsEventMessage.MessageId,
            ReceiptHandle = sqsEventMessage.ReceiptHandle,
        };

        if (sqsEventMessage.MessageAttributes != null)
        {
            sqsMessage.MessageAttributes = new Dictionary<string, MessageAttributeValue>();
            foreach (var attr in sqsEventMessage.MessageAttributes)
            {
                sqsMessage.MessageAttributes.Add(attr.Key, new MessageAttributeValue
                {
                    BinaryListValues = attr.Value.BinaryListValues,
                    BinaryValue = attr.Value.BinaryValue,
                    DataType = attr.Value.DataType,
                    StringListValues = attr.Value.StringListValues,
                    StringValue = attr.Value.StringValue,
                });
            }
        }

        return sqsMessage;
    }
}


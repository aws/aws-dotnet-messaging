// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.Services;
using AWS.Messaging.Services.Backoff;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.SQS;

/// <summary>
/// SQS implementation of the <see cref="IMessagePoller" />
/// </summary>
internal class SQSMessagePoller : IMessagePoller, ISQSMessageCommunication
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<SQSMessagePoller> _logger;
    private readonly IMessageManager _messageManager;
    private readonly SQSMessagePollerConfiguration _configuration;
    private readonly IEnvelopeSerializer _envelopeSerializer;
    private readonly IBackoffHandler _backoffHandler;
    private readonly PollingControlToken _pollingControlToken;
    private readonly bool _isFifoEndpoint;

    /// <summary>
    /// Maximum valid value for <see cref="ReceiveMessageRequest.MaxNumberOfMessages"/>
    /// </summary>
    private const int SQS_MAX_NUMBER_MESSAGES_TO_READ = 10;

    /// <summary>
    /// Maximum valid value for number of messages in <see cref="ChangeMessageVisibilityBatchRequest"/>
    /// </summary>
    private const int SQS_MAX_MESSAGE_CHANGE_VISIBILITY = 10;

    /// <summary>
    /// The maximum amount of time a polling iteration should pause for while waiting
    /// for in flight messages to finish processing
    /// </summary>
    private static readonly TimeSpan CONCURRENT_CAPACITY_WAIT_TIMEOUT = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Creates instance of <see cref="AWS.Messaging.SQS.SQSMessagePoller" />
    /// </summary>
    /// <param name="logger">Logger for debugging information.</param>
    /// <param name="messageManagerFactory">The factory to create the message manager for processing messages.</param>
    /// <param name="awsClientProvider">Provides the AWS service client from the DI container.</param>
    /// <param name="configuration">The SQS message poller configuration.</param>
    /// <param name="envelopeSerializer">Serializer used to deserialize the SQS messages</param>
    /// <param name="backoffHandler">Backoff handler for performing back-offs if exceptions are thrown when polling SQS.</param>
    /// <param name="pollingControlToken">Control token to start and stop the poller.</param>
    public SQSMessagePoller(
        ILogger<SQSMessagePoller> logger,
        IMessageManagerFactory messageManagerFactory,
        IAWSClientProvider awsClientProvider,
        SQSMessagePollerConfiguration configuration,
        IEnvelopeSerializer envelopeSerializer,
        IBackoffHandler backoffHandler,
        PollingControlToken pollingControlToken)
    {
        _logger = logger;
        _sqsClient = awsClientProvider.GetServiceClient<IAmazonSQS>();
        _configuration = configuration;
        _envelopeSerializer = envelopeSerializer;
        _backoffHandler = backoffHandler;
        _pollingControlToken = pollingControlToken;
        _isFifoEndpoint = configuration.SubscriberEndpoint.EndsWith(".fifo");

        _messageManager = messageManagerFactory.CreateMessageManager(this, _configuration.ToMessageManagerConfiguration());
    }

    internal IEnvelopeSerializer EnvelopeSerializer => _envelopeSerializer;

    /// <inheritdoc/>
    public async Task StartPollingAsync(CancellationToken token = default)
    {
        await PollQueue(token);
    }

    /// <summary>
    /// Polls SQS indefinitely until cancelled. Message receipt can be stopped and started using <see cref="PollingControlToken"/></summary>
    /// <param name="token">Cancellation token to shutdown the poller.</param>
    private async Task PollQueue(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_pollingControlToken.IsPollingEnabled)
            {
                await Task.Delay(_pollingControlToken.PollingWaitTime, token);
                continue;
            }

            var numberOfMessagesToRead = _configuration.MaxNumberOfConcurrentMessages - _messageManager.ActiveMessageCount;

            // If already processing the maximum number of messages, wait for at least one to complete and then try again
            if (numberOfMessagesToRead <= 0)
            {
                _logger.LogTrace("The maximum number of {Max} concurrent messages is already being processed. " +
                    "Waiting for one or more to complete for a maximum of {Timeout} seconds before attempting to poll again.",
                    _configuration.MaxNumberOfConcurrentMessages, CONCURRENT_CAPACITY_WAIT_TIMEOUT.TotalSeconds);

                await _messageManager.WaitAsync(CONCURRENT_CAPACITY_WAIT_TIMEOUT);
                continue;
            }

            // Only read SQS's maximum number of messages. If configured for
            // higher concurrency, then the next iteration could read more
            if (numberOfMessagesToRead > SQS_MAX_NUMBER_MESSAGES_TO_READ)
            {
                numberOfMessagesToRead = SQS_MAX_NUMBER_MESSAGES_TO_READ;
            }

            var receiveMessageRequest = new ReceiveMessageRequest
            {
                QueueUrl = _configuration.SubscriberEndpoint,
                VisibilityTimeout = _configuration.VisibilityTimeout,
                WaitTimeSeconds = _configuration.WaitTimeSeconds,
                MaxNumberOfMessages = numberOfMessagesToRead,
                MessageSystemAttributeNames = new List<string> { "All" },
                MessageAttributeNames = new List<string> { "All" }
            };

            List<Message>? receivedMessages =
                await _backoffHandler.BackoffAsync<List<Message>?>(async () =>
                {
                    _logger.LogTrace("Retrieving up to {NumberOfMessagesToRead} messages from {QueueUrl}",
                        receiveMessageRequest.MaxNumberOfMessages, receiveMessageRequest.QueueUrl);
                    ReceiveMessageResponse receiveMessageResponse;
                    try
                    {
                        receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(receiveMessageRequest, token);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogTrace("Cancellation requested while receiving messages, probably due to shutdown. Returning empty list of messages");
                        return new List<Message>();
                    }

                    _logger.LogTrace("Retrieved {MessagesCount} messages from {QueueUrl} via request ID {RequestId}",
                        receiveMessageResponse.Messages?.Count ?? 0, receiveMessageRequest.QueueUrl, receiveMessageResponse.ResponseMetadata.RequestId);

                    return receiveMessageResponse.Messages;
                },
                _configuration,
                token);

            if (receivedMessages is null)
                continue;

            var messageEnvelopResults = new List<ConvertToEnvelopeResult>();
            foreach (var message in receivedMessages)
            {
                try
                {
                    var messageEnvelopeResult = await _envelopeSerializer.ConvertToEnvelopeAsync(message);
                    messageEnvelopResults.Add(messageEnvelopeResult);
                }
                catch (AWSMessagingException)
                {
                    // Swallow exceptions thrown by the framework, and rely on the thrower to log
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unknown exception occurred while polling {SubscriberEndpoint}", _configuration.SubscriberEndpoint);
                }
            }

            try
            {
                if (_isFifoEndpoint)
                    ProcessInFifoMode(messageEnvelopResults, token);
                else
                    ProcessInStandardMode(messageEnvelopResults, token);
            }
            catch (AWSMessagingException)
            {
                // Swallow exceptions thrown by the framework, and rely on the thrower to log
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unknown exception occurred while processing messages from {SubscriberEndpoint}", _configuration.SubscriberEndpoint);
            }
        }
    }

    private void ProcessInStandardMode(List<ConvertToEnvelopeResult> messageEnvelopResults, CancellationToken token)
    {
        foreach (var result in messageEnvelopResults)
        {
            // Don't await this result, we want to process multiple messages concurrently
            _ = _messageManager.ProcessMessageAsync(result.Envelope, result.Mapping, token);
        }
    }

    private void ProcessInFifoMode(List<ConvertToEnvelopeResult> messageEnvelopResults, CancellationToken token)
    {
        var messageGroupMapping = new Dictionary<string, List<ConvertToEnvelopeResult>>();

        foreach (var messageEnvelopResult in messageEnvelopResults)
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

        foreach (var item in messageGroupMapping)
        {
            // Don't await this result, we want to process multiple message groups concurrently
            _ = _messageManager.ProcessMessageGroupAsync(item.Value, item.Key, token);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteMessagesAsync(IEnumerable<MessageEnvelope> messages, CancellationToken token = default)
    {
        if (!messages.Any())
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

        try
        {
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
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "Failed to delete message(s) [{MessageIds}] from queue {SubscriberEndpoint}",
                string.Join(", ", messages.Select(x => x.Id)), _configuration.SubscriberEndpoint);

            // Rethrow the exception to fail fast for invalid configuration, permissioning, etc.
            if (_configuration.IsExceptionFatal(ex))
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected exception occurred while deleting messages from queue {SubscriberEndpoint}", _configuration.SubscriberEndpoint);

            // Rethrow the exception to fail fast for invalid configuration, permissioning, etc.
            if (_configuration.IsExceptionFatal(ex))
            {
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task ExtendMessageVisibilityTimeoutAsync(IEnumerable<MessageEnvelope> messages, CancellationToken token = default)
    {
        if (!messages.Any())
        {
            return;
        }

        var requestBatches = new List<ChangeMessageVisibilityBatchRequest>();

        var currentRequest = new ChangeMessageVisibilityBatchRequest
        {
            QueueUrl = _configuration.SubscriberEndpoint,
            Entries = new List<ChangeMessageVisibilityBatchRequestEntry>()
        };
        foreach (var message in messages)
        {
            if (!string.IsNullOrEmpty(message.SQSMetadata?.ReceiptHandle))
            {
                _logger.LogTrace("Preparing to extend the visibility of {MessageId} with SQS receipt handle {ReceiptHandle} by {VisibilityTimeout} seconds",
                    message.Id, message.SQSMetadata.ReceiptHandle, _configuration.VisibilityTimeout);

                if (currentRequest.Entries.Count >= SQS_MAX_MESSAGE_CHANGE_VISIBILITY)
                {
                    requestBatches.Add(currentRequest);
                    currentRequest = new ChangeMessageVisibilityBatchRequest
                    {
                        QueueUrl = _configuration.SubscriberEndpoint,
                        Entries = new List<ChangeMessageVisibilityBatchRequestEntry>()
                    };
                }
                currentRequest.Entries.Add(new ChangeMessageVisibilityBatchRequestEntry
                {
                    Id = $"batchNum_{currentRequest.Entries.Count}_messageId_{message.Id}",
                    ReceiptHandle = message.SQSMetadata.ReceiptHandle,
                    VisibilityTimeout = _configuration.VisibilityTimeout
                });
            }
            else
            {
                _logger.LogError("Attempted to change the visibility of message {MessageId} from {SubscriberEndpoint} without an SQS receipt handle.", message.Id, _configuration.SubscriberEndpoint);
                throw new MissingSQSMetadataException($"Attempted to change the visibility of message {message.Id} from {_configuration.SubscriberEndpoint} without an SQS receipt handle.");
            }
        }
        requestBatches.Add(currentRequest);

        List<Task<ChangeMessageVisibilityBatchResponse>> changeMessageVisibilityBatchTasks =
            requestBatches
            .Select(request => _sqsClient.ChangeMessageVisibilityBatchAsync(request, token))
            .ToList();

        try
        {
            var responses = await Task.WhenAll(changeMessageVisibilityBatchTasks);
        }
        catch (Exception ex)
        {
            // TODO: this is being hit even for an AmazonSQSException that we want to handle below
            _logger.LogError(ex, "An unexpected exception occurred while extending message visibility on queue {SubscriberEndpoint}", _configuration.SubscriberEndpoint);
        }

        foreach (var changeMessageVisibilityBatchTask in changeMessageVisibilityBatchTasks)
        {
            if (!changeMessageVisibilityBatchTask.IsFaulted)
            {
                var response = changeMessageVisibilityBatchTask.Result;
                if (response.Successful != null)
                {
                    foreach (var successMessage in response.Successful)
                    {
                        _logger.LogTrace("Extended the visibility of message {MessageId} on queue {SubscriberEndpoint} successfully", successMessage.Id, _configuration.SubscriberEndpoint);
                    }
                }

                if (response.Failed != null)
                {
                    foreach (var failedMessage in response.Failed)
                    {
                        // It's possible that the task that is extending the message visibility timeout of in flight messages attempts to extend
                        // a message whose handler task has just finished and deleted the message. Rather than adding synchronization between the two
                        // (such as stopping handlers from deleting while the extension task is running), we "downgrade" these errors to trace messages
                        if (failedMessage.Code.Equals("ReceiptHandleIsInvalid") &&
                            failedMessage.Message.Equals("Message does not exist or is not available for visibility timeout change", StringComparison.InvariantCultureIgnoreCase))
                        {
                            _logger.LogTrace("Failed to extend the visibility of message {FailedMessageId} on queue {SubscriberEndpoint} with code {Code}, which was likely deleted: {FailedMessage}",
                                failedMessage.Id, _configuration.SubscriberEndpoint, failedMessage.Code, failedMessage.Message);
                        }
                        else // treat any other failed entries as errors
                        {
                            _logger.LogError("Failed to extend the visibility of message {FailedMessageId} on queue {SubscriberEndpoint} with code {Code}: {FailedMessage}",
                                failedMessage.Id, _configuration.SubscriberEndpoint, failedMessage.Code, failedMessage.Message);
                        }
                    }
                }
            }
            else
            {
                if (changeMessageVisibilityBatchTask.Exception?.InnerException is AmazonSQSException amazonEx)
                {
                    _logger.LogError(amazonEx, "Failed to extend the visibility of message(s) [{MessageIds}] on queue {SubscriberEndpoint}",
                        string.Join(", ", messages.Select(x => x.Id)), _configuration.SubscriberEndpoint);

                    // Rethrow the exception to fail fast for invalid configuration, permissioning, etc.
                    if (_configuration.IsExceptionFatal(amazonEx))
                    {
                        throw amazonEx;
                    }
                }
                else if (changeMessageVisibilityBatchTask.Exception?.InnerException is Exception ex)
                {
                    _logger.LogError(ex, "An unexpected exception occurred while extending message visibility on queue {SubscriberEndpoint}", _configuration.SubscriberEndpoint);

                    // Rethrow the exception to fail fast for invalid configuration, permissioning, etc.
                    if (_configuration.IsExceptionFatal(ex))
                    {
                        throw ex;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>This is a no-op since we currently do not have any special logic to handle messages that failed to process in <see cref="SQSMessagePoller"/></remarks>
    public ValueTask ReportMessageFailureAsync(MessageEnvelope message, CancellationToken token = default)
    {
        return ValueTask.CompletedTask;
    }
}

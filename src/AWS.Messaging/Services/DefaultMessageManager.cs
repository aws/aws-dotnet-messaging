// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using AWS.Messaging.Configuration;
using AWS.Messaging.Serialization;
using AWS.Messaging.SQS;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Services;

/// <inheritdoc cref="IMessageManager"/>
public class DefaultMessageManager : IMessageManager
{
    private readonly ISQSMessageCommunication _sqsMessageCommunication;
    private readonly IHandlerInvoker _handlerInvoker;
    private readonly ILogger<DefaultMessageManager> _logger;
    private readonly MessageManagerConfiguration _configuration;

    private int _activeMessageCount;
    private readonly ManualResetEventSlim _activeMessageCountDecrementedEvent = new ManualResetEventSlim(false);

    private readonly ConcurrentDictionary<MessageEnvelope, InFlightMetadata> _inFlightMessageMetadata = new();
    private readonly object _visibilityTimeoutExtensionTaskLock = new object();
    private Task? _visibilityTimeoutExtensionTask;

    /// <summary>
    /// Constructs an instance of <see cref="DefaultMessageManager"/>
    /// </summary>
    /// <param name="sqsMessageCommunication">Provides APIs to communicate back to SQS and the associated Queue for incoming messages.</param>
    /// <param name="handlerInvoker">Used to look up and invoke the correct handler for each message</param>
    /// <param name="logger">Logger for debugging information</param>
    /// <param name="configuration">The configuration for the message manager</param>
    public DefaultMessageManager(ISQSMessageCommunication sqsMessageCommunication, IHandlerInvoker handlerInvoker, ILogger<DefaultMessageManager> logger, MessageManagerConfiguration configuration)
    {
        _sqsMessageCommunication = sqsMessageCommunication;
        _handlerInvoker = handlerInvoker;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public int ActiveMessageCount
    {
        get => Volatile.Read(ref _activeMessageCount);
    }

    /// <summary>
    /// Updates <see cref="ActiveMessageCount"/> in a thread safe manner
    /// </summary>
    /// <param name="delta">Difference to apply to the current active message count</param>
    /// <returns>Updated <see cref="ActiveMessageCount"/></returns>
    private int UpdateActiveMessageCount(int delta)
    {
        int newValue = Interlocked.Add(ref _activeMessageCount, delta);
        _logger.LogTrace("Updating {ActiveMessageCount} from {Before} to {After}", nameof(ActiveMessageCount), newValue - delta, newValue);

 
        // If we just decremented the active message count, signal to the poller
        // that there may be more capacity available again.
        if (delta < 0)
        {
            _activeMessageCountDecrementedEvent.Set();
        }

        return newValue;
    }

    /// <inheritdoc/>
    public Task WaitAsync(TimeSpan timeout)
    {
        _logger.LogTrace("Beginning wait for {Name} for a maximum of {Timeout} seconds", nameof(_activeMessageCountDecrementedEvent), timeout.TotalSeconds);

        // TODO: Rework to avoid this synchronous wait.
        // See https://github.com/dotnet/runtime/issues/35962 for potential workarounds
        var wasSet = _activeMessageCountDecrementedEvent.Wait(timeout);

        // Don't reset if we hit the timeout
        if (wasSet)
        {
            _logger.LogTrace("{Name} was set, resetting the event", nameof(_activeMessageCountDecrementedEvent));
            _activeMessageCountDecrementedEvent.Reset();
        }
        else
        {
            _logger.LogTrace("Timed out at {Timeout} seconds while waiting for {Name}, returning.", timeout.TotalSeconds, nameof(_activeMessageCountDecrementedEvent));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ProcessMessageAsync(MessageEnvelope messageEnvelope, SubscriberMapping subscriberMapping, CancellationToken token = default)
    {
        try
        {
            UpdateActiveMessageCount(1);

            AcknowledgeMessage(messageEnvelope, token);

            await InvokeHandler(messageEnvelope, subscriberMapping, token);
        }
        finally
        {
            UpdateActiveMessageCount(-1);
        }
    }

    /// <inheritdoc/>
    public async Task ProcessMessageGroupAsync(List<ConvertToEnvelopeResult> messageGroup, string groupId, CancellationToken token = default)
    {
        try
        {
            // Update the active message count by 1 once for the entire message group.
            UpdateActiveMessageCount(1);

            foreach (var message in messageGroup)
            {
                AcknowledgeMessage(message.Envelope, token);
            }

            var remaining = messageGroup.Count;

            // Sequentially process each message within the group
            for(var i = 0; i < messageGroup.Count; i++)
            {
                var message = messageGroup[i];

                var isSuccessful = await InvokeHandler(message.Envelope, message.Mapping, token);
                if (!isSuccessful)
                {
                    // If the handler invocation fails for any message, report failure for the rest in the group so they are reported back to Lambda
                    // as not failure that should be retried. Otherwise Lambda will think the messages were successfully processed and delete the messages.
                    for (var unprocessedIndex = i + 1; unprocessedIndex < messageGroup.Count; unprocessedIndex++)
                    {
                        await _sqsMessageCommunication.ReportMessageFailureAsync(messageGroup[unprocessedIndex].Envelope, token);
                        _inFlightMessageMetadata.Remove(messageGroup[unprocessedIndex].Envelope, out _);
                    }
                    _logger.LogError("Handler invocation failed for a message belonging to message group '{GroupdId}' having message ID '{MessageID}'. Skipping processing of {Remaining} messages from the same group.", groupId, message.Envelope.Id, remaining);
                    break;
                }
                remaining -= 1;
            }
        }
        finally
        {
            // Decrement the active message count by 1 indicating that the message group is done being processed.
            UpdateActiveMessageCount(-1);
        }
    }

    /// <summary>
    /// Places the message in the internal collection of in-flight messages
    /// and starts the background task to extend their visibility timeout if the configuration supports it.
    /// </summary>
    private void AcknowledgeMessage(MessageEnvelope messageEnvelope, CancellationToken token)
    {
        // Add that metadata to the dictionary of running handlers, used to extend the visibility timeout if necessary
        _inFlightMessageMetadata.TryAdd(messageEnvelope, new InFlightMetadata(
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(_configuration.VisibilityTimeout)
        ));

        if (_configuration.SupportExtendingVisibilityTimeout)
            StartMessageVisibilityExtensionTaskIfNotRunning(token);
    }

    private async Task<bool> InvokeHandler(MessageEnvelope messageEnvelope, SubscriberMapping subscriberMapping, CancellationToken cancelToken)
    {
        var isSuccessful = false;
        var handlerTask = _handlerInvoker.InvokeAsync(messageEnvelope, subscriberMapping, cancelToken);
        try
        {
            await handlerTask;
        }
        catch (InvalidMessageHandlerSignatureException)
        {
            throw;
        }
        catch (AWSMessagingException)
        {
            // Swallow exceptions thrown by the framework, and rely on the thrower to log
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has been thrown from handler '{HandlerType}' while processing message with ID '{MessageId}'", subscriberMapping.HandlerType.Name, messageEnvelope.Id);
        }

        _inFlightMessageMetadata.Remove(messageEnvelope, out _);

        if (handlerTask.IsCompletedSuccessfully)
        {
            if (handlerTask.Result.IsSuccess)
            {
                // Delete the message from the queue if it was processed successfully
                await _sqsMessageCommunication.DeleteMessagesAsync(new MessageEnvelope[] { messageEnvelope });
                isSuccessful = true;
            }
            else // the handler still finished, but returned MessageProcessStatus.Failed
            {
                _logger.LogError("Message handling completed unsuccessfully for message ID {MessageId}", messageEnvelope.Id);
                await _sqsMessageCommunication.ReportMessageFailureAsync(messageEnvelope);
            }
        }
        else if (handlerTask.IsFaulted)
        {
            _logger.LogError(handlerTask.Exception, "An exception has been thrown from handler '{HandlerType}' while processing message with ID '{MessageId}'", subscriberMapping.HandlerType.Name, messageEnvelope.Id);
            await _sqsMessageCommunication.ReportMessageFailureAsync(messageEnvelope);
        }

        return isSuccessful;
    }

    /// <summary>
    /// Starts the task that extends the visibility timeout of in-flight messages
    /// </summary>
    /// <param name="token">Cancellation token to stop the visibility timeout extension task</param>
    private void StartMessageVisibilityExtensionTaskIfNotRunning(CancellationToken token)
    {
        // It may either have been never started, or previously started and completed because there were no more in flight messages
        if (_visibilityTimeoutExtensionTask == null || _visibilityTimeoutExtensionTask.IsCompleted)
        {
            lock(_visibilityTimeoutExtensionTaskLock)
            {
                if (_visibilityTimeoutExtensionTask == null || _visibilityTimeoutExtensionTask.IsCompleted)
                {
                    _visibilityTimeoutExtensionTask = ExtendUnfinishedMessageVisibilityTimeoutBatch(token);

                    _logger.LogTrace("Started task with id {Id} to extend the visibility timeout of in flight messages", _visibilityTimeoutExtensionTask.Id);
                }
            }
        }
    }

    /// <summary>
    /// Extends the visibility timeout periodically for messages whose corresponding handler task is not yet complete
    /// </summary>
    /// <param name="token">Cancellation token to stop the visibility timeout extension loop</param>
    private async Task ExtendUnfinishedMessageVisibilityTimeoutBatch(CancellationToken token)
    {
        IEnumerable<KeyValuePair<MessageEnvelope, InFlightMetadata>> unfinishedMessages;

        do
        {
            // Wait for the configured interval before extending visibility
            await Task.Delay(_configuration.VisibilityTimeoutExtensionHeartbeatInterval, token);

            // Select the message envelopes whose corresponding handler task is not yet complete.
            //
            //   The .ToList() is important as we want to evaluate their expected expiration relative to the threshold at this instant,
            //   rather than lazily. Otherwise the batch of messages that are eligible for a visibility timeout extension may change between
            //   actually extending the timeout and recording that we've done so.
            unfinishedMessages = _inFlightMessageMetadata.Where(messageAndMetadata =>
                messageAndMetadata.Value.IsMessageVisibilityTimeoutExpiring(_configuration.VisibilityTimeoutExtensionThreshold)).ToList();

            // TODO: Handle the race condition where a message could have finished handling and be deleted concurrently
            if (unfinishedMessages.Any())
            {
                // Update the timestamp that the visibility timeout window is expected to expire
                // Per SQS documentation: "The new timeout period takes effect from the time you call the ChangeMessageVisibility action"
                foreach (var unfinishedMessage in unfinishedMessages)
                {
                    unfinishedMessage.Value.UpdateExpectedVisibilityTimeoutExpiration(_configuration.VisibilityTimeout);
                }

                await _sqsMessageCommunication.ExtendMessageVisibilityTimeoutAsync(unfinishedMessages.Select(messageAndMetadata => messageAndMetadata.Key), token);
            }
        } while (_inFlightMessageMetadata.Any() && !token.IsCancellationRequested);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS;
using Amazon.SQS.Model;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Internal configuration for polling messages from SQS
/// </summary>
public class SQSMessagePollerConfiguration : IMessagePollerConfiguration
{
    /// <summary>
    /// Default value for <see cref="MaxNumberOfConcurrentMessages"/>
    /// </summary>
    /// <remarks>The default value is 10 messages.</remarks>
    public const int DEFAULT_MAX_NUMBER_OF_CONCURRENT_MESSAGES = 10;

    /// <summary>
    /// Default value for <see cref="VisibilityTimeout"/>
    /// </summary>
    /// <remarks>The default value is 30 seconds.</remarks>
    public const int DEFAULT_VISIBILITY_TIMEOUT_SECONDS = 30;

    /// <summary>
    /// Default value for <see cref="VisibilityTimeoutExtensionThreshold"/>
    /// </summary>
    /// <remarks>The default value is 5 seconds.</remarks>
    public const int DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_THRESHOLD_SECONDS = 5;

    /// <summary>
    /// Default value for <see cref="VisibilityTimeoutExtensionHeartbeatInterval"/>
    /// </summary>
    /// <remarks>The default value is 1 second.</remarks>
    public const int DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_HEARTBEAT_INTERVAL = 1;

    /// <summary>
    /// Default value for <see cref="WaitTimeSeconds"/>
    /// </summary>
    /// <remarks>The default value is 20 seconds.</remarks>
    public const int DEFAULT_WAIT_TIME_SECONDS = 20;

    /// <summary>
    /// The SQS QueueUrl to poll messages from.
    /// </summary>
    public string SubscriberEndpoint { get; }

    /// <summary>
    /// The maximum number of messages from this queue to process concurrently.
    /// </summary>
    /// <remarks><inheritdoc cref="DEFAULT_MAX_NUMBER_OF_CONCURRENT_MESSAGES" path="//remarks"/></remarks>
    public int MaxNumberOfConcurrentMessages { get; init; } = DEFAULT_MAX_NUMBER_OF_CONCURRENT_MESSAGES;

    /// <summary>
    /// <inheritdoc cref="ReceiveMessageRequest.VisibilityTimeout"/>
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="DEFAULT_VISIBILITY_TIMEOUT_SECONDS" path="//remarks"/>
    /// The minimum is 0 seconds. The maximum is 12 hours.
    /// </remarks>
    public int VisibilityTimeout { get; init; } = DEFAULT_VISIBILITY_TIMEOUT_SECONDS;

    /// <summary>
    /// When an in flight message is within this many seconds of becoming visible again, the framework will extend its visibility timeout automatically.
    /// The new visibility timeout will be set to <see cref="VisibilityTimeout"/> seconds relative to now.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_THRESHOLD_SECONDS" path="//remarks"/>
    /// </remarks>
    public int VisibilityTimeoutExtensionThreshold { get; init; } = DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_THRESHOLD_SECONDS;

    /// <summary>
    /// How frequently the framework will check in flight messages and extend the visibility
    /// timeout of messages that will expire within the <see cref="VisibilityTimeoutExtensionThreshold"/>.
    /// </summary>
    /// /// <remarks>
    /// <inheritdoc cref="DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_HEARTBEAT_INTERVAL" path="//remarks"/>
    /// </remarks>
    public int VisibilityTimeoutExtensionHeartbeatInterval { get; init; } = DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_HEARTBEAT_INTERVAL;

    /// <summary>
    /// <inheritdoc cref="ReceiveMessageRequest.WaitTimeSeconds"/>
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="DEFAULT_WAIT_TIME_SECONDS" path="//remarks"/>
    /// The minimum is 0 seconds. The maximum is 20 seconds.
    /// </remarks>
    public int WaitTimeSeconds { get; init; } = DEFAULT_WAIT_TIME_SECONDS;

    /// <summary>
    /// Determines if a given exception should be treated as fatal and rethrown to stop the SQS poller.
    /// </summary>
    /// <remarks>This method only applies to the SQS poller, and not to publishing messages nor handling messages in Lambda.</remarks>
    public Func<Exception, bool> IsExceptionFatal { get; set; } = DefaultIsExceptionFatal;

    /// <summary>
    /// An optional <see cref="Configuration.PollingControlToken"/> scoped to this poller, used to start and stop it independently of other pollers on the same message bus.
    /// </summary>
    /// <remarks>
    /// When <c>null</c> (the default), the poller uses the bus-scoped token configured via <see cref="IMessageBusBuilder.ConfigurePollingControlToken"/>.
    /// When set, the poller is controlled exclusively by this token and is unaffected by the bus-scoped token.
    /// </remarks>
    public PollingControlToken? PollingControlToken { get; init; }

    /// <summary>
    /// Construct an instance of <see cref="SQSMessagePollerConfiguration" />
    /// </summary>
    /// <param name="queueUrl">The SQS QueueUrl to poll messages from.</param>
    public SQSMessagePollerConfiguration(string queueUrl)
    {
        if (string.IsNullOrEmpty(queueUrl))
            throw new InvalidSubscriberEndpointException("The SQS Queue URL cannot be empty.");

        SubscriberEndpoint = queueUrl;
    }

    /// <summary>
    /// Converts this instance to a <see cref="MessageManagerConfiguration"/>
    /// </summary>
    /// <returns></returns>
    internal MessageManagerConfiguration ToMessageManagerConfiguration()
    {
        return new MessageManagerConfiguration
        {
            SupportExtendingVisibilityTimeout = true,
            VisibilityTimeout = VisibilityTimeout,
            VisibilityTimeoutExtensionThreshold = VisibilityTimeoutExtensionThreshold,
            VisibilityTimeoutExtensionHeartbeatInterval = VisibilityTimeoutExtensionHeartbeatInterval
        };
    }

    /// <summary>
    /// <see cref="AmazonSQSException"/> error codes that should be treated as fatal and stop the poller
    /// </summary>
    /// <remarks>
    /// These are the subset of https://docs.aws.amazon.com/AWSSimpleQueueService/latest/APIReference/API_ReceiveMessage.html#API_ReceiveMessage_Errors
    /// that aren't modeled as typed exceptions
    /// </remarks>
    private static readonly HashSet<string> _fatalSQSErrorCodes = new HashSet<string>
    {
        "AccessDenied", // Returned due to insufficient IAM permissions to read from the configured queue
    };

    /// <summary>
    /// Default logic that determines if a given exception should be treated as fatal and rethrown to stop the SQS poller.
    /// </summary>
    /// <remarks>
    /// This treats exceptions related to queue or KMS permissions or the framework configuration as fatal.
    /// Exceptions related to the deserializing and handling of a specific message are not fatal.</remarks>
    /// <param name="exception">Exception to determine if it's fatal</param>
    /// <returns>True to stop the SQS poller if the exception is caught, false otherwise</returns>
    internal static bool DefaultIsExceptionFatal(Exception exception)
    {
        switch (exception)
        {
            // Modeled SQS exceptions that should be treated as fatal
            case QueueDoesNotExistException:    // Queue URL doesn't exist
            case UnsupportedOperationException: // Error code 400. Unsupported operation.
            case InvalidAddressException:       // The accountId is invalid.
            case InvalidSecurityException:      // When the request to a queue is not HTTPS and SigV4.
            case KmsAccessDeniedException:      // The caller doesn't have the required KMS access.
            case KmsInvalidKeyUsageException:   // The key is incompatible with the operation, or the encryption/signing algorithm is incompatible.
            case KmsInvalidStateException:      // The state of the specified resource is not valid for this request.
            case KmsNotFoundException:          // The specified entity or resource could not be found.
            case KmsOptInRequiredException:     // The specified key policy isn't syntactically or semantically correct.
                return true;

            // For unmodeled SQS exceptions that don't have a corresponding .NET type, check the error code
            case AmazonSQSException sqsException:
                return _fatalSQSErrorCodes.Contains(sqsException.ErrorCode);

            // AWSMessagingExceptions thrown by the framework that should be treated as fatal
            case FailedToFindAWSServiceClientException:     // Failed to resolve AWS service clients from DI
            case InvalidAppSettingsConfigurationException:  // Failed to find the handler type from what was specified in settings
            case InvalidMessageHandlerSignatureException:   // A subscriber mapping was registered, but failed to invoke the handler
                return true;

            default:
                return false;
        }
    }
}

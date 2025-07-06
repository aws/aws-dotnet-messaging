// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using AWS.Messaging.Lambda.Services;
using AWS.Messaging.Lambda.Services.Internal;
using AWS.Messaging.Telemetry;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Lambda;

/// <summary>
/// This is an internal implementation of <see cref="ILambdaMessaging"/>
/// </summary>
internal class DefaultLambdaMessaging : ILambdaMessaging
{
    private readonly ILogger<DefaultLambdaMessaging> _logger;
    private readonly ILambdaMessageProcessorFactory _messageProcessorFactory;
    private readonly LambdaContextServiceHolder _lambdaContextHolder;
    private readonly LambdaMessagingOptions _lambdaMessagingOptions;
    private readonly ITelemetryFactory _telemetryFactory;

    /// <summary>
    /// Creates an instance of <see cref="DefaultLambdaMessaging" />
    /// </summary>
    /// <param name="logger">Logger for debugging information</param>
    /// <param name="messageProcessorFactory">Factory for creating a <see cref="ILambdaMessageProcessor"/> for each configuration</param>
    /// <param name="lambdaContextHolder">Used to hold the ILambdaContext for DI injections</param>
    /// <param name="lambdaMessagingOptions">Configuration options for concurrency processing of messages inside Lambda functions.</param>
    /// <param name="telemetryFactory">Factory for telemetry data</param>
    public DefaultLambdaMessaging(
        ILogger<DefaultLambdaMessaging> logger,
        ILambdaMessageProcessorFactory messageProcessorFactory,
        LambdaContextServiceHolder lambdaContextHolder,
        LambdaMessagingOptions lambdaMessagingOptions,
        ITelemetryFactory telemetryFactory)
    {
        _logger = logger;
        _messageProcessorFactory = messageProcessorFactory;
        _lambdaContextHolder = lambdaContextHolder;
        _lambdaMessagingOptions = lambdaMessagingOptions;
        _telemetryFactory = telemetryFactory;
    }

    /// <inheritdoc/>
    public async Task ProcessLambdaEventAsync(SQSEvent sqsEvent, ILambdaContext context)
    {
        using (var trace = _telemetryFactory.Trace("Processing Lambda event from AWS SQS"))
        {
            try
            {
                if (sqsEvent.Records?.Count == 0)
                    return;

                _lambdaContextHolder.Context = context;
                using var cts = new CancellationTokenSource();
                try
                {
                    var lambdaMessageProcessorConfiguration = CreateLambdaMessageProcessorConfiguration(sqsEvent, useBatchResponse: false, trace, _lambdaMessagingOptions);
                    var lambdaMessageProcessor = _messageProcessorFactory.CreateLambdaMessageProcessor(lambdaMessageProcessorConfiguration);
                    await lambdaMessageProcessor.ProcessMessagesAsync(cts.Token);
                }
                finally
                {
                    // At this point all background tasks should have either succeeded or we are in an error state. If we are in an
                    // error state we need to cancel any background tasks so they don't continue running in future Lambda invocations.
                    cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<SQSBatchResponse> ProcessLambdaEventWithBatchResponseAsync(SQSEvent sqsEvent, ILambdaContext context)
    {
        using (var trace = _telemetryFactory.Trace("Processing Lambda event with batch response from AWS SQS"))
        {
            try
            {
                if (sqsEvent.Records?.Count == 0)
                    return new SQSBatchResponse();

                _lambdaContextHolder.Context = context;
                using var cts = new CancellationTokenSource();
                try
                {
                    var lambdaMessageProcessorConfiguration = CreateLambdaMessageProcessorConfiguration(sqsEvent, useBatchResponse: true, trace, _lambdaMessagingOptions);
                    var lambdaMessageProcessor = _messageProcessorFactory.CreateLambdaMessageProcessor(lambdaMessageProcessorConfiguration);
                    var sqsBatchResponse = await lambdaMessageProcessor.ProcessMessagesAsync(cts.Token);
                    return sqsBatchResponse ?? new SQSBatchResponse();
                }
                finally
                {
                    // At this point all background tasks should have either succeeded or we are in an error state. If we are in an
                    // error state we need to cancel any background tasks so they don't continue running in future Lambda invocations.
                    cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                trace.AddException(ex);
                throw;
            }
        }
    }

    private LambdaMessageProcessorConfiguration CreateLambdaMessageProcessorConfiguration(SQSEvent sqsEvent, bool useBatchResponse, ITelemetryTrace trace, LambdaMessagingOptions? options = null)
    {
        options ??= new LambdaMessagingOptions();

        options.Validate();
        var sqsQueueArn = sqsEvent.Records[0].EventSourceArn;
        var sqsQueueUrl = GetSQSQueueUrl(sqsQueueArn);

        trace.AddMetadata(TelemetryKeys.QueueUrl, sqsQueueUrl);

        var lambdaMessageProcessorConfiguration = new LambdaMessageProcessorConfiguration(sqsQueueUrl)
        {
            SQSEvent = sqsEvent,
            UseBatchResponse = useBatchResponse,
            VisibilityTimeoutForBatchFailures = options?.VisibilityTimeoutForBatchFailures,
            DeleteMessagesWhenCompleted = options?.DeleteMessagesWhenCompleted ?? false,
            // TODO: This value should be set differently when working with FIFO queues.
            MaxNumberOfConcurrentMessages = options?.MaxNumberOfConcurrentMessages ?? LambdaMessagingOptions.DEFAULT_MAX_NUMBER_OF_CONCURRENT_MESSAGES
        };

        return lambdaMessageProcessorConfiguration;
    }

    private string GetSQSQueueUrl(string queueArn)
    {
        Arn arn;
        if (!Arn.TryParse(queueArn, out arn))
        {
            _logger.LogError("{QueueArn} is not a valid SQS queue ARN", queueArn);
            throw new InvalidSQSQueueArnException($"{queueArn} is not a valid SQS queue ARN");
        }

        // TODO: Figure out the dnsSuffix from AWSSDK.Core's partition metadata
        var queueUrl = $"https://sqs.{arn.Region}.amazonaws.com/{arn.AccountId}/{arn.Resource}";
        return queueUrl;
    }
}

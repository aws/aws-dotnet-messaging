// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0
using Amazon.Lambda.SQSEvents;

namespace AWS.Messaging.Lambda.Services;

/// <summary>
/// Configuration for the <see cref="ILambdaMessageProcessor"/> to use for processing the <see cref="Amazon.Lambda.SQSEvents.SQSEvent"/> coming for the Lambda service.
/// </summary>
public class LambdaMessageProcessorConfiguration
{
    /// <summary>
    /// The maximum number of messages from the SQS event batch to process concurrently.
    /// </summary>
    /// <remarks>The default value is 10.</remarks>
    public int MaxNumberOfConcurrentMessages { get; init; } = 10;

    /// <summary>
    /// If true when a message has been successfully processed delete the message from the SQS queue. When not set
    /// to false the messages will be deleted by the Lambda service if all of the messages in the were successfully processed
    /// and the Lambda function returned no exceptions.
    ///
    /// For Lambda functions that are configured for partial failure and return an <see cref="Amazon.Lambda.SQSEvents.SQSBatchResponse"/> this property is ignored.
    /// </summary>
    public bool DeleteMessagesWhenCompleted { get; init; } = false;

    /// <summary>
    /// Indicates whether the SQS event source mapping is configured to use <see href="https://docs.aws.amazon.com/lambda/latest/dg/with-sqs.html#services-sqs-batchfailurereporting">partial batch responses.</see>
    /// </summary>
    /// <remarks>The default value is false.</remarks>
    public bool UseBatchResponse { get; init; } = false;

    /// <summary>
    /// How many seconds to set the VisibilityTimeout value to on partial batch failures.
    ///
    /// This is only applicable if <see cref="AWS.Messaging.Lambda.Services.LambdaMessageProcessorConfiguration.UseBatchResponse"/> is true.
    /// </summary>
    public int? VisibilityTimeoutForBatchFailures { get; init; }

    /// <summary>
    /// The SQS event that will be processed by the Lambda function.
    /// </summary>
    public SQSEvent? SQSEvent { get; init; }

    /// <summary>
    /// The SQS queue which acts as an event trigger for the Lambda function.
    /// </summary>
    public string SubscriberEndpoint { get; init; }

    /// <summary>
    /// Creates an instance of <see cref="LambdaMessageProcessorConfiguration"/>
    /// </summary>
    /// <param name="queueUrl">The SQS queue which acts as an event trigger for the Lambda function.</param>
    public LambdaMessageProcessorConfiguration(string queueUrl)
    {
        SubscriberEndpoint = queueUrl;
    }
}

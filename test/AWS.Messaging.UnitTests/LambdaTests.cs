// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Lambda;
using AWS.Messaging.Serialization;
using AWS.Messaging.UnitTests.Models;
using AWS.Messaging.Lambda.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using static Amazon.Lambda.SQSEvents.SQSEvent;
using Amazon;

namespace AWS.Messaging.UnitTests;

public class LambdaTests
{
    Mock<IAmazonSQS>? _mockSqs;
    IServiceProvider? _serviceProvider;

    [Fact]
    public async Task OneSuccessfulMessage()
    {
        var successMessage = new SimulatedMessage
        {
            Id = "success-message-1"
        };

        var log = await Execute(new SimulatedMessage[] { successMessage });

        Assert.Contains(successMessage.Id, log.ToString());
    }

    [Fact]
    public async Task MessageHandlerReturnsFailedStatus()
    {
        var failedMessage = new SimulatedMessage
        {
            Id = "failed-message-1",
            ReturnFailedStatus = true
        };

        await Assert.ThrowsAsync<LambdaInvocationFailureException>(async () =>  await Execute(new SimulatedMessage[] { failedMessage }));
    }

    [Fact]
    public async Task ConcurrencyOne()
    {
        var slowMessage = new SimulatedMessage
        {
            Id = "slow-message",
            WaitTime = TimeSpan.FromMilliseconds(200)
        };

        var fastMessage = new SimulatedMessage
        {
            Id = "fast-message"
        };

        var log = await Execute(new SimulatedMessage[] { slowMessage, fastMessage }, maxNumberOfConcurrentMessages: 1);

        // Since only one message was done at a time the fast message should have its finish log message later in the
        // log because it wasn't able to jump ahead of the slow message
        var finishSlowMessagePos = FindFinishPos(log, slowMessage);
        var finishFastMessagePos = FindFinishPos(log, fastMessage);
        Assert.True(finishSlowMessagePos < finishFastMessagePos);
    }

    [Fact]
    public async Task ConcurrencyTwo()
    {
        var slowMessage = new SimulatedMessage
        {
            Id = "slow-message",
            WaitTime = TimeSpan.FromMilliseconds(200)
        };

        var fastMessage = new SimulatedMessage
        {
            Id = "fast-message"
        };

        var log = await Execute(new SimulatedMessage[] { slowMessage, fastMessage }, maxNumberOfConcurrentMessages: 2);

        // Since both messages ran in parallel the second message should be done first because
        // it runs faster. This is checked by checking where in the log the finish message is at.
        var finishSlowMessagePos = FindFinishPos(log, slowMessage);
        var finishFastMessagePos = FindFinishPos(log, fastMessage);
        Assert.True(finishFastMessagePos < finishSlowMessagePos);
    }

    [Fact]
    public async Task MessageHandlerReturnsFailedStatusBatchResponse()
    {
        var failedMessage = new SimulatedMessage
        {
            Id = "failed-message-1",
            ReturnFailedStatus = true
        };
        var secondMessage = new SimulatedMessage
        {
            Id = "second-message-1",
            ReturnFailedStatus = false
        };

        var tuple = await ExecuteWithBatchResponse(new SimulatedMessage[] { failedMessage, secondMessage });
        _mockSqs!.Verify(
            expression: x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()),
            times: Times.Never);
        Assert.Single(tuple.batchResponse.BatchItemFailures);
        Assert.Equal("1", tuple.batchResponse.BatchItemFailures[0].ItemIdentifier);
    }

    [Fact]
    public async Task MessageHandlerReturnsFailedStatusBatchResponseFifo()
    {
        var failedMessage = new SimulatedMessage
        {            
            Id = "failed-message-1",
            ReturnFailedStatus = true,
            MessageGroupId = "group1"
        };
        var secondMessage = new SimulatedMessage
        {
            Id = "second-message-1",
            ReturnFailedStatus = false,
            MessageGroupId = "group1"
        };

        var tuple = await ExecuteWithBatchResponse(new SimulatedMessage[] { failedMessage, secondMessage }, isFifoQueue: true);
        _mockSqs!.Verify(
            expression: x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()),
            times: Times.Never);
        Assert.Equal(2, tuple.batchResponse.BatchItemFailures.Count);
        Assert.Equal("1", tuple.batchResponse.BatchItemFailures[0].ItemIdentifier);
    }

    [Fact]
    public async Task MessageHandlerResetsVisibilityWhenFailedStatusBatchResponse()
    {
        var failedMessage = new SimulatedMessage
        {
            Id = "failed-message-1",
            ReturnFailedStatus = true
        };

        var tuple = await ExecuteWithBatchResponse(new SimulatedMessage[] { failedMessage }, visibilityTimeoutForBatchFailures: 10);
        _mockSqs!.Verify(
            expression: x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()),
            times: Times.Once);
        Assert.Single(tuple.batchResponse.BatchItemFailures);
        Assert.Equal("1", tuple.batchResponse.BatchItemFailures[0].ItemIdentifier);
    }

    [Fact]
    public async Task MessageHandlerResetsVisibilityBatchWhenFailedStatusBatchResponse()
    {
        var failedMessage1 = new SimulatedMessage
        {
            Id = "failed-message-1",
            ReturnFailedStatus = true
        };
        var failedMessage2 = new SimulatedMessage
        {
            Id = "failed-message-2",
            ReturnFailedStatus = true
        };

        var tuple = await ExecuteWithBatchResponse(new SimulatedMessage[] { failedMessage1, failedMessage2 }, visibilityTimeoutForBatchFailures: 10);
        _mockSqs!.Verify(
            expression: x => x.ChangeMessageVisibilityAsync(It.IsAny<ChangeMessageVisibilityRequest>(), It.IsAny<CancellationToken>()),
            times: Times.Never);
        _mockSqs!.Verify(
            expression: x => x.ChangeMessageVisibilityBatchAsync(It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()),
            times: Times.Once);
    }

    [Fact]
    public async Task DeleteMessagesAsWeGo()
    {
        var message1 = new SimulatedMessage
        {
            Id = "success-1",
        };

        var message2 = new SimulatedMessage
        {
            Id = "success-2",
        };

        var message3 = new SimulatedMessage
        {
            Id = "success-3",
        };

        await Execute(new SimulatedMessage[] { message1, message2, message3 }, deleteMessagesWhenCompleted: true);
        _mockSqs!.VerifyDeleteMessageBatchAsyncWasCalled(Times.Exactly(3));

        await Execute(new SimulatedMessage[] { message1, message2, message3 }, deleteMessagesWhenCompleted: false);
        _mockSqs!.VerifyDeleteMessageBatchAsyncWasCalled(Times.Never());
    }

    [Fact]
    public async Task MakeSureDeleteIsNotCalledWhenUsingPartialFailure()
    {
        var message1 = new SimulatedMessage
        {
            Id = "success-1",
        };

        var message2 = new SimulatedMessage
        {
            Id = "success-2",
        };

        var message3 = new SimulatedMessage
        {
            Id = "success-3",
        };

        await ExecuteWithBatchResponse(new SimulatedMessage[] { message1, message2, message3 }, deleteMessagesWhenCompleted: false);
        _mockSqs!.VerifyDeleteMessageBatchAsyncWasCalled(Times.Never());

        // deleteMessagesWhenCompleted should be ignored on the options class when using partial failure.
        await ExecuteWithBatchResponse(new SimulatedMessage[] { message1, message2, message3 }, deleteMessagesWhenCompleted: true);
        _mockSqs!.VerifyDeleteMessageBatchAsyncWasCalled(Times.Never());
    }

    [Fact]
    public async Task InvalidMessageFormatTriggersInvocationFailiure()
    {
        await Assert.ThrowsAsync<FailedToCreateMessageEnvelopeException>(async () => await Execute(new SimulatedMessage[] { }, addInvalidMessageFormatRecord: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task FifoMessageHandling_SingleMessageGroup(int maxNumberOfConcurrentMessages)
    {
        // create messages that belong to the same group with processing times as message3 < message2 < message1
        var message1 = new SimulatedMessage { Id = "1", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(300) };
        var message2 = new SimulatedMessage { Id = "2", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(200) };
        var message3 = new SimulatedMessage { Id = "3", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(100) };

        var log = await Execute(new SimulatedMessage[] { message1, message2, message3 },
            maxNumberOfConcurrentMessages: maxNumberOfConcurrentMessages, isFifoQueue: true);

        var messageStorage = _serviceProvider!.GetRequiredService<TempStorage<SimulatedMessage>>();

        // Since all messages belong to the same group and we are polling from a FIFO queue,
        // the message processing must also respect the FIFO ordering.
        var message1Pos = FindFinishFifoPos(messageStorage, message1);
        var message2Pos = FindFinishFifoPos(messageStorage, message2);
        var message3Pos = FindFinishFifoPos(messageStorage, message3);

        Assert.True(message1Pos != -1);
        Assert.True(message2Pos != -1);
        Assert.True(message3Pos != -1);
        Assert.True(message1Pos < message2Pos);
        Assert.True(message2Pos < message3Pos);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task FifoMessageHandling_MultipleMessageGroups(int maxNumberOfConcurrentMessages)
    {
        // create messages that belong to group "A" with processing times as message3A < message2A < message1A
        var message1A = new SimulatedMessage { Id = "1", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(300) };
        var message2A = new SimulatedMessage { Id = "2", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(200) };
        var message3A = new SimulatedMessage { Id = "3", MessageGroupId = "A", WaitTime = TimeSpan.FromMilliseconds(100) };

        // create messages that belong to group "B" with processing times as message6B < message5B < message4B
        var message4B = new SimulatedMessage { Id = "4", MessageGroupId = "B", WaitTime = TimeSpan.FromMilliseconds(300) };
        var message5B = new SimulatedMessage { Id = "5", MessageGroupId = "B", WaitTime = TimeSpan.FromMilliseconds(200) };
        var message6B = new SimulatedMessage { Id = "6", MessageGroupId = "B", WaitTime = TimeSpan.FromMilliseconds(100) };

        // send messages with interleaved message groups
        await Execute(new SimulatedMessage[] { message4B, message1A, message2A, message5B, message3A, message6B },
            maxNumberOfConcurrentMessages: maxNumberOfConcurrentMessages, isFifoQueue: true);

        var messageStorage = _serviceProvider!.GetRequiredService<TempStorage<SimulatedMessage>>();

        // Messages belonging to the same group must be processed in FIFO order.
        var message1APos = FindFinishFifoPos(messageStorage, message1A);
        var message2APos = FindFinishFifoPos(messageStorage, message2A);
        var message3APos = FindFinishFifoPos(messageStorage, message3A);

        Assert.True(message1APos != -1);
        Assert.True(message2APos != -1);
        Assert.True(message3APos != -1);
        Assert.True(message1APos < message2APos);
        Assert.True(message2APos < message3APos);

        var message4BPos = FindFinishFifoPos(messageStorage, message4B);
        var message5BPos = FindFinishFifoPos(messageStorage, message5B);
        var message6BPos = FindFinishFifoPos(messageStorage, message6B);

        Assert.True(message4BPos != -1);
        Assert.True(message5BPos != -1);
        Assert.True(message6BPos != -1);
        Assert.True(message4BPos < message5BPos);
        Assert.True(message5BPos < message6BPos);
    }

    [Fact]
    public void ConvertToSDKMessage()
    {
        var lambdaMessage = new SQSEvent.SQSMessage
        {
            Attributes = new Dictionary<string, string> { { "key", "value" } },
            Body = "body",
            Md5OfMessageAttributes = "md5Attributes",
            MessageId = "messageId",
            ReceiptHandle = "handle"
        };

        var sdkMessage = DefaultLambdaMessageProcessor.ConvertToStandardSQSMessage(lambdaMessage);

        Assert.Equal(lambdaMessage.Body, sdkMessage.Body);
        Assert.Equal(lambdaMessage.Md5OfMessageAttributes, sdkMessage.MD5OfMessageAttributes);
        Assert.Equal(lambdaMessage.MessageId, sdkMessage.MessageId);
        Assert.Equal(lambdaMessage.ReceiptHandle, sdkMessage.ReceiptHandle);
        Assert.True(object.ReferenceEquals(lambdaMessage.Attributes, sdkMessage.Attributes));

        if (AWSConfigs.InitializeCollections)
        {
            Assert.Empty(sdkMessage.MessageAttributes);
        }
        else
        {
            Assert.Null(sdkMessage.MessageAttributes);
        }

        lambdaMessage.MessageAttributes = new Dictionary<string, MessageAttribute>
        {
            {"keyString", new MessageAttribute
                {
                    DataType = "String",
                    StringValue = "TheString"
                }
            },
            {"keyBinary", new MessageAttribute
                {
                    DataType = "Binary",
                    BinaryValue = new System.IO.MemoryStream(new byte[]{1,2,3})
                }
            }
        };

        sdkMessage = DefaultLambdaMessageProcessor.ConvertToStandardSQSMessage(lambdaMessage);


        Assert.Equal(lambdaMessage.Body, sdkMessage.Body);
        Assert.Equal(lambdaMessage.Md5OfMessageAttributes, sdkMessage.MD5OfMessageAttributes);
        Assert.Equal(lambdaMessage.MessageId, sdkMessage.MessageId);
        Assert.Equal(lambdaMessage.ReceiptHandle, sdkMessage.ReceiptHandle);
        Assert.True(object.ReferenceEquals(lambdaMessage.Attributes, sdkMessage.Attributes));

        Assert.Equal(2, sdkMessage.MessageAttributes.Count);
        Assert.Equal("Binary", sdkMessage.MessageAttributes["keyBinary"].DataType);
        Assert.Equal(3, sdkMessage.MessageAttributes["keyBinary"].BinaryValue.Length);
    }

    private async Task<string> Execute(SimulatedMessage[] messages, int maxNumberOfConcurrentMessages = 1, bool deleteMessagesWhenCompleted = false, bool addInvalidMessageFormatRecord = false, bool isFifoQueue = false)
    {
        var provider = CreateServiceProvider(maxNumberOfConcurrentMessages: maxNumberOfConcurrentMessages, deleteMessagesWhenCompleted: deleteMessagesWhenCompleted, isFifoQueue: isFifoQueue);
        var sqsEvent = await CreateLambdaEvent(provider, messages, addInvalidMessageFormatRecord: addInvalidMessageFormatRecord, isFifoQueue: isFifoQueue);

        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext()
        {
            Logger = logger
        };
        var lambdaMessaging = provider.GetRequiredService<ILambdaMessaging>();
        var lambdaFunction = new SimulatedLambdaFunction(lambdaMessaging);
        await lambdaFunction.FunctionHandler(sqsEvent, context);

        return logger.Buffer.ToString();
    }

    private async Task<(string log, SQSBatchResponse batchResponse)> ExecuteWithBatchResponse(
        SimulatedMessage[] messages,
        int maxNumberOfConcurrentMessages = 1,
        bool deleteMessagesWhenCompleted = false,
        int? visibilityTimeoutForBatchFailures = default,
        bool isFifoQueue = false)
    {
        var provider = CreateServiceProvider(
            maxNumberOfConcurrentMessages: maxNumberOfConcurrentMessages,
            deleteMessagesWhenCompleted: deleteMessagesWhenCompleted,
            visibilityTimeoutForBatchFailures: visibilityTimeoutForBatchFailures, isFifoQueue: isFifoQueue);
        var sqsEvent = await CreateLambdaEvent(provider, messages, isFifoQueue: isFifoQueue);

        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext()
        {
            Logger = logger
        };
        var lambdaMessaging = provider.GetRequiredService<ILambdaMessaging>();
        var lambdaFunction = new SimulatedLambdaFunction(lambdaMessaging);
        var batchResponse = await lambdaFunction.FunctionHandlerWithBatchResponse(sqsEvent, context);

        return (logger.Buffer.ToString(), batchResponse);
    }

    private async Task<SQSEvent> CreateLambdaEvent(IServiceProvider provider,  SimulatedMessage[] messages, bool addInvalidMessageFormatRecord = false, bool isFifoQueue = false)
    {
        var envelopeSerializer = provider.GetRequiredService<IEnvelopeSerializer>();

        var sqsEvent = new SQSEvent()
        {
            Records = new List<SQSMessage>()
        };

        var eventSourceArn = "arn:aws:sqs:us-west-2:123412341234:SimulatedMessage";
        if (isFifoQueue)
            eventSourceArn += ".fifo";

        if (addInvalidMessageFormatRecord)
        {
            var i = "-1";
            var sqsMessage = new SQSEvent.SQSMessage
            {
                Body = "This is not a valid message",

                MessageId = i.ToString(),
                ReceiptHandle = "fake-receipt-handle-" + i,
                AwsRegion = "us-west-2",
                EventSource = "aws:sqs",
                EventSourceArn = eventSourceArn,
                MessageAttributes = new Dictionary<string, MessageAttribute>(),
                Attributes = new Dictionary<string, string>()
            };
            sqsEvent.Records.Add(sqsMessage);
        }

        for (int i = 1; i <= messages.Length; i++)
        {
            var message = messages[i-1];
            var messageEnvelope = await envelopeSerializer.CreateEnvelopeAsync(message);
            var messageBody = await envelopeSerializer.SerializeAsync(messageEnvelope);

            var sqsMessage = new SQSEvent.SQSMessage
            {
                Body = messageBody,

                MessageId = i.ToString(),
                ReceiptHandle = "fake-receipt-handle-" + i,
                AwsRegion = "us-west-2",
                EventSource = "aws:sqs",
                EventSourceArn = eventSourceArn,
                MessageAttributes = new Dictionary<string, MessageAttribute>(),
                Attributes = new Dictionary<string, string>()
            };

            if (!string.IsNullOrEmpty(message.MessageGroupId))
                sqsMessage.Attributes.Add("MessageGroupId", message.MessageGroupId);

            sqsEvent.Records.Add(sqsMessage);
        }

        return sqsEvent;
    }

    private IServiceProvider CreateServiceProvider(
        int maxNumberOfConcurrentMessages = 1,
        bool deleteMessagesWhenCompleted = false,
        bool isFifoQueue = false,
        int? visibilityTimeoutForBatchFailures = default)
    {
        _mockSqs = new Mock<IAmazonSQS>();

        _mockSqs.Setup(x => x.DeleteMessageBatchAsync(
                It.IsAny<DeleteMessageBatchRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new DeleteMessageBatchResponse()));

        IServiceCollection services = new ServiceCollection();

        services.AddSingleton(_mockSqs.Object);
        services.AddSingleton<TempStorage<SimulatedMessage>>();

        services.AddAWSMessageBus(builder =>
        {
            var sqsQueueUrl = "https://sqs.us-west-2.amazonaws.com/123412341234/SimulatedMessage";
            if (isFifoQueue)
                sqsQueueUrl += ".fifo";

            builder.AddSQSPublisher<SimulatedMessage>(sqsQueueUrl);
            builder.AddMessageHandler<SimulatedMessageHandler, SimulatedMessage>();

            builder.AddLambdaMessageProcessor(options =>
            {
                options.MaxNumberOfConcurrentMessages = maxNumberOfConcurrentMessages;
                options.DeleteMessagesWhenCompleted = deleteMessagesWhenCompleted;
                options.VisibilityTimeoutForBatchFailures = visibilityTimeoutForBatchFailures;
            });
        });

        var provider = services.BuildServiceProvider();
        _serviceProvider = provider;

        return provider;
    }

    private int FindFinishPos(string log, SimulatedMessage message)
    {
        var token = "Finished handler: " + message.Id;
        return log.IndexOf(token);
    }

    private int FindFinishFifoPos(TempStorage<SimulatedMessage> tempStorage, SimulatedMessage message)
    {
        for (var i = 0; i < tempStorage.FifoMessages.Count; i++)
        {
            var id = tempStorage.FifoMessages.ElementAt(i).Message.Id;
            if (id == message.Id)
            {
                return i;
            }
        }

        return -1;
    }
}

public class SimulatedLambdaFunction
{
    private readonly ILambdaMessaging _lambdaMessaging;

    public SimulatedLambdaFunction(ILambdaMessaging lambdaMessaging)
    {
        _lambdaMessaging = lambdaMessaging;
    }

    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        await _lambdaMessaging.ProcessLambdaEventAsync(evnt, context);
    }

    public async Task<SQSBatchResponse> FunctionHandlerWithBatchResponse(SQSEvent evnt, ILambdaContext context)
    {
        return await _lambdaMessaging.ProcessLambdaEventWithBatchResponseAsync(evnt, context);
    }
}

public class SimulatedMessage
{
    public string? Id { get; set; }

    public bool ReturnFailedStatus { get; set; } = false;

    public TimeSpan WaitTime { get; set; }

    public string? MessageGroupId { get; set; }
}

public class SimulatedMessageHandler : IMessageHandler<SimulatedMessage>
{
    // TestLambdaLogger.Buffer is a plain StringBuilder — not thread-safe.
    // Multiple message groups may run concurrently (FIFO multi-group tests), so log
    // writes must be serialized to prevent corrupted state / spurious exceptions.
    private static readonly object _logLock = new();

    public readonly ILambdaContext _lambdaContext;
    public readonly TempStorage<SimulatedMessage> _messageStorage;

    public SimulatedMessageHandler(ILambdaContext lambdaContext, TempStorage<SimulatedMessage> messageStorage)
    {
        _lambdaContext = lambdaContext;
        _messageStorage = messageStorage;
    }

    public async Task<MessageProcessStatus> HandleAsync(MessageEnvelope<SimulatedMessage> messageEnvelope, CancellationToken token = default)
    {
        var message = messageEnvelope.Message;
        try
        {
            lock (_logLock) { _lambdaContext.Logger.LogInformation("Message Id: " + message.Id); }

            await Task.Delay(message.WaitTime, token);
            if (message.ReturnFailedStatus)
            {
                return MessageProcessStatus.Failed();
            }

            return MessageProcessStatus.Success();
        }
        finally
        {
            lock (_logLock) { _lambdaContext.Logger.LogInformation("Finished handler: " + message.Id); }
            _messageStorage.FifoMessages.Enqueue(messageEnvelope);
        }
    }
}

public static class MockSQSClientExtensions
{
    public static void VerifyDeleteMessageBatchAsyncWasCalled(this Mock<IAmazonSQS> mockSQSClient, Times times)
    {
        mockSQSClient.Verify(x => x.DeleteMessageBatchAsync(
                It.IsAny<DeleteMessageBatchRequest>(),
                It.IsAny<CancellationToken>()),
            times);
    }
}

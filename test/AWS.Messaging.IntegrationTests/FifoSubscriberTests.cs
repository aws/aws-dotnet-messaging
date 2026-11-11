// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Services;
using AWS.Messaging.Tests.Common;
using AWS.Messaging.Tests.Common.Handlers;
using AWS.Messaging.Tests.Common.Models;
using AWS.Messaging.Tests.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AWS.Messaging.IntegrationTests;

public class FifoSubscriberTests : IAsyncLifetime
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IServiceCollection _serviceCollection;
    private string _sqsQueueUrl;

    public FifoSubscriberTests()
    {
        _sqsClient = new TestAwsBackend().CreateSqsClient();
        _serviceCollection = new ServiceCollection();
        _serviceCollection.AddLogging(x => x.AddInMemoryLogger().SetMinimumLevel(LogLevel.Trace));
        _serviceCollection.AddSingleton<IAmazonSQS>(_sqsClient);
        _sqsQueueUrl = string.Empty;
    }

    public async Task InitializeAsync()
    {
        _sqsQueueUrl = await AWSUtilities.CreateQueueAsync(_sqsClient, isFifo: true);
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(3, 3, 10)]
    [InlineData(5, 5, 5)]
    [InlineData(5, 10, 5)]
    public async Task SendAndReceiveMessages_MultipleMessageGroups(int numberOfGroups, int numberOfMessagesPerGroup, int maxConcurrentMessages)
    {
        _serviceCollection.AddSingleton<TempStorage<TransactionInfo>>();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_sqsQueueUrl);
            builder.AddSQSPoller(_sqsQueueUrl, options =>
            {
                options.MaxNumberOfConcurrentMessages = maxConcurrentMessages;
            });
            builder.AddMessageHandler<TransactionInfoHandler, TransactionInfo>();
            builder.AddMessageSource("/aws/messaging");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();
        var sqsPublisher = serviceProvider.GetRequiredService<ISQSPublisher>();

        var messageGroups = new List<string>();
        for (var i = 0; i < numberOfGroups; i++)
        {
            // Each userId is considered as a message group
            var userId = i.ToString();
            messageGroups.Add(i.ToString());
            await PublishTransactions(sqsPublisher, numberOfMessagesPerGroup, userId, addWaitTime: true);
        }

        var pump = serviceProvider.GetRequiredService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);
        var source = new CancellationTokenSource();
        await pump.StartAsync(source.Token);

        var transactionInfoStorage = serviceProvider.GetRequiredService<TempStorage<TransactionInfo>>();

        // Wait until every group's messages have been processed, with a ceiling to avoid hanging.
        // The per-group exact-count check in VerifyTransactions still catches any duplicate delivery.
        source.CancelAfter(numberOfGroups * numberOfMessagesPerGroup * 2000);
        await AsyncTestUtilities.WaitUntilAsync(
            () => messageGroups.All(g => transactionInfoStorage.FifoMessages.TryGetValue(g, out var list) && list.Count >= numberOfMessagesPerGroup),
            source.Token);
        source.Cancel();

        foreach (var messageGroup in messageGroups)
        {
            VerifyTransactions(numberOfMessagesPerGroup, transactionInfoStorage.FifoMessages[messageGroup]);
        }
    }

    [Theory]
    [InlineData(10)]
    public async Task MessagesWithFrameworkandWithout(int numberOfMessages)
    {
        _serviceCollection.AddSingleton<TempStorage<TransactionInfo>>();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_sqsQueueUrl);
            builder.AddSQSPoller(_sqsQueueUrl);
            builder.AddMessageHandler<TransactionInfoHandler, TransactionInfo>();
            builder.AddMessageSource("/aws/messaging");
        });

        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var sqsPublisher = serviceProvider.GetRequiredService<ISQSPublisher>();

        // send messages using the framework with registered handlers
        await PublishTransactions(sqsPublisher, numberOfMessages, userId: "A");

        // send messages without the framework.
        for (var i = 0; i < numberOfMessages; i++)
        {
            await _sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MessageBody = Guid.NewGuid().ToString(),
                MessageGroupId = "B"
            });
        }

        var pump = serviceProvider.GetRequiredService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);
        var source = new CancellationTokenSource();
        await pump.StartAsync(source.Token);
        source.CancelAfter((numberOfMessages + 1) * 3000);

        var transactionInfoStorage = serviceProvider.GetRequiredService<TempStorage<TransactionInfo>>();
        var inMemoryLogger = serviceProvider.GetRequiredService<InMemoryLogger>();
        await AsyncTestUtilities.WaitUntilAsync(
            () => transactionInfoStorage.FifoMessages.TryGetValue("A", out var list) && list.Count >= numberOfMessages
                && inMemoryLogger.Logs.Count(x => x.Message.Equals("Failed to create a MessageEnvelope")) >= numberOfMessages,
            source.Token);
        source.Cancel();

        VerifyTransactions(numberOfMessages, transactionInfoStorage.FifoMessages["A"]);

        var errorMessages = inMemoryLogger.Logs.Where(x => x.Message.Equals("Failed to create a MessageEnvelope"));
        Assert.NotEmpty(errorMessages);
        Assert.True(errorMessages.Count() >= numberOfMessages);
    }

    [Theory]
    [InlineData(5)]
    public async Task MessagesWithoutHandlers(int numberOfMessages)
    {
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_sqsQueueUrl);
            builder.AddSQSPoller(_sqsQueueUrl);
        });
        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var sqsPublisher = serviceProvider.GetRequiredService<ISQSPublisher>();
        await PublishTransactions(sqsPublisher, numberOfMessages, userId: "A");

        var pump = serviceProvider.GetRequiredService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);
        var source = new CancellationTokenSource();

        var processStartTime = DateTime.UtcNow;
        await pump.StartAsync(source.Token);

        source.CancelAfter(30000);
        await source.Token.WaitForCancellationAsync();
        var timeElapsed = DateTime.UtcNow - processStartTime;

        var inMemoryLogger = serviceProvider.GetRequiredService<InMemoryLogger>();
        var errorMessages = inMemoryLogger.Logs.Where(x => x.Message.StartsWith("'") && x.Message.Contains("is not a valid subscriber mapping"));
        Assert.NotEmpty(errorMessages);
        Assert.True(errorMessages.Count() >= numberOfMessages);
        Assert.True(timeElapsed.TotalSeconds > 29);
        Assert.True(source.IsCancellationRequested);
    }

    [Theory]
    [InlineData(5)]
    public async Task MessagesWithFailedHandlers(int numberOfMessages)
    {
        _serviceCollection.AddSingleton<TempStorage<TransactionInfo>>();
        _serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_sqsQueueUrl);
            builder.AddSQSPoller(_sqsQueueUrl);
            builder.AddMessageHandler<TransactionInfoHandler, TransactionInfo>();
            builder.AddMessageSource("/aws/messaging");
        });
        var serviceProvider = _serviceCollection.BuildServiceProvider();

        var sqsPublisher = serviceProvider.GetRequiredService<ISQSPublisher>();

        await PublishTransactions(sqsPublisher, numberOfMessages, userId: "A", shouldFail: true);

        var pump = serviceProvider.GetRequiredService<IHostedService>() as MessagePumpService;
        Assert.NotNull(pump);
        var source = new CancellationTokenSource();

        var processStartTime = DateTime.UtcNow;
        await pump.StartAsync(source.Token);

        source.CancelAfter(30000);
        await source.Token.WaitForCancellationAsync();
        var timeElapsed = DateTime.UtcNow - processStartTime;

        var inMemoryLogger = serviceProvider.GetRequiredService<InMemoryLogger>();

        var errorMessages = inMemoryLogger.Logs.Where(x => x.Message.Contains("Message handling completed unsuccessfully for message"));
        var skippingGroup = inMemoryLogger.Logs.Where(x => x.Message.Contains("Handler invocation failed for a message belonging to message group 'A'"));

        Assert.NotEmpty(errorMessages);
        Assert.NotEmpty(skippingGroup);
        Assert.True(timeElapsed.TotalSeconds > 29);
        Assert.True(source.IsCancellationRequested);
    }

    private async Task PublishTransactions(ISQSPublisher sqsPublisher, int numTransactions, string userId, bool addWaitTime = false, bool shouldFail = false)
    {
        var rnd = new Random();

        for (var i = 0; i < numTransactions; i++)
        {
            var transactionInfo = new TransactionInfo
            {
                UserId = userId,
                TransactionId = Guid.NewGuid().ToString(),
                PublishTimeStamp = DateTime.Now,
            };

            if (addWaitTime)
            {
                // Add a random delay between 0 to 1 seconds during the handler invocation.
                transactionInfo.WaitTime = TimeSpan.FromMilliseconds(rnd.Next(0, 1001));
            }

            if (shouldFail)
            {
                // The handler invocation for this message will fail.
                transactionInfo.ShouldFail = true;
            }

            await sqsPublisher.SendAsync(transactionInfo, new SQSOptions
            {
                MessageGroupId = userId
            });
        }
    }

    private void VerifyTransactions(int expectedNumberOfTransactions, List<MessageEnvelope<TransactionInfo>> fifoTransactions)
    {
        Assert.Equal(expectedNumberOfTransactions, fifoTransactions.Count);
        var previousTimeStamp = DateTime.MinValue;
        foreach (var transaction in fifoTransactions)
        {
            // Verify that the messages were processed in the order in which they were published.
            Assert.True(transaction.Message.PublishTimeStamp.CompareTo(previousTimeStamp) > 0);
            previousTimeStamp = transaction.Message.PublishTimeStamp;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _sqsClient.DeleteQueueAsync(_sqsQueueUrl);
        }
        catch { }
    }
}

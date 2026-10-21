// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Lambda;
using Amazon.S3;
using Amazon.SQS;
using AWS.Messaging.Publishers.SQS;
using AWS.Messaging.Tests.Common;
using AWS.Messaging.Tests.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AWS.Messaging.IntegrationTests;

// LambdaIntegrationTestCollection and LambdaIntegrationTestFixture contain
// shared setup that us used across ALL Lambda integration tests:
//   1. Locally built Lambda deployment bundle (with all code under test)
//   2. S3 bucket containing that deployment bundle
//   3. Lambda execution role
//
// Then each class in this file deploys a specific scenerio
// that we want to test:
//   1. Lambda function (specfic handler within the shared bundle)
//   2. SQS queue (and optional DLQ) configuration
//
// One or more tests within each class exercise that class's scenerio.
//
[CollectionDefinition("LambdaIntegrationTests")]
public class LambdaIntegrationTestCollection : ICollectionFixture<LambdaIntegrationTestFixture>
{
    // This class has no code, and is never created. It maps the [CollectionDefinition]
    // to the ICollectionFixture<> interface.
    // See https://xunit.net/docs/shared-context#collection-fixture
}

/// <summary>
/// Creates AWS resources that will be shared across all Lambda integration tests.
/// Currently this is the Lambda execution role and the artifact bucket (with the deployment zip uploaded)
/// </summary>
public class LambdaIntegrationTestFixture : IAsyncLifetime, IDisposable
{
    public string ExecutionRoleArn { get; set; } = string.Empty;

    public string ArtifactBucketName { get; set; } = string.Empty;

    /// <summary>
    /// This is both the name of the local package containing the Lambda function(s) under test,
    /// as well as the name of the deployed Lambda function
    /// </summary>
    public const string FunctionPackageName = "AWS.Messaging.Tests.LambdaFunctions";

    private const string TestBucketRoot = "mpf-lambda-artifacts-";
    private const string LambdaExecutionRoleName = "ExecutionRoleForMPFTestLambdas";

    private readonly IAmazonIdentityManagementService _iamClient;
    private readonly IAmazonS3 _s3Client;

    public LambdaIntegrationTestFixture()
    {
        var backend = new TestAwsBackend();
        _iamClient = backend.CreateIAMClient();
        _s3Client = backend.CreateS3Client();
    }

    public async Task InitializeAsync()
    {
        // Ensure Amazon.Lambda.Tools is installed, which will be used to deploy the test functions
        var toolsProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "dotnet",
                CreateNoWindow = true,
                Arguments = "tool install -g Amazon.Lambda.Tools"
            }
        };
        toolsProcess.Start();
        toolsProcess.WaitForExit();

        // It returns 1 when already installed https://github.com/dotnet/sdk/issues/9500
        if (toolsProcess.ExitCode != 0 && toolsProcess.ExitCode != 1)
        {
            Assert.Fail($"Failed to install Amazon.Lambda.Tools");
        }

        // Package the project containing the functions that are under test (individual tests will deploy functions as needed)
        var path = Path.Combine(TestUtilities.FindParentDirectoryWithName(Environment.CurrentDirectory, "test"), FunctionPackageName);
        var buildProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "dotnet",
                CreateNoWindow = true,
                // Package for the host's architecture so the function runs both on real AWS and in a
                // local emulator (e.g. floci), which executes the function in a container matching the host CPU.
                Arguments = $"lambda package -c Release --function-architecture {AWSUtilities.HostLambdaArchitecture}",
                WorkingDirectory = path
            }
        };
        buildProcess.Start();
        buildProcess.WaitForExit();

        if (buildProcess.ExitCode != 0)
        {
            Assert.Fail($"Failed to package the Lambda functions under test at {path}");
        }

        // Create the execution IAM role for the function
        ExecutionRoleArn = await AWSUtilities.CreateFunctionRoleIfNotExists(_iamClient, LambdaExecutionRoleName);

        // Create the S3 bucket used to deploy the functions
        ArtifactBucketName = TestBucketRoot + Guid.NewGuid().ToString();
        await AWSUtilities.CreateBucketWithDeploymentZipAsync(_s3Client, ArtifactBucketName, FunctionPackageName);
    }

    public async Task DisposeAsync()
    {
        // Delete the S3 bucket
        if (!string.IsNullOrEmpty(ArtifactBucketName))
        {
            await AWSUtilities.DeleteDeploymentZipAndBucketAsync(_s3Client, ArtifactBucketName);
        }

        // Delete the Lambda execution role, which requires detaching the policy first
        await _iamClient.DetachRolePolicyAsync(new DetachRolePolicyRequest
        {
            RoleName = LambdaExecutionRoleName,
            PolicyArn = AWSUtilities.LambdaManagedPolicyArn
        });

        await _iamClient.DeleteRoleAsync(new DeleteRoleRequest { RoleName = LambdaExecutionRoleName });
    }

    public void Dispose()
    {
        return;
    }
}

/// <summary>
/// Tests for a Lambda function that uses the ProcessLambdaEventAsync handler
/// </summary>
[Collection("LambdaIntegrationTests")]
public class LambdaEventTests : IAsyncLifetime
{
    private readonly LambdaIntegrationTestFixture _fixture;
    private readonly IAmazonLambda _lambdaClient;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonCloudWatchLogs _cloudWatchLogsClient;
    private IMessagePublisher? _publisher;

    private string _queueUrl = "";
    private string _dlqUrl = "";

    public LambdaEventTests(LambdaIntegrationTestFixture fixture)
    {
        _fixture = fixture;

        var backend = new TestAwsBackend();
        _lambdaClient = backend.CreateLambdaClient();
        _sqsClient = backend.CreateSqsClient();
        _cloudWatchLogsClient = backend.CreateCloudWatchLogsClient();
    }

    public async Task InitializeAsync()
    {

        // Create the function
        await AWSUtilities.CreateFunctionAsync(_lambdaClient, _fixture.ArtifactBucketName, LambdaIntegrationTestFixture.FunctionPackageName, "LambdaEventHandler", _fixture.ExecutionRoleArn, 3);

        // Create the queue and DLQ and map it to the function
        (_queueUrl, _dlqUrl) = await AWSUtilities.CreateQueueWithDLQAsync(_sqsClient, messageVisibilityTimeout: 3);
        await AWSUtilities.CreateQueueLambdaMapping(_sqsClient, _lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName, _queueUrl);

        // Create the publisher
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<IAmazonSQS>(_sqsClient);
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_queueUrl, "TransactionInfo");
        });

        _publisher = serviceCollection.BuildServiceProvider().GetRequiredService<IMessagePublisher>();
    }

    public async Task DisposeAsync()
    {
        // Delete the function
        await AWSUtilities.DeleteFunctionIfExistsAsync(_lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName);

        // Delete the queues
        await _sqsClient.DeleteQueueAsync(_queueUrl);
        await _sqsClient.DeleteQueueAsync(_dlqUrl);
    }

    /// <summary>
    /// Happy path test for successful message
    /// </summary>
    [AWSFact]
    public async Task ProcessLambdaEventAsync_Success()
    {
        var message = new TransactionInfo
        {
            TransactionId = $"test-{Guid.NewGuid()}"
        };

        var publishTimestamp = DateTime.UtcNow;
        await _publisher!.PublishAsync(message);

        var logsWithHandler = await AWSUtilities.PollForLogWithMessage(_cloudWatchLogsClient, LambdaIntegrationTestFixture.FunctionPackageName, "Processed message with Id: ", publishTimestamp);

        Assert.Single(logsWithHandler);
        Assert.Contains($"Processed message with Id: {message.TransactionId}", logsWithHandler.First().Message);
    }

    /// <summary>
    /// Tests that a message that the Lambda handler fails for is moved to the DLQ appropriately
    /// </summary>
    [AWSFact]
    public async Task ProcessLambdaEventAsync_Failure()
    {
        var message = new TransactionInfo
        {
            TransactionId = $"test-{Guid.NewGuid()}",
            ShouldFail = true
        };

        var publishTimestamp = DateTime.UtcNow;
        await _publisher!.PublishAsync(message);

        await Task.Delay(TimeSpan.FromSeconds(10));

        // Assert that the message was not processed, since it's expected to fail
        var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, LambdaIntegrationTestFixture.FunctionPackageName, publishTimestamp);
        var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
        Assert.Empty(logsWithHandler);

        // Assert that the message was moved to the DLQ
        var receiveResponse = await _sqsClient.ReceiveMessageAsync(_dlqUrl);
        Assert.Single(receiveResponse.Messages);
        Assert.Contains(message.TransactionId, receiveResponse.Messages[0].Body);
    }
}

/// <summary>
/// Tests for a Lambda function that uses our ProcessLambdaEventWithBatchResponseAsync handler
/// </summary>
[Collection("LambdaIntegrationTests")]
public class LambdaBatchTests : IAsyncLifetime
{
    private readonly LambdaIntegrationTestFixture _fixture;
    private readonly IAmazonLambda _lambdaClient;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonCloudWatchLogs _cloudWatchLogsClient;
    private IMessagePublisher? _publisher;

    private string _queueUrl = "";
    private string _dlqUrl = "";

    public LambdaBatchTests(LambdaIntegrationTestFixture fixture)
    {
        _fixture = fixture;

        var backend = new TestAwsBackend();
        _lambdaClient = backend.CreateLambdaClient();
        _sqsClient = backend.CreateSqsClient();
        _cloudWatchLogsClient = backend.CreateCloudWatchLogsClient();
    }

    public async Task InitializeAsync()
    {
        // Create the function
        await AWSUtilities.CreateFunctionAsync(_lambdaClient, _fixture.ArtifactBucketName, LambdaIntegrationTestFixture.FunctionPackageName, "LambdaEventWithBatchResponseHandler", _fixture.ExecutionRoleArn, 3);

        // Create the queue and DLQ and map it to the function
        (_queueUrl, _dlqUrl) = await AWSUtilities.CreateQueueWithDLQAsync(_sqsClient, messageVisibilityTimeout: 3);
        await AWSUtilities.CreateQueueLambdaMapping(_sqsClient, _lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName, _queueUrl, true);

        // Create the publisher
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<IAmazonSQS>(_sqsClient);
        serviceCollection.AddAWSMessageBus(builder =>
        {
            builder.AddSQSPublisher<TransactionInfo>(_queueUrl, "TransactionInfo");
        });

        _publisher = serviceCollection.BuildServiceProvider().GetRequiredService<IMessagePublisher>();
    }

    public async Task DisposeAsync()
    {
        // Delete the function
        await AWSUtilities.DeleteFunctionIfExistsAsync(_lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName);

        // Delete the queue
        await _sqsClient.DeleteQueueAsync(_queueUrl);
        await _sqsClient.DeleteQueueAsync(_dlqUrl);
    }

    /// <summary>
    /// Happy path test for successful message
    /// </summary>
    [AWSFact]
    public async Task ProcessLambdaEventAsync_Success()
    {
        var message = new TransactionInfo
        {
            TransactionId = $"test-{Guid.NewGuid()}"
        };

        var publishTimestamp = DateTime.UtcNow;
        await _publisher!.PublishAsync(message);

        var logsWithHandler = await AWSUtilities.PollForLogWithMessage(_cloudWatchLogsClient, LambdaIntegrationTestFixture.FunctionPackageName, "Processed message with Id: ", publishTimestamp);

        Assert.Single(logsWithHandler);
        Assert.Contains($"Processed message with Id: {message.TransactionId}", logsWithHandler.First().Message);
    }

    /// <summary>
    /// Tests that a message that the Lambda handler fails for is moved to the DLQ appropriately
    /// </summary>
    [AWSFact]
    public async Task ProcessLambdaEventAsync_Failure()
    {
        var message = new TransactionInfo
        {
            TransactionId = $"test-{Guid.NewGuid()}",
            ShouldFail = true
        };

        var publishTimestamp = DateTime.UtcNow;
        await _publisher!.PublishAsync(message);

        await Task.Delay(TimeSpan.FromSeconds(10));

        // Assert that the message was not processed, since it's expected to fail
        var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, LambdaIntegrationTestFixture.FunctionPackageName, publishTimestamp);
        var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
        Assert.Empty(logsWithHandler);

        // Assert that the message was moved to the DLQ
        var receiveResponse = await _sqsClient.ReceiveMessageAsync(_dlqUrl);
        Assert.Single(receiveResponse.Messages);
        Assert.Contains(message.TransactionId, receiveResponse.Messages[0].Body);
    }
}

/// <summary>
/// Tests for a Lambda function that uses our ProcessLambdaEventWithBatchResponseAsync handler
/// </summary>
[Collection("LambdaIntegrationTests")]
public class LambdaFifoTests : IAsyncLifetime
{
    private readonly LambdaIntegrationTestFixture _fixture;
    private readonly IAmazonLambda _lambdaClient;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonCloudWatchLogs _cloudWatchLogsClient;
    private ISQSPublisher? _publisher;
    private string _queueUrl = "";

    public LambdaFifoTests(LambdaIntegrationTestFixture fixture)
    {
        _fixture = fixture;

        var backend = new TestAwsBackend();
        _lambdaClient = backend.CreateLambdaClient();
        _sqsClient = backend.CreateSqsClient();
        _cloudWatchLogsClient = backend.CreateCloudWatchLogsClient();
    }

    public async Task InitializeAsync()
    {
        // Create the function
        await AWSUtilities.CreateFunctionAsync(_lambdaClient, _fixture.ArtifactBucketName, LambdaIntegrationTestFixture.FunctionPackageName, "LambdaEventWithBatchResponseHandler", _fixture.ExecutionRoleArn, 3);

        // Create the queue and DLQ and map it to the function
        _queueUrl = await AWSUtilities.CreateQueueAsync(_sqsClient, isFifo: true);

        await AWSUtilities.CreateQueueLambdaMapping(_sqsClient, _lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName, _queueUrl, true);

        // Create the publisher
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAmazonSQS>(_sqsClient)
            .AddSingleton<TempStorage<TransactionInfo>>()
            .AddAWSMessageBus(builder =>
            {
                builder.AddSQSPublisher<TransactionInfo>(_queueUrl, "TransactionInfo");
            })
            .BuildServiceProvider();

        _publisher = serviceProvider.GetRequiredService<ISQSPublisher>();
    }

    public async Task DisposeAsync()
    {
        // Delete the function
        await AWSUtilities.DeleteFunctionIfExistsAsync(_lambdaClient, LambdaIntegrationTestFixture.FunctionPackageName);

        // Delete the queue
        await _sqsClient.DeleteQueueAsync(_queueUrl);
    }

    /// <summary>
    /// Asserts that when handling messages from a FIFO queue in Lambda that they are
    /// handled in the correct order
    /// </summary>
    [AWSTheory]
    [InlineData(1, 10)]
    [InlineData(3, 5)]
    public async Task ProcessFifoLambdaEventsAsync_Success(int numberOfGroups, int numberOfMessagesPerGroup)
    {
        var publishTimestamp = DateTime.UtcNow;
        var expectedMessagesPerGroup = new Dictionary<string, List<TransactionInfo>>();

        for (var groupIndex = 0; groupIndex < numberOfGroups; groupIndex++)
        {
            var groupId = groupIndex.ToString();

            expectedMessagesPerGroup[groupId] = new List<TransactionInfo>();

            for (var messageIndex = 0; messageIndex < numberOfMessagesPerGroup; messageIndex++)
            {
                var transactionInfo = new TransactionInfo
                {
                    UserId = groupId,
                    TransactionId = Guid.NewGuid().ToString(),
                    PublishTimeStamp = DateTime.UtcNow,
                };

                expectedMessagesPerGroup[groupId].Add(transactionInfo);

                await _publisher!.SendAsync(transactionInfo, new SQSOptions
                {
                    MessageGroupId = groupId
                });
            }
        }

        // Wait for the Lambda to handle the messages and write the IDs to CloudWatch
        await Task.Delay(TimeSpan.FromSeconds(20));

        // Extract the CloudWatch Logs lines that are logged for each message, and sort by timestamp
        var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, LambdaIntegrationTestFixture.FunctionPackageName, publishTimestamp);
        var handlerLogLines = logs
            .Where(logEvent => logEvent.Message.Contains("Processed message with Id: "))
            .OrderBy(logEvent => logEvent.Timestamp)
            .ToList();

        // Assert that the Lambda handled the number of messages that were published
        Assert.Equal(numberOfGroups * numberOfMessagesPerGroup, handlerLogLines.Count);

        // Sort the Lambda logs by message group
        var actualMessagesPerGroup = new Dictionary<string, List<string>>();
        foreach (var logLine in handlerLogLines)
        {
            // Extract IDs from expected format
            //  "Processed message with Id: {messageEnvelope.Message.TransactionId} as part of group {messageEnvelope.SQSMetadata?.MessageGroupId}"
            var pieces = logLine.Message.Split(" ");
            var messageId = pieces[4].Trim() ;
            var groupId = pieces[9].Trim();

            if (actualMessagesPerGroup.ContainsKey(groupId))
            {
                actualMessagesPerGroup[groupId].Add(messageId);
            }
            else
            {
                actualMessagesPerGroup[groupId] = new List<string> { messageId };
            }
        }

        // Now compare the messages IDs in each group, in the order they were published
        for (var groupIndex = 0; groupIndex < numberOfGroups; groupIndex++)
        {
            var groupId = groupIndex.ToString();

            Assert.Equal(expectedMessagesPerGroup[groupId].Count, actualMessagesPerGroup[groupId].Count);

            for (var messageIndex = 0; messageIndex < expectedMessagesPerGroup[groupId].Count; messageIndex++)
            {
                Assert.Equal(expectedMessagesPerGroup[groupId][messageIndex].TransactionId, actualMessagesPerGroup[groupId][messageIndex]);
            }
        }
    }
}


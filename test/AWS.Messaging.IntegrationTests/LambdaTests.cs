// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Lambda;
using Amazon.S3;
using Amazon.SQS;
using AWS.Messaging.IntegrationTests.Models;
using AWS.Messaging.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AWS.Messaging.IntegrationTests
{
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

        private const string TestBucketRoot = "mpf-lambda-artifacts-";
        private const string LambdaExecutionRoleName = "ExecutionRoleForMPFTestLambdas";
        private const string FunctionPackageName = "AWS.Messaging.Tests.LambdaFunctions";

        private readonly IAmazonIdentityManagementService _iamClient;
        private readonly IAmazonS3 _s3Client;

        public LambdaIntegrationTestFixture()
        {
            _iamClient = new AmazonIdentityManagementServiceClient();
            _s3Client = new AmazonS3Client();
        }

        public async Task InitializeAsync()
        {
            // Create the execution IAM role for the function
            ExecutionRoleArn = await AWSUtilities.CreateFunctionRoleIfNotExists(_iamClient, LambdaExecutionRoleName);

            // Create the S3 bucket used to deploy the functions
            ArtifactBucketName = TestBucketRoot + Guid.NewGuid().ToString();
            await AWSUtilities.CreateBucketWithDeploymentZipAsync(_s3Client, ArtifactBucketName, FunctionPackageName);
        }

        public async Task DisposeAsync()
        {
            // Delete the S3 bucket
            await AWSUtilities.DeleteDeploymentZipAndBucketAsync(_s3Client, ArtifactBucketName!);

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
        private string _functionName = "";

        public LambdaEventTests(LambdaIntegrationTestFixture fixture)
        {
            _fixture = fixture;

            _lambdaClient = new AmazonLambdaClient();
            _sqsClient = new AmazonSQSClient();
            _cloudWatchLogsClient = new AmazonCloudWatchLogsClient();
        }

        public async Task InitializeAsync()
        {
            _functionName = "AWS.Messaging.Tests.LambdaFunctions";

            // Create the function 
            await AWSUtilities.CreateFunctionAsync(_lambdaClient, _fixture.ArtifactBucketName, _functionName, "LambdaEventHandler", _fixture.ExecutionRoleArn, 3);

            // Create the queue and DLQ and map it to the function
            (_queueUrl, _dlqUrl) = await AWSUtilities.CreateQueueWithDLQAsync(_sqsClient, 3);
            await AWSUtilities.CreateQueueLambdaMapping(_sqsClient, _lambdaClient, _functionName, _queueUrl);

            // Create the publisher
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddAWSMessageBus(builder =>
            {
                builder.AddSQSPublisher<SimulatedMessage>(_queueUrl, "SimulatedMessage");
            });

            _publisher = serviceCollection.BuildServiceProvider().GetRequiredService<IMessagePublisher>();
        }

        public async Task DisposeAsync()
        {
            // Delete the function
            await AWSUtilities.DeleteFunctionIfExistsAsync(_lambdaClient, _functionName);

            // Delete the queues
            await _sqsClient.DeleteQueueAsync(_queueUrl);
            await _sqsClient.DeleteQueueAsync(_dlqUrl);
        }

        /// <summary>
        /// Happy path test for successful message
        /// </summary>
        [Fact]
        public async Task ProcessLambdaEventAsync_Success()
        {
            var message = new SimulatedMessage
            {
                Id = $"test-{Guid.NewGuid()}"
            };

            var publishTimestamp = DateTime.UtcNow;
            await _publisher!.PublishAsync(message);

            await Task.Delay(TimeSpan.FromSeconds(10));

            // Assert that the message was processed and logged successfully
            var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, _functionName, publishTimestamp);
            var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
            Assert.Single(logsWithHandler);
            Assert.Contains($"Processed message with Id: {message.Id}", logsWithHandler.First().Message);
        }

        /// <summary>
        /// Tests that a message that the Lambda handler fails for is moved to the DLQ appropriately
        /// </summary>
        [Fact]
        public async Task ProcessLambdaEventAsync_Failure()
        {
            var message = new SimulatedMessage
            {
                Id = $"test-{Guid.NewGuid()}",
                ReturnFailedStatus = true
            };

            var publishTimestamp = DateTime.UtcNow;
            await _publisher!.PublishAsync(message);

            await Task.Delay(TimeSpan.FromSeconds(10));

            // Assert that the message was not processed, since it's expected to fail
            var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, _functionName, publishTimestamp);
            var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
            Assert.Empty(logsWithHandler);

            // Assert that the message was moved to the DLQ
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(_dlqUrl);
            Assert.Single(receiveResponse.Messages);
            Assert.Contains(message.Id, receiveResponse.Messages[0].Body);
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
        private string _functionName = "";

        public LambdaBatchTests(LambdaIntegrationTestFixture fixture)
        {
            _fixture = fixture;

            _lambdaClient = new AmazonLambdaClient();
            _sqsClient = new AmazonSQSClient();
            _cloudWatchLogsClient = new AmazonCloudWatchLogsClient();
        }

        public async Task InitializeAsync()
        {
            _functionName = "AWS.Messaging.Tests.LambdaFunctions";

            // Create the function 
            await AWSUtilities.CreateFunctionAsync(_lambdaClient, _fixture.ArtifactBucketName, _functionName, "LambdaEventWithBatchResponseHandler", _fixture.ExecutionRoleArn, 3);

            // Create the queue and DLQ and map it to the function
            (_queueUrl, _dlqUrl) = await AWSUtilities.CreateQueueWithDLQAsync(_sqsClient, 3);
            await AWSUtilities.CreateQueueLambdaMapping(_sqsClient, _lambdaClient, _functionName, _queueUrl, true);

            // Create the publisher
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddAWSMessageBus(builder =>
            {
                builder.AddSQSPublisher<SimulatedMessage>(_queueUrl, "SimulatedMessage");
            });

            _publisher = serviceCollection.BuildServiceProvider().GetRequiredService<IMessagePublisher>();
        }

        public async Task DisposeAsync()
        {
            // Delete the function
            await AWSUtilities.DeleteFunctionIfExistsAsync(_lambdaClient, _functionName);

            // Delete the queue
            await _sqsClient.DeleteQueueAsync(_queueUrl);
            await _sqsClient.DeleteQueueAsync(_dlqUrl);
        }

        /// <summary>
        /// Happy path test for successful message
        /// </summary>
        [Fact]
        public async Task ProcessLambdaEventAsync_Success()
        {
            var message = new SimulatedMessage
            {
                Id = $"test-{Guid.NewGuid()}"
            };

            var publishTimestamp = DateTime.UtcNow;
            await _publisher!.PublishAsync(message);

            await Task.Delay(TimeSpan.FromSeconds(10));

            // Assert that the message was processed and logged successfully
            var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, _functionName, publishTimestamp);
            var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
            Assert.Single(logsWithHandler);
            Assert.Contains($"Processed message with Id: {message.Id}", logsWithHandler.First().Message);
        }

        /// <summary>
        /// Tests that a message that the Lambda handler fails for is moved to the DLQ appropriately
        /// </summary>
        [Fact]
        public async Task ProcessLambdaEventAsync_Failure()
        {
            var message = new SimulatedMessage
            {
                Id = $"test-{Guid.NewGuid()}",
                ReturnFailedStatus = true
            };

            var publishTimestamp = DateTime.UtcNow;
            await _publisher!.PublishAsync(message);

            await Task.Delay(TimeSpan.FromSeconds(10));

            // Assert that the message was not processed, since it's expected to fail
            var logs = await AWSUtilities.GetMostRecentLambdaLogs(_cloudWatchLogsClient, _functionName, publishTimestamp);
            var logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains("Processed message with Id: "));
            Assert.Empty(logsWithHandler);

            // Assert that the message was moved to the DLQ
            var receiveResponse = await _sqsClient.ReceiveMessageAsync(_dlqUrl);
            Assert.Single(receiveResponse.Messages);
            Assert.Contains(message.Id, receiveResponse.Messages[0].Body);
        }
    }
}

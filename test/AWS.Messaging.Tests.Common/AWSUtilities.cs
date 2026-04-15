// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.S3.Model;
using Amazon.S3;
using Amazon.Lambda.Model;
using Amazon.Lambda;
using Amazon.SQS;
using Amazon.CloudWatchLogs.Model;
using Amazon.CloudWatchLogs;
using Amazon.SQS.Model;

namespace AWS.Messaging.Tests.Common;

/// <summary>
/// Utilities to create AWS resources for integration tests
/// </summary>
public static class AWSUtilities
{
    public const string LambdaManagedPolicyArn = "arn:aws:iam::aws:policy/service-role/AWSLambdaSQSQueueExecutionRole";
    private static readonly string LambdaAssumeRolePolicy =
    @"
        {
          ""Version"": ""2012-10-17"",
          ""Statement"": [
            {
              ""Effect"": ""Allow"",
              ""Principal"": {
                ""Service"": ""lambda.amazonaws.com""
              },
              ""Action"": ""sts:AssumeRole""
            }
          ]
        }
        ".Trim();

    /// <summary>
    /// Creates a Lambda execution role if it doesn't already exist, using the AWS-provided AWSLambdaSQSQueueExecutionRole
    /// </summary>
    /// <param name="iamClient">IAM Client</param>
    /// <param name="roleName">Desired name of the role</param>
    /// <returns>Role ARN</returns>
    public static async Task<string> CreateFunctionRoleIfNotExists(IAmazonIdentityManagementService iamClient, string roleName)
    {
        var getRoleRequest = new GetRoleRequest
        {
            RoleName = roleName
        };
        try
        {
            return (await iamClient.GetRoleAsync(getRoleRequest)).Role.Arn;
        }
        catch (NoSuchEntityException)
        {
            // create the role
            var createRoleRequest = new CreateRoleRequest
            {
                RoleName = roleName,
                Description = "Execution role for Lambda functions for testing the .NET message processing framework",
                AssumeRolePolicyDocument = LambdaAssumeRolePolicy
            };

            var executionRoleArn = (await iamClient.CreateRoleAsync(createRoleRequest)).Role.Arn;

            // Wait for role to propagate.
            await WaitTillRoleAvailableAsync(iamClient, roleName);

            await iamClient.AttachRolePolicyAsync(new AttachRolePolicyRequest
            {
                RoleName = roleName,
                PolicyArn = LambdaManagedPolicyArn
            });

            return executionRoleArn;
        }
    }

    public async static Task WaitTillRoleAvailableAsync(IAmazonIdentityManagementService iamClient, string roleName)
    {
        const int POLL_INTERVAL = 3000;
        const int MAX_TIMEOUT_MINUTES = 1;

        var getRoleRequest = new GetRoleRequest()
        {
            RoleName = roleName
        };

        var startTime = DateTime.Now;
        while (DateTime.Now < startTime.AddMinutes(MAX_TIMEOUT_MINUTES))
        {
            try
            {
                var response = await iamClient.GetRoleAsync(getRoleRequest);
                return;
            }
            catch (NoSuchEntityException)
            {
                // If the role doesn't exist yet, wait then try polling again
                await Task.Delay(POLL_INTERVAL);
            }
            catch (Exception)
            {
                // Rethrow all other exceptions
                throw;
            }
        }
    }

    /// <summary>
    /// Creates an S3 bucket to hold the Lambda deployment artifact(s), and uploads the specified deployment zip
    /// </summary>
    /// <param name="s3Client">S3 Client</param>
    /// <param name="bucketName">Desired name of the bucket</param>
    /// <param name="functionName">Name of the Lambda deployment artifact elsewhere in this test suite</param>
    public async static Task CreateBucketWithDeploymentZipAsync(IAmazonS3 s3Client, string bucketName, string functionName)
    {
        // Create bucket if it doesn't exist
        var listBucketsResponse = await s3Client.ListBucketsAsync();
        if (listBucketsResponse.Buckets?.Find((bucket) => bucket.BucketName == bucketName) == null)
        {
            var putBucketRequest = new PutBucketRequest
            {
                BucketName = bucketName
            };
            await s3Client.PutBucketAsync(putBucketRequest);
            await Task.Delay(10000);
        }

        // Write or overwrite deployment package
        var putObjectRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = $"{functionName}.zip",
            FilePath = TestUtilities.GetDeploymentZipPath(functionName)
        };

        await s3Client.PutObjectAsync(putObjectRequest);
    }

    /// <summary>
    /// Deletes an S3 bucket and all objects in it
    /// </summary>
    /// <param name="s3Client">S3 Client</param>
    /// <param name="bucketName">Name of the bucket to delete</param>
    public static async Task DeleteDeploymentZipAndBucketAsync(IAmazonS3 s3Client, string bucketName)
    {
        try
        {
            await Amazon.S3.Util.AmazonS3Util.DeleteS3BucketWithObjectsAsync(s3Client, bucketName);
        }
        catch (AmazonS3Exception e)
        {
            // If it's just telling us the bucket's not there then continue, otherwise throw.
            if (!e.Message.Contains("The specified bucket does not exist"))
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Creates a Lambda function
    /// </summary>
    /// <param name="lambdaClient">Lambda client</param>
    /// <param name="bucketName">S3 bucket containing the deployment artifact</param>
    /// <param name="functionName">Function name, which will be used to name the function as well as match the deployment artifact and handler</param>
    /// <param name="handlerName">Name of the actual handler function within the deployment artifact</param>
    /// <param name="executionRoleArn">Lambda execution role to assign</param>
    /// <param name="functionTimeout">Desired function timeout, in seconds</param>
    public static async Task CreateFunctionAsync(IAmazonLambda lambdaClient, string bucketName, string functionName, string handlerName, string executionRoleArn, int functionTimeout)
    {
        await DeleteFunctionIfExistsAsync(lambdaClient, functionName);

        var createRequest = new CreateFunctionRequest
        {
            FunctionName = functionName.Replace(".", ""),
            Code = new FunctionCode
            {
                S3Bucket = bucketName,
                S3Key = $"{functionName}.zip"
            },
            Handler = $"{functionName}::{functionName}.Functions::{handlerName}",
            MemorySize = 512,
            Timeout = functionTimeout,
            Runtime = Runtime.Dotnet8,
            Role = executionRoleArn,
        };

        var startTime = DateTime.Now;
        var created = false;
        while (DateTime.Now < startTime.AddSeconds(30))
        {
            try
            {
                await lambdaClient.CreateFunctionAsync(createRequest);
                created = true;
                break;
            }
            catch (InvalidParameterValueException ipve)
            {
                // Wait for the role to be fully propagated through AWS
                if (ipve.Message == "The role defined for the function cannot be assumed by Lambda.")
                {
                    await Task.Delay(2000);
                }
                else
                {
                    throw;
                }
            }
        }

        await WaitTillFunctionAvailableAsync(lambdaClient, functionName.Replace(".", ""));

        if (!created)
        {
            throw new Exception($"Timed out trying to create Lambda function {functionName}");
        }
    }

    /// <summary>
    /// Waits until the specified Lambda function is no longer "Pending"
    /// </summary>
    /// <param name="lambdaClient">Lambda client</param>
    /// <param name="functionName">Function name</param>
    private static async Task WaitTillFunctionAvailableAsync(IAmazonLambda lambdaClient, string functionName)
    {
        const int POLL_INTERVAL = 3000;
        const int MAX_TIMEOUT_MINUTES = 5;

        try
        {
            var request = new GetFunctionConfigurationRequest
            {
                FunctionName = functionName
            };

            GetFunctionConfigurationResponse response;

            var timeout = DateTime.UtcNow.AddMinutes(MAX_TIMEOUT_MINUTES);
            var startTime = DateTime.UtcNow;
            do
            {
                response = await lambdaClient.GetFunctionConfigurationAsync(request);
                if (response.LastUpdateStatus != LastUpdateStatus.InProgress && response.State != Amazon.Lambda.State.Pending)
                {
                    if (response.LastUpdateStatus == LastUpdateStatus.Failed)
                    {
                        // Not throwing exception because it is possible the calling code could be fixing the failed state.
                    }

                    return;
                }

                await Task.Delay(POLL_INTERVAL);

            } while (DateTime.UtcNow < timeout);

        }
        catch (Exception e)
        {
            throw new Exception($"Error waiting for Lambda function to be in available status: {e.Message}");
        }

        throw new Exception($"Timeout waiting for function {functionName} to become available");
    }

    /// <summary>
    /// Deletes a Lambda function if it already exists. Removes any event source mappings first.
    /// </summary>
    /// <param name="lambdaClient">Lambda client</param>
    /// <param name="functionName">Name of the function to delete</param>
    public static async Task DeleteFunctionIfExistsAsync(IAmazonLambda lambdaClient, string functionName)
    {
        // First remove any event source mappings from the function (if it is recreated with the same name these will reappear)
        var eventSourceMappingsPaginator = lambdaClient.Paginators.ListEventSourceMappings(new ListEventSourceMappingsRequest
        {
            FunctionName = functionName.Replace(".", "")
        });

        await foreach (var mapping in eventSourceMappingsPaginator.EventSourceMappings)
        {
            if (mapping.State != "Deleting")
            {
                await lambdaClient.DeleteEventSourceMappingAsync(new DeleteEventSourceMappingRequest
                {
                    UUID = mapping.UUID
                });
            }
        }

        var request = new DeleteFunctionRequest
        {
            FunctionName = functionName.Replace(".", "")
        };

        try
        {
            var response = await lambdaClient.DeleteFunctionAsync(request);
        }
        catch (Amazon.Lambda.Model.ResourceNotFoundException)
        {
            // no problem
        }
    }

    /// <summary>
    /// Creates an SQS queue
    /// </summary>
    /// <param name="sqsClient">SQS Client</param>
    /// <param name="queueName">Desired name of the queue</param>
    /// <param name="messageVisiblityTimeout">The queue's default message visibility timeout, in seconds</param>
    /// <returns>Queue URL</returns>
    public static async Task<string> CreateQueueAsync(IAmazonSQS sqsClient, string queueName = "", int messageVisiblityTimeout = 30, bool isFifo = false)
    {
        if (string.IsNullOrEmpty(queueName))
        {
            queueName = $"MPFTest-{Guid.NewGuid().ToString().Split('-').Last()}";
        }

        if (isFifo && !queueName.EndsWith(".fifo"))
        {
            queueName += ".fifo";
        }

        var request = new CreateQueueRequest
        {
            QueueName = queueName,
            Attributes = new Dictionary<string, string>()
            {
                { QueueAttributeName.VisibilityTimeout, messageVisiblityTimeout.ToString() }
            }
        };

        if (isFifo)
        {
            request.Attributes[QueueAttributeName.FifoQueue] = "true";
            request.Attributes[QueueAttributeName.ContentBasedDeduplication] = "true";
        }
        var createQueueResponse = await sqsClient.CreateQueueAsync(request);

        return createQueueResponse.QueueUrl;
    }

    /// <summary>
    /// Creates an SQS Queue with a dead-letter queue
    /// </summary>
    /// <param name="sqsClient">SQS Client</param>
    /// <param name="messageVisiblityTimeout">The queue's default message visibility timeout, in seconds</param>
    /// <returns>Tuple consisting of the queue URL followed by the DLQ URL</returns>
    public static async Task<(string, string)> CreateQueueWithDLQAsync(IAmazonSQS sqsClient, bool isFifo = false, int messageVisibilityTimeout = 30)
    {
        // Create both queues
        var queueName = $"MPFTest-{Guid.NewGuid().ToString().Split('-').Last()}";
        var sourceQueueUrl = await CreateQueueAsync(sqsClient, queueName, messageVisibilityTimeout, isFifo);
        var dlqUrl = await CreateQueueAsync(sqsClient, queueName + "-DLQ", messageVisibilityTimeout, isFifo);

        // Get the DLQ's arn
        var dlqAttributes = await sqsClient.GetQueueAttributesAsync(dlqUrl, new List<string> { "QueueArn" });

        // Set the DLQ as the DLQ for the source queue
        var request = new SetQueueAttributesRequest
        {
            QueueUrl = sourceQueueUrl,
            Attributes = new Dictionary<string, string>
            {
                { QueueAttributeName.RedrivePolicy, $"{{ \"maxReceiveCount\": \"1\", \"deadLetterTargetArn\": \"{dlqAttributes.QueueARN}\"}}" }
            }
        };

        await sqsClient.SetQueueAttributesAsync(request);

        return (sourceQueueUrl, dlqUrl);
    }


    public static async Task<IList<OutputLogEvent>> PollForLogWithMessage(IAmazonCloudWatchLogs cloudWatchLogsClient, string functionName, string message, DateTime publishTimestamp)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));

        IEnumerable<Amazon.CloudWatchLogs.Model.OutputLogEvent>? logsWithHandler = null;
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow < start.AddMinutes(1))
        {
            // Assert that the message was processed and logged successfully
            var logs = await GetMostRecentLambdaLogs(cloudWatchLogsClient, functionName, publishTimestamp);
            if (logs == null)
                continue;

            logsWithHandler = logs.Where(logEvent => logEvent.Message.Contains(message));
            if (logsWithHandler != null && logsWithHandler.Count() > 0)
                break;

            await Task.Delay(1000);
        }

        if (logsWithHandler == null || logsWithHandler.Count() == 0)
        {
            Console.WriteLine("No logs");
        }

        return logsWithHandler?.ToList() ?? new List<OutputLogEvent>();
    }

    /// <summary>
    /// Returns a list of the most recent log events for a Lambda function
    /// </summary>
    /// <param name="cloudWatchLogsClient">CloudWatch Logs client</param>
    /// <param name="lambdaName">Name of the Lambda function whose logs to request</param>
    /// <param name="publishTimestamp">Only logs after this timestamp will be returned</param>
    /// <returns>List of log events</returns>
    public static async Task<List<OutputLogEvent>> GetMostRecentLambdaLogs(IAmazonCloudWatchLogs cloudWatchLogsClient, string lambdaName, DateTime publishTimestamp)
    {
        var logGroupName = $"/aws/lambda/{lambdaName.Replace(".", "")}";

        // Get the most recent log stream
        var logStreamsResponse = await cloudWatchLogsClient.DescribeLogStreamsAsync(new DescribeLogStreamsRequest
        {
            LogGroupName = logGroupName,
            OrderBy = OrderBy.LastEventTime,
            Descending = true
        });

        if (logStreamsResponse.LogStreams.Count == 0)
        {
            throw new Exception($"Expected at least one log stream for {logGroupName} to assert against.");
        }

        var logs = new List<OutputLogEvent>();

        foreach (var logStream in logStreamsResponse.LogStreams)
        {
            if (logStream.LastEventTimestamp > publishTimestamp)
            {
                var getLogsRequest = new GetLogEventsRequest()
                {
                    LogGroupName = logGroupName,
                    LogStreamName = logStream.LogStreamName,
                    StartTime = publishTimestamp
                };

                // Do the pagination manually since the generated paginator will keep looping
                // indefinitely in case more logs become available.
                do
                {
                    var getLogsResponse = await cloudWatchLogsClient.GetLogEventsAsync(getLogsRequest);

                    logs.AddRange(getLogsResponse.Events);

                    // Stop looping once the NextToken repeats, which mean we've read all of the current logs
                    if (getLogsResponse.NextForwardToken == getLogsRequest.NextToken)
                    {
                        break;
                    }

                    getLogsRequest.NextToken = getLogsResponse.NextForwardToken;

                } while (true);
            }
        }

        return logs;
    }

    /// <summary>
    /// Creates the mapping between an SQS queue and a Lambda function
    /// </summary>
    /// <param name="sqsClient">SQS Client</param>
    /// <param name="lambdaClient">Lambda client</param>
    /// <param name="functionName">Name of the Lambda function to map</param>
    /// <param name="queueUrl">Name of the SQS queue to map</param>
    /// <param name="reportBatchItemFailures">Set to true to activate ReportBatchItemFailures</param>
    public static async Task CreateQueueLambdaMapping(IAmazonSQS sqsClient, IAmazonLambda lambdaClient, string functionName, string queueUrl, bool reportBatchItemFailures = false)
    {
        var queueArn = (await sqsClient.GetQueueAttributesAsync(queueUrl, new List<string> { "All" })).QueueARN;

        var request = new CreateEventSourceMappingRequest()
        {
            FunctionName = functionName.Replace(".", ""),
            EventSourceArn = queueArn,
        };

        if (reportBatchItemFailures)
        {
            request.FunctionResponseTypes = new List<string> { "ReportBatchItemFailures" };
        }

        var response = await lambdaClient.CreateEventSourceMappingAsync(request);

        await TestUtilities.WaitUntilAsync(async () =>
        {
            var state = (await lambdaClient.GetEventSourceMappingAsync(new GetEventSourceMappingRequest { UUID = response.UUID })).State;
            return state == "Enabled";

        });
    }
}

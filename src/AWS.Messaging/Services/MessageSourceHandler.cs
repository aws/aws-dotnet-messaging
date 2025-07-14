// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Amazon.Util;
using AWS.Messaging.Configuration;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Services;

/// <summary>
/// Provides the functionality to compute the message source
/// based on the hosting environment.
/// </summary>
internal class MessageSourceHandler : IMessageSourceHandler
{
    private readonly IEnvironmentManager _environmentManager;
    private readonly IECSContainerMetadataManager _ecsContainerMetadataManager;
    private readonly IEC2InstanceMetadataManager _ec2InstanceMetadataHandler;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly ILogger<MessageSourceHandler> _logger;

    public MessageSourceHandler(
        IEnvironmentManager environmentManager,
        IECSContainerMetadataManager ecsContainerMetadataManager,
        IEC2InstanceMetadataManager ec2InstanceMetadataHandler,
        IMessageConfiguration messageConfiguration,
        ILogger<MessageSourceHandler> logger)
    {
        _environmentManager = environmentManager;
        _ecsContainerMetadataManager = ecsContainerMetadataManager;
        _ec2InstanceMetadataHandler = ec2InstanceMetadataHandler;
        _messageConfiguration = messageConfiguration;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a message source depending on the compute environment that it's executing in.
    /// The following compute environments are considered, in the specified order below:
    /// <list type="number">
    /// <item>
    ///     <term>AWS Lambda</term>
    ///     <description>The AWS_LAMBDA_FUNCTION_NAME environment variable is checked.</description>
    /// </item>
    /// <item>
    ///     <term>Amazon ECS</term>
    ///     <description>The ECS_CONTAINER_METADATA_URI environment variable is checked.</description>
    /// </item>
    /// <item>
    ///     <term>Amazon EC2</term>
    ///     <description>The AWS SDK for .NET is used to retrieve the <see cref="EC2InstanceMetadata.InstanceId"/>
    ///     property using <see cref="EC2InstanceMetadata"/></description>
    /// </item>
    /// <item>
    ///     <description>If the source cannot be resolved from the compute environment,
    ///     we fallback to using "/aws/messaging" as the source identifier</description>
    /// </item>
    /// </list>
    /// After a source is computed, the message source suffix is appended if one is set.
    /// </summary>
    /// <returns>The computed message source.</returns>
    public async Task<Uri> ComputeMessageSource()
    {
        if (_messageConfiguration.Source != null)
            return GetFullSourceUri(_messageConfiguration.Source, _messageConfiguration.SourceSuffix);

        _logger.LogTrace("Attempting to compute message source based on the current environment...");
        var messageSource = GetSourceFromLambda();
        if (string.IsNullOrEmpty(messageSource))
        {
            messageSource = await GetSourceFromECS();
        }
        if (string.IsNullOrEmpty(messageSource))
        {
            messageSource = GetSourceFromEC2();
        }
        if (string.IsNullOrEmpty(messageSource))
        {
            messageSource = "/aws/messaging";
        }

        _logger.LogTrace("Computed message source is '{MessageSource}'", messageSource);

        return GetFullSourceUri(messageSource, _messageConfiguration.SourceSuffix);
    }

    /// <summary>
    /// Combines the message source and the source suffix
    /// and ensures the proper separation is included between the two.
    /// </summary>
    /// <param name="source">The message source</param>
    /// <param name="suffix">The message source suffix</param>
    /// <returns>A Uri that represents the message source and source suffix</returns>
    private Uri GetFullSourceUri(string source, string? suffix)
    {
        source = source.Trim();
        suffix = suffix?.Trim();
        var sourceEndsInSlash = source.EndsWith('/');
        var suffixStartsWithSlash = suffix?.StartsWith('/') ?? true;

        if (sourceEndsInSlash && suffixStartsWithSlash)
        {
            source = source[..^1];
        }
        else if (!sourceEndsInSlash && !suffixStartsWithSlash)
        {
            suffix = $"/{suffix}";
        }

        return new Uri($"{source}{suffix}", UriKind.Relative);
    }

    /// <summary>
    /// Lambda runtime sets several environment variables during initialization.
    /// This checks the AWS_LAMBDA_FUNCTION_NAME environment variable to tell if the process is running inside a Lambda function.
    /// </summary>
    /// <returns>Message source from AWS Lambda</returns>
    private string? GetSourceFromLambda()
    {
        _logger.LogTrace("Checking if process if running in AWS Lambda...");

        var lambdaFunctionName = _environmentManager.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME");

        return
            !string.IsNullOrEmpty(lambdaFunctionName) ?
            $"/AWSLambda/{lambdaFunctionName}" :
            null;
    }

    /// <summary>
    /// The Amazon ECS container agent injects an environment variable called ECS_CONTAINER_METADATA_URI into each container in a task.
    /// To retrieve the metadata related to an ECS task we can issue a GET request to ${ECS_CONTAINER_METADATA_URI}/task
    /// If a value is found for the Cluster and TaskARN, this would indicate that the process is running in Amazon ECS.
    /// </summary>
    /// <returns>Message source from Amazon ECS</returns>
    private async Task<string?> GetSourceFromECS()
    {
        _logger.LogTrace("Checking if process if running in Amazon ECS...");

        var taskMetadata = await _ecsContainerMetadataManager.GetContainerTaskMetadata();
        if (taskMetadata == null)
            return null;

        static string GetLastSegment(string value)
        {
            var span = value.AsSpan();
            int lastSlash = span.LastIndexOf('/');
            return lastSlash >= 0 ? span.Slice(lastSlash + 1).ToString() : value;
        }

        string clusterName = GetLastSegment(taskMetadata.Cluster);
        string taskId = GetLastSegment(taskMetadata.TaskARN);

        return $"/AmazonECS/{clusterName}/{taskId}";
    }

    /// <summary>
    /// The AWS SDK for .NET is used to check the EC2 Instance Metadata.
    /// If the SDK returns valid values, this would indicate that the process is running in Amazon EC2.
    /// </summary>
    /// <returns>Message source from Amazon EC2</returns>
    private string? GetSourceFromEC2()
    {
        _logger.LogTrace("Checking if process if running in Amazon EC2...");

        var instanceID = _ec2InstanceMetadataHandler.InstanceId;

        return
            !string.IsNullOrEmpty(instanceID) ?
            $"/AmazonEC2/{instanceID}" :
            null;
    }
}

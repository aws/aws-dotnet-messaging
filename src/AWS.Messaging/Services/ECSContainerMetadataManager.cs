// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Amazon;
using AWS.Messaging.Configuration.Internal;
using AWS.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Services;

/// <summary>
/// A wrapper around ECS container metadata.
/// </summary>
internal class ECSContainerMetadataManager : IECSContainerMetadataManager
{
    private readonly IEnvironmentManager _environmentManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ECSContainerMetadataManager> _logger;

    private readonly string EcsContainerHostAddress = "169.254.170.2";

    public ECSContainerMetadataManager(
        IEnvironmentManager environmentManager,
        IHttpClientFactory httpClientFactory,
        ILogger<ECSContainerMetadataManager> logger)
    {
        _environmentManager = environmentManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TaskMetadataResponse?> GetContainerTaskMetadata()
    {
        var ecsMetadataUri = _environmentManager.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI");
        if (string.IsNullOrEmpty(ecsMetadataUri))
            return null;

        var resolvedUri = new Uri(ecsMetadataUri);
        if (!EcsContainerHostAddress.Equals(resolvedUri.Host))
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient("ECSMetadataClient");

            var response = await client.GetAsync(new Uri($"{ecsMetadataUri}/task"));
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Unable to retrieve task metadata from the ECS container.");
                return null;
            }

            var taskMetadataJson = await response.Content.ReadAsStringAsync();
            var taskMetadata = JsonSerializer.Deserialize<TaskMetadataResponse>(taskMetadataJson, MessagingJsonSerializerContext.Default.TaskMetadataResponse);
            if (ValidateContainerTaskMetadata(taskMetadata))
            {
                return taskMetadata;
            }
            else
            {
                _logger.LogError("The retrieved task metadata from the ECS container is invalid.");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to retrieve task metadata from the ECS container.");
            return null;
        }
    }

    /// <summary>
    /// Runs validation checks on the information returned from the task metadata endpoint.
    /// </summary>
    /// <param name="metadata">The container task metadata.</param>
    /// <returns>Whether the response is valid or not.</returns>
    private bool ValidateContainerTaskMetadata(TaskMetadataResponse? metadata)
    {
        if (metadata == null)
            return false;

        // Use the .NET SDK to parse the Cluster to check if it is an ARN or Short Name
        if (!Arn.IsArn(metadata.Cluster))
        {
            // Validate the Cluster name which has the following validation rule:
            // There can be a maximum of 255 characters. The valid characters are letters (uppercase and lowercase), numbers, hyphens, and underscores.
            if (string.IsNullOrEmpty(metadata.Cluster))
                return false;

            // Check if the string length is within the allowed limit (255 characters)
            if (metadata.Cluster.Length > 255)
                return false;

            // Use a regular expression to validate the characters
            // Valid characters include letters (uppercase and lowercase), numbers, hyphens, and underscores
            if (!Regex.IsMatch(metadata.Cluster, @"^[A-Za-z0-9\-_]+$"))
                return false;
        }

        // Use the .NET SDK to parse the Task ARN to check if it is valid
        if (!Arn.IsArn(metadata.TaskARN))
            return false;

        return true;
    }


}

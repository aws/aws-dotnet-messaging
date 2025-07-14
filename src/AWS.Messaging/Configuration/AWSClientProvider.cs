// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Runtime;
using AWS.Messaging.Telemetry;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Provides an AWS service client from the DI container
/// </summary>
internal class AWSClientProvider : IAWSClientProvider
{
    private const string _userAgentHeader = "User-Agent";
    private static readonly string _userAgentString = $"lib/aws-dotnet-messaging#{TelemetryKeys.AWSMessagingAssemblyVersion}";

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates an instance of <see cref="AWSClientProvider"/>
    /// </summary>
    public AWSClientProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public T GetServiceClient<T>() where T : IAmazonService
    {
        var serviceClient =  _serviceProvider.GetService(typeof(T)) ?? throw new FailedToFindAWSServiceClientException($"Failed to find AWS service client of type {typeof(T)}");
        if (serviceClient is AmazonServiceClient client)
        {
            client.BeforeRequestEvent += AWSServiceClient_BeforeServiceRequest;
        }
        return (T)serviceClient;
    }

    internal static void AWSServiceClient_BeforeServiceRequest(object sender, RequestEventArgs e)
    {
        if (e is not WebServiceRequestEventArgs args || !args.Headers.TryGetValue(_userAgentHeader, out var value) || value.Contains(_userAgentString))
            return;

        args.Headers[_userAgentHeader] = value + " " + _userAgentString;
    }
}

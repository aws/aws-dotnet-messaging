// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Amazon.Runtime;
using AWS.Messaging.Telemetry;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Provides an AWS service client from the DI container
/// </summary>
internal class AWSClientProvider : IAWSClientProvider
{
    private const string _userAgentHeader = "User-Agent";
    private static readonly string _userAgentString = $"lib/aws-dotnet-messaging_{TelemetryKeys.AWSMessagingAssemblyVersion}";

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
        if (serviceClient is AmazonServiceClient)
        {
            ((AmazonServiceClient)serviceClient).BeforeRequestEvent += AWSServiceClient_BeforeServiceRequest;
        }
        return (T)serviceClient;
    }

    private static void AWSServiceClient_BeforeServiceRequest(object sender, RequestEventArgs e)
    {
        if (e is not WebServiceRequestEventArgs args || !args.Headers.ContainsKey(_userAgentHeader) || args.Headers[_userAgentHeader].Contains(_userAgentString))
            return;

        args.Headers[_userAgentHeader] = args.Headers[_userAgentHeader] + " " + _userAgentString;
    }
}

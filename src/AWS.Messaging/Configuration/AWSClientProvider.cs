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
        WebServiceRequestEventArgs? args = e as WebServiceRequestEventArgs;
        if (args != null && args.Request is Amazon.Runtime.Internal.IAmazonWebServiceRequest internalRequest && !internalRequest.UserAgentDetails.GetCustomUserAgentComponents().Contains(_userAgentString))
        {
            internalRequest.UserAgentDetails.AddUserAgentComponent(_userAgentString);
        }
    }
}

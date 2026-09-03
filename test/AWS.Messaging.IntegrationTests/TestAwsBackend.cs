// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.EventBridge;
using Amazon.IdentityManagement;
using Amazon.Lambda;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using LocalSqsSnsMessaging;
using Xunit;

namespace AWS.Messaging.IntegrationTests;

/// <summary>
/// The backend that integration tests run against, selected via the
/// <c>AWS_MESSAGING_TEST_MODE</c> environment variable.
/// </summary>
public enum TestAwsBackendMode
{
    /// <summary>In-memory LocalSqsSnsMessaging bus (default). No AWS account or containers needed.</summary>
    InMemory,

    /// <summary>A local Floci container (LocalStack-compatible AWS emulator). Close approximation of
    /// real AWS, including Lambda. Start it with
    /// <c>docker compose -f test/AWS.Messaging.IntegrationTests/docker-compose.yml up -d</c>.</summary>
    Floci,

    /// <summary>Real AWS resources, using the ambient credential chain.</summary>
    Aws
}

/// <summary>
/// Provides the AWS service clients that the integration tests run against, in one of three modes
/// (selected via the <c>AWS_MESSAGING_TEST_MODE</c> environment variable):
/// <list type="bullet">
/// <item><c>local</c> / unset — in-memory LocalSqsSnsMessaging bus; <c>dotnet test</c> needs no AWS account.</item>
/// <item><c>floci</c> — a local Floci emulator container at <c>http://localhost:4566</c>
/// (override with <c>FLOCI_SERVICE_URL</c>).</item>
/// <item><c>aws</c> — real AWS resources.</item>
/// </list>
/// </summary>
public sealed class TestAwsBackend
{
    public const string TestModeEnvironmentVariable = "AWS_MESSAGING_TEST_MODE";
    public const string FlociServiceUrlEnvironmentVariable = "FLOCI_SERVICE_URL";
    public const string DefaultFlociServiceUrl = "http://localhost:4566";

    /// <summary>
    /// The signing region for requests against the Floci emulator. Floci does not validate it,
    /// but the AWS SDK requires a region to sign requests.
    /// </summary>
    private const string FlociRegion = "us-east-1";

    /// <summary>
    /// Dummy credentials for the Floci emulator, which accepts any non-empty access/secret key.
    /// </summary>
    private static readonly AWSCredentials FlociCredentials = new BasicAWSCredentials("floci", "floci");

    public static TestAwsBackendMode Mode =>
        Environment.GetEnvironmentVariable(TestModeEnvironmentVariable)?.ToLowerInvariant() switch
        {
            "aws" => TestAwsBackendMode.Aws,
            "floci" => TestAwsBackendMode.Floci,
            _ => TestAwsBackendMode.InMemory
        };

    /// <summary>
    /// True when the backend provides real AWS service infrastructure (real AWS or the Floci
    /// emulator) rather than the in-memory bus. Tests needing services the in-memory bus cannot
    /// emulate (e.g. Lambda) are gated on this via <see cref="AWSFactAttribute"/>/<see cref="AWSTheoryAttribute"/>.
    /// </summary>
    public static bool IsRealAwsInfrastructure => Mode != TestAwsBackendMode.InMemory;

    /// <summary>
    /// The Floci service endpoint, overridable via <c>FLOCI_SERVICE_URL</c>.
    /// </summary>
    public static string FlociServiceUrl =>
        Environment.GetEnvironmentVariable(FlociServiceUrlEnvironmentVariable) is { Length: > 0 } url
            ? url
            : DefaultFlociServiceUrl;

    private readonly InMemoryAwsBus? _bus;

    public TestAwsBackend()
    {
        if (Mode == TestAwsBackendMode.InMemory)
        {
            _bus = new InMemoryAwsBus();
        }
    }

    private static TConfig ConfigureFloci<TConfig>(TConfig config) where TConfig : ClientConfig
    {
        config.ServiceURL = FlociServiceUrl;
        config.AuthenticationRegion = FlociRegion;
        return config;
    }

    public IAmazonSQS CreateSqsClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateSqsClient(),
        TestAwsBackendMode.Floci => new AmazonSQSClient(FlociCredentials, ConfigureFloci(new AmazonSQSConfig())),
        _ => new AmazonSQSClient()
    };

    public IAmazonSimpleNotificationService CreateSnsClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateSnsClient(),
        TestAwsBackendMode.Floci => new AmazonSimpleNotificationServiceClient(FlociCredentials, ConfigureFloci(new AmazonSimpleNotificationServiceConfig())),
        _ => new AmazonSimpleNotificationServiceClient()
    };

    public IAmazonEventBridge CreateEventBridgeClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateEventBridgeClient(),
        TestAwsBackendMode.Floci => new AmazonEventBridgeClient(FlociCredentials, ConfigureFloci(new AmazonEventBridgeConfig())),
        _ => new AmazonEventBridgeClient()
    };

    // The services below have no in-memory emulation; tests that need them are gated on
    // IsRealAwsInfrastructure, so these factories are only reached in Floci or AWS mode.

    public IAmazonLambda CreateLambdaClient() => Mode switch
    {
        TestAwsBackendMode.Floci => new AmazonLambdaClient(FlociCredentials, ConfigureFloci(new AmazonLambdaConfig())),
        TestAwsBackendMode.Aws => new AmazonLambdaClient(),
        _ => throw NoInMemorySupport("Lambda")
    };

    public IAmazonS3 CreateS3Client() => Mode switch
    {
        // Path-style addressing avoids bucket-name-as-subdomain resolution against the emulator.
        TestAwsBackendMode.Floci => new AmazonS3Client(FlociCredentials, ConfigureFloci(new AmazonS3Config { ForcePathStyle = true })),
        TestAwsBackendMode.Aws => new AmazonS3Client(),
        _ => throw NoInMemorySupport("S3")
    };

    public IAmazonIdentityManagementService CreateIAMClient() => Mode switch
    {
        TestAwsBackendMode.Floci => new AmazonIdentityManagementServiceClient(FlociCredentials, ConfigureFloci(new AmazonIdentityManagementServiceConfig())),
        TestAwsBackendMode.Aws => new AmazonIdentityManagementServiceClient(),
        _ => throw NoInMemorySupport("IAM")
    };

    public IAmazonCloudWatchLogs CreateCloudWatchLogsClient() => Mode switch
    {
        TestAwsBackendMode.Floci => new AmazonCloudWatchLogsClient(FlociCredentials, ConfigureFloci(new AmazonCloudWatchLogsConfig())),
        TestAwsBackendMode.Aws => new AmazonCloudWatchLogsClient(),
        _ => throw NoInMemorySupport("CloudWatch Logs")
    };

    private static NotSupportedException NoInMemorySupport(string service) =>
        new($"{service} is not emulated by the in-memory backend. Set {TestModeEnvironmentVariable}=floci or aws to run tests that use it.");

    /// <summary>
    /// The AWS account id that resources are created under: the bus's fixed account id
    /// in in-memory mode, otherwise the caller identity from STS.
    /// </summary>
    public async Task<string> GetAccountIdAsync()
    {
        if (_bus is not null)
        {
            return _bus.CurrentAccountId;
        }

        using var stsClient = Mode == TestAwsBackendMode.Floci
            ? new AmazonSecurityTokenServiceClient(FlociCredentials, ConfigureFloci(new AmazonSecurityTokenServiceConfig()))
            : new AmazonSecurityTokenServiceClient();
        return (await stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest())).Account;
    }
}

/// <summary>
/// A fact that only runs against real AWS infrastructure — real AWS or the Floci emulator
/// (<c>AWS_MESSAGING_TEST_MODE=aws</c> or <c>floci</c>); skipped on the in-memory backend.
/// For tests exercising services the in-memory backend cannot emulate (e.g. Lambda).
/// </summary>
public sealed class AWSFactAttribute : FactAttribute
{
    public AWSFactAttribute()
    {
        if (!TestAwsBackend.IsRealAwsInfrastructure)
        {
            Skip = $"Requires AWS infrastructure the in-memory backend cannot emulate. Set {TestAwsBackend.TestModeEnvironmentVariable}=floci or aws to run.";
        }
    }
}

/// <summary>
/// A theory that only runs against real AWS infrastructure — real AWS or the Floci emulator
/// (<c>AWS_MESSAGING_TEST_MODE=aws</c> or <c>floci</c>); skipped on the in-memory backend.
/// </summary>
public sealed class AWSTheoryAttribute : TheoryAttribute
{
    public AWSTheoryAttribute()
    {
        if (!TestAwsBackend.IsRealAwsInfrastructure)
        {
            Skip = $"Requires AWS infrastructure the in-memory backend cannot emulate. Set {TestAwsBackend.TestModeEnvironmentVariable}=floci or aws to run.";
        }
    }
}

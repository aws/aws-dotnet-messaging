// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
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

    /// <summary>A local <see href="https://github.com/aws/aws-emulator">AWS Emulator</see> container,
    /// a close approximation of the real AWS APIs. Start it with
    /// <c>docker compose -f test/AWS.Messaging.IntegrationTests/docker-compose.yml up -d --build --wait</c>.</summary>
    Emulator,

    /// <summary>Real AWS resources, using the ambient credential chain.</summary>
    Aws
}

/// <summary>
/// Provides the AWS service clients that the integration tests run against, in one of three modes
/// (selected via the <c>AWS_MESSAGING_TEST_MODE</c> environment variable):
/// <list type="bullet">
/// <item><c>local</c> / unset — in-memory LocalSqsSnsMessaging bus; <c>dotnet test</c> needs no AWS account.</item>
/// <item><c>emulator</c> — a local AWS Emulator container at <c>https://127.0.0.1:9297</c>
/// (override with <c>AWS_EMULATOR_SERVICE_URL</c>).</item>
/// <item><c>aws</c> — real AWS resources.</item>
/// </list>
/// </summary>
public sealed class TestAwsBackend
{
    public const string TestModeEnvironmentVariable = "AWS_MESSAGING_TEST_MODE";
    public const string EmulatorServiceUrlEnvironmentVariable = "AWS_EMULATOR_SERVICE_URL";
    public const string DefaultEmulatorServiceUrl = "https://127.0.0.1:9297";

    /// <summary>
    /// The signing region for requests against the emulator. It keeps a single set of resources
    /// regardless of region, but the AWS SDK requires a region to sign requests.
    /// </summary>
    private const string EmulatorRegion = "us-east-1";

    /// <summary>
    /// Dummy credentials for the emulator, which docker-compose.yml starts with request
    /// authentication off (<c>--auth off</c>), so any key is accepted.
    /// </summary>
    private static readonly AWSCredentials EmulatorCredentials = new BasicAWSCredentials("emulator", "emulator");

    public static TestAwsBackendMode Mode =>
        Environment.GetEnvironmentVariable(TestModeEnvironmentVariable)?.ToLowerInvariant() switch
        {
            "aws" => TestAwsBackendMode.Aws,
            "emulator" => TestAwsBackendMode.Emulator,
            _ => TestAwsBackendMode.InMemory
        };

    /// <summary>
    /// Why the Lambda integration tests can't run on the current backend, or null when they can.
    /// They deploy and run real .NET Lambda functions, which needs real AWS: the in-memory bus has no
    /// Lambda at all, and AWS Emulator implements the Lambda APIs but can only execute Python and
    /// Node.js functions (creating a <c>dotnet8</c> function succeeds, but invoking it fails with
    /// <c>InvalidRuntimeException</c>). Tests are gated on this via
    /// <see cref="LambdaFactAttribute"/>/<see cref="LambdaTheoryAttribute"/>.
    /// </summary>
    public static string? LambdaSkipReason => Mode switch
    {
        TestAwsBackendMode.Aws => null,
        TestAwsBackendMode.Emulator => $"AWS Emulator cannot execute .NET Lambda functions (it runs only Python and Node.js). Set {TestModeEnvironmentVariable}=aws to run.",
        _ => $"The in-memory backend does not emulate Lambda. Set {TestModeEnvironmentVariable}=aws to run."
    };

    /// <summary>
    /// The AWS Emulator service endpoint, overridable via <c>AWS_EMULATOR_SERVICE_URL</c>.
    /// </summary>
    public static string EmulatorServiceUrl =>
        Environment.GetEnvironmentVariable(EmulatorServiceUrlEnvironmentVariable) is { Length: > 0 } url
            ? url
            : DefaultEmulatorServiceUrl;

    private readonly InMemoryAwsBus? _bus;

    public TestAwsBackend()
    {
        if (Mode == TestAwsBackendMode.InMemory)
        {
            _bus = new InMemoryAwsBus();
        }
    }

    private static TConfig ConfigureEmulator<TConfig>(TConfig config) where TConfig : ClientConfig
    {
        config.ServiceURL = EmulatorServiceUrl;
        config.AuthenticationRegion = EmulatorRegion;
        config.HttpClientFactory = EmulatorHttpClientFactory.Instance;
        return config;
    }

    /// <summary>
    /// Creates HTTP clients that trust the emulator's TLS certificate. The emulator only serves
    /// HTTPS, with a self-signed certificate that it generates afresh on every start (inside the
    /// container), so there is no stable CA to install. Instead, certificate errors are ignored
    /// for loopback endpoints only — the emulator's port is published on 127.0.0.1 — and normal
    /// validation applies everywhere else.
    /// </summary>
    private sealed class EmulatorHttpClientFactory : HttpClientFactory
    {
        public static readonly EmulatorHttpClientFactory Instance = new();

        public override HttpClient CreateHttpClient(IClientConfig clientConfig) =>
            new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None || IsLoopback(request.RequestUri)
            });

        private static bool IsLoopback(Uri? uri) =>
            uri is not null && (uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)));
    }

    public IAmazonSQS CreateSqsClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateSqsClient(),
        TestAwsBackendMode.Emulator => new AmazonSQSClient(EmulatorCredentials, ConfigureEmulator(new AmazonSQSConfig())),
        _ => new AmazonSQSClient()
    };

    public IAmazonSimpleNotificationService CreateSnsClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateSnsClient(),
        TestAwsBackendMode.Emulator => new AmazonSimpleNotificationServiceClient(EmulatorCredentials, ConfigureEmulator(new AmazonSimpleNotificationServiceConfig())),
        _ => new AmazonSimpleNotificationServiceClient()
    };

    public IAmazonEventBridge CreateEventBridgeClient() => Mode switch
    {
        TestAwsBackendMode.InMemory => _bus!.CreateEventBridgeClient(),
        TestAwsBackendMode.Emulator => new AmazonEventBridgeClient(EmulatorCredentials, ConfigureEmulator(new AmazonEventBridgeConfig())),
        _ => new AmazonEventBridgeClient()
    };

    // The services below have no in-memory emulation. They're only used by the Lambda tests,
    // which are gated on LambdaSkipReason; the emulator clients are kept so those tests can be
    // enabled there once the emulator can run .NET functions.

    public IAmazonLambda CreateLambdaClient() => Mode switch
    {
        TestAwsBackendMode.Emulator => new AmazonLambdaClient(EmulatorCredentials, ConfigureEmulator(new AmazonLambdaConfig())),
        TestAwsBackendMode.Aws => new AmazonLambdaClient(),
        _ => throw NoInMemorySupport("Lambda")
    };

    public IAmazonS3 CreateS3Client() => Mode switch
    {
        // Path-style addressing: the emulator rejects requests that put the bucket name in the host name.
        TestAwsBackendMode.Emulator => new AmazonS3Client(EmulatorCredentials, ConfigureEmulator(new AmazonS3Config { ForcePathStyle = true })),
        TestAwsBackendMode.Aws => new AmazonS3Client(),
        _ => throw NoInMemorySupport("S3")
    };

    public IAmazonIdentityManagementService CreateIAMClient() => Mode switch
    {
        TestAwsBackendMode.Emulator => new AmazonIdentityManagementServiceClient(EmulatorCredentials, ConfigureEmulator(new AmazonIdentityManagementServiceConfig())),
        TestAwsBackendMode.Aws => new AmazonIdentityManagementServiceClient(),
        _ => throw NoInMemorySupport("IAM")
    };

    public IAmazonCloudWatchLogs CreateCloudWatchLogsClient() => Mode switch
    {
        TestAwsBackendMode.Emulator => new AmazonCloudWatchLogsClient(EmulatorCredentials, ConfigureEmulator(new AmazonCloudWatchLogsConfig())),
        TestAwsBackendMode.Aws => new AmazonCloudWatchLogsClient(),
        _ => throw NoInMemorySupport("CloudWatch Logs")
    };

    private static NotSupportedException NoInMemorySupport(string service) =>
        new($"{service} is not emulated by the in-memory backend. Set {TestModeEnvironmentVariable}=emulator or aws to run tests that use it.");

    /// <summary>
    /// The AWS account id that resources are created under: the bus's fixed account id
    /// in in-memory mode, otherwise the caller identity from STS.
    /// </summary>
    public async Task<string> GetAccountIdAsync(CancellationToken cancellationToken = default)
    {
        if (_bus is not null)
        {
            return _bus.CurrentAccountId;
        }

        using var stsClient = Mode == TestAwsBackendMode.Emulator
            ? new AmazonSecurityTokenServiceClient(EmulatorCredentials, ConfigureEmulator(new AmazonSecurityTokenServiceConfig()))
            : new AmazonSecurityTokenServiceClient();
        var identity = await stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest(), cancellationToken);
        if (string.IsNullOrEmpty(identity.Account))
        {
            throw new InvalidOperationException($"STS GetCallerIdentity returned no account id for the {Mode} backend.");
        }

        return identity.Account;
    }
}

/// <summary>
/// A fact for tests that deploy and run .NET Lambda functions; skipped on backends that can't
/// execute them (see <see cref="TestAwsBackend.LambdaSkipReason"/>).
/// </summary>
public sealed class LambdaFactAttribute : FactAttribute
{
    public LambdaFactAttribute()
    {
        if (TestAwsBackend.LambdaSkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// A theory for tests that deploy and run .NET Lambda functions; skipped on backends that can't
/// execute them (see <see cref="TestAwsBackend.LambdaSkipReason"/>).
/// </summary>
public sealed class LambdaTheoryAttribute : TheoryAttribute
{
    public LambdaTheoryAttribute()
    {
        if (TestAwsBackend.LambdaSkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AWS.Messaging.Configuration;
using AWS.Messaging.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AWS.Messaging.UnitTests;

public class MessageSourceHandlerTests
{
    private readonly Mock<IEnvironmentManager> _environmentManager;
    private readonly Mock<IDnsManager> _dnsManager;
    private readonly Mock<IEC2InstanceMetadataManager> _ec2InstanceMetadataManager;
    private readonly Mock<IECSContainerMetadataManager> _ecsContainerMetadataManager;
    private readonly IMessageConfiguration _messageConfiguration;
    private readonly Mock<ILogger<MessageSourceHandler>> _logger;

    public MessageSourceHandlerTests()
    {
        _environmentManager = new Mock<IEnvironmentManager>();
        _dnsManager = new Mock<IDnsManager>();
        _ec2InstanceMetadataManager = new Mock<IEC2InstanceMetadataManager>();
        _ecsContainerMetadataManager = new Mock<IECSContainerMetadataManager>();
        _messageConfiguration = new MessageConfiguration();
        _logger = new Mock<ILogger<MessageSourceHandler>>();
    }

    [Fact]
    public async Task MessageSourceIsSet()
    {
        _messageConfiguration.Source = "/aws/messaging";

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging", messageSource.OriginalString);
    }

    [Fact]
    public async Task MessageSourceAndSuffixIsSet()
    {
        _messageConfiguration.Source = "/aws/messaging";
        _messageConfiguration.SourceSuffix = "/suffix";

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging/suffix", messageSource.OriginalString);
    }

    [Fact]
    public async Task MessageSourceAndSuffixIsSet_SourceDoesntEndInSlash_SuffixDoesntStartWithSlash()
    {
        _messageConfiguration.Source = "/aws/messaging";
        _messageConfiguration.SourceSuffix = "suffix";

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging/suffix", messageSource.OriginalString);
    }

    [Fact]
    public async Task MessageSourceAndSuffixIsSet_SourceEndsInSlash_SuffixStartsWithSlash()
    {
        _messageConfiguration.Source = "/aws/messaging/";
        _messageConfiguration.SourceSuffix = "/suffix";

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging/suffix", messageSource.OriginalString);
    }

    [Fact]
    public async Task MessageSourceAndSuffixIsSet_SourceAndSuffixHaveWhitespace()
    {
        _messageConfiguration.Source = " /aws/messaging/ ";
        _messageConfiguration.SourceSuffix = " /suffix ";

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging/suffix", messageSource.OriginalString);
    }

    [Fact]
    public async Task MessageSourceNotSet_RunningLocally()
    {
        _ecsContainerMetadataManager.Setup(x => x.GetContainerTaskMetadata()).ReturnsAsync(new Dictionary<string, object>());
        _dnsManager.Setup(x => x.GetHostName()).Returns("local");

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/DNSHostName/local", messageSource.ToString());
    }

    [Fact]
    public async Task MessageSourceNotSet_SuffixSet_RunningLocally()
    {
        _messageConfiguration.SourceSuffix = "/suffix";
        _ecsContainerMetadataManager.Setup(x => x.GetContainerTaskMetadata()).ReturnsAsync(new Dictionary<string, object>());
        _dnsManager.Setup(x => x.GetHostName()).Returns("local");

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/DNSHostName/local/suffix", messageSource.ToString());
    }

    [Fact]
    public async Task MessageSourceNotSet_RunningInLambda()
    {
        _environmentManager.Setup(x => x.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")).Returns("lambda");

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/AWSLambda/lambda", messageSource.ToString());
    }

    [Fact]
    public async Task MessageSourceNotSet_RunningInECS()
    {
        _ecsContainerMetadataManager.Setup(x => x.GetContainerTaskMetadata()).ReturnsAsync(new Dictionary<string, object>
        {
            { "Cluster", "cluster" },
            { "TaskARN", "taskArn" }
        });

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/AmazonECS/cluster/taskArn", messageSource.ToString());
    }

    [Fact]
    public async Task MessageSourceNotSet_RunningInEC2()
    {
        _ecsContainerMetadataManager.Setup(x => x.GetContainerTaskMetadata()).ReturnsAsync(new Dictionary<string, object>());
        _ec2InstanceMetadataManager.Setup(x => x.InstanceId).Returns("instanceId");

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/AmazonEC2/instanceId", messageSource.ToString());
    }

    [Fact]
    public async Task MessageSourceNotSet_DnsThrowsException()
    {
        _ecsContainerMetadataManager.Setup(x => x.GetContainerTaskMetadata()).ReturnsAsync(new Dictionary<string, object>());
        _dnsManager.Setup(x => x.GetHostName()).Throws<Exception>();

        var messageSourceHandler = new MessageSourceHandler(
            _environmentManager.Object,
            _dnsManager.Object,
            _ecsContainerMetadataManager.Object,
            _ec2InstanceMetadataManager.Object,
            _messageConfiguration,
            _logger.Object
            );

        var messageSource = await messageSourceHandler.ComputeMessageSource();

        Assert.Equal("/aws/messaging", messageSource.ToString());
    }
}

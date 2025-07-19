// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AWS.Messaging.Configuration;
using AWS.Messaging.Telemetry.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SubscriberService.MessageHandlers;
using SubscriberService.Middleware;
using SubscriberService.Models;

await Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
    })
    .ConfigureAppConfiguration(configuration =>
    {
        configuration.AddJsonFile("appsettings.json");
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAWSMessageBus(builder =>
        {
            // To load the configuration from appsettings.json instead of the code below, uncomment this and remove the following lines.
            // builder.LoadConfigurationFromSettings(context.Configuration);

            var mpfQueueUrl = context.Configuration["AWS:Resources:MPFQueueUrl"];
            if (string.IsNullOrEmpty(mpfQueueUrl))
                throw new InvalidOperationException("Missing required configuration parameter 'AWS:Resources:MPFQueueUrl'.");

            builder.AddMiddleware<SampleMiddleware>();

            builder.AddSQSPoller(mpfQueueUrl);
            builder.AddMessageHandler<ChatMessageHandler, ChatMessage>("chatMessage");

            builder.ConfigureSerializationOptions(options =>
            {
                options.SystemTextJsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };
            });

            // Optional: Configure the backoff policy used by the SQS Poller.
            builder.ConfigureBackoffPolicy(options =>
            {
                options.UseCappedExponentialBackoff(x =>
                {
                    x.CapBackoffTime = 60;
                });
            });

            // Optional: Configure a PollingControlToken, you can call Start()/Stop() to start and stop message processing, by default it will be started
            builder.ConfigurePollingControlToken(new PollingControlToken
            {
                // Optional: Set how frequently it will check for changes to the state of the PollingControlToken
                PollingWaitTime = TimeSpan.FromMilliseconds(200)
            });

            // Logging data messages is disabled by default to protect sensitive user data. If you want this enabled, uncomment the line below.
            // builder.EnableMessageContentLogging();
        })
        .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("SubscriberService"))
            .WithTracing(tracing => tracing
                .AddAWSMessagingInstrumentation()
                .AddConsoleExporter());
    })
    .Build()
    .RunAsync();

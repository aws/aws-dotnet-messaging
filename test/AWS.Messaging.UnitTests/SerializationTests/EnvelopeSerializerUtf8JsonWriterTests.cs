// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AWS.Messaging.UnitTests.SerializationTests;

public class EnvelopeSerializerUtf8JsonWriterTests : EnvelopeSerializerTestsBase
{
    protected override bool EnableExperimentalFeatures => true;

    [Fact]
    public override void EnvelopeSerializer_RegistersCorrectly()
    {
        {
            // ARRANGE
            var serviceProvider = _serviceCollection.BuildServiceProvider();
            // ACT
            var envelopeSerializer = serviceProvider.GetService<IEnvelopeSerializer>();
            // ASSERT
            Assert.NotNull(envelopeSerializer);

            Assert.IsType<EnvelopeSerializerUtf8JsonWriter>(envelopeSerializer);
        }
    }
}


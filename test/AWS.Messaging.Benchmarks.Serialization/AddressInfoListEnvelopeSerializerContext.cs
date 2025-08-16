using System.Text.Json.Serialization;
using AWS.Messaging.Benchmarks.Serialization;

[JsonSerializable(typeof(AddressInfoListEnvelope))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
public partial class AddressInfoListEnvelopeSerializerContext : JsonSerializerContext
{
}

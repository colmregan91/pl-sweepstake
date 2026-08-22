using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sweepstake.Core;

/// <summary>
/// Writes timestamps as "2026-08-22T18:00:00Z", the shape documented for stats.json in
/// CLAUDE.md. The default converter would emit "+00:00" and seven fractional digits, which is
/// equivalent but noisier in a file that gets committed and read by people.
/// </summary>
public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString()
            ?? throw new JsonException("Expected an ISO 8601 timestamp, found null.");

        // Accept anything ISO 8601 on the way in -- "Z", "+00:00" and offsets all round-trip.
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}

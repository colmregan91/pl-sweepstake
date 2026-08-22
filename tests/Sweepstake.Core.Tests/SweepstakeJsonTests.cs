using System.Globalization;
using System.Text.Json;
using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

public class SweepstakeJsonTests
{
    private static readonly StatsFile Sample = new(
        DateTimeOffset.Parse("2026-08-22T18:00:00Z", CultureInfo.InvariantCulture),
        "2026",
        "espn-core-api",
        new Dictionary<string, PlayerStat>(StringComparer.Ordinal)
        {
            ["235662"] = new("Alexander Isak", 3, 1),
            ["289155"] = new("Benjamin Šeško", 0, 2),
        });

    [Fact]
    public void Stats_are_written_in_the_shape_documented_in_CLAUDE_md()
    {
        using var doc = JsonDocument.Parse(SweepstakeJson.WriteStats(Sample));
        var root = doc.RootElement;

        Assert.Equal("2026", root.GetProperty("season").GetString());
        Assert.Equal("espn-core-api", root.GetProperty("source").GetString());
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-22T18:00:00Z", CultureInfo.InvariantCulture),
            root.GetProperty("generatedUtc").GetDateTimeOffset());

        var isak = root.GetProperty("players").GetProperty("235662");
        Assert.Equal("Alexander Isak", isak.GetProperty("name").GetString());
        Assert.Equal(3, isak.GetProperty("goals").GetInt32());
        Assert.Equal(1, isak.GetProperty("assists").GetInt32());
    }

    [Fact]
    public void The_timestamp_is_written_as_a_plain_z_suffixed_utc_instant()
    {
        // CLAUDE.md documents "2026-08-22T18:00:00Z". The default converter would emit
        // "+00:00" and seven fractional digits.
        Assert.Contains("\"generatedUtc\": \"2026-08-22T18:00:00Z\"", SweepstakeJson.WriteStats(Sample), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_utc_timestamp_is_normalised_before_writing()
    {
        var local = Sample with
        {
            GeneratedUtc = new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.FromHours(1)),
        };

        Assert.Contains("\"generatedUtc\": \"2026-08-22T18:00:00Z\"", SweepstakeJson.WriteStats(local), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-08-22T18:00:00Z")]
    [InlineData("2026-08-22T18:00:00+00:00")]
    [InlineData("2026-08-22T19:00:00+01:00")]
    [InlineData("2026-08-22T18:00:00.1234567Z")]
    public void Any_iso8601_timestamp_can_be_read_back(string timestamp)
    {
        var json = $$"""
            { "generatedUtc": "{{timestamp}}", "season": "2026", "source": "t", "players": {} }
            """;

        var actual = SweepstakeJson.ReadStats(json).GeneratedUtc.ToUniversalTime();
        var toTheSecond = new DateTimeOffset(
            actual.Year, actual.Month, actual.Day, actual.Hour, actual.Minute, actual.Second, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.Zero), toTheSecond);
    }

    [Fact]
    public void Property_names_are_camel_case_and_id_keys_are_left_alone()
    {
        var json = SweepstakeJson.WriteStats(Sample);

        Assert.Contains("\"generatedUtc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"235662\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GeneratedUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Accented_names_are_written_as_utf8_not_escaped()
    {
        // A committed file full of Šeško is valid but unreviewable in a diff.
        var json = SweepstakeJson.WriteStats(Sample);

        Assert.Contains("Benjamin Šeško", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0160", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Stats_round_trip()
    {
        var round = SweepstakeJson.ReadStats(SweepstakeJson.WriteStats(Sample));

        Assert.Equal(Sample.GeneratedUtc, round.GeneratedUtc);
        Assert.Equal(Sample.Season, round.Season);
        Assert.Equal(Sample.Source, round.Source);
        Assert.Equal("Benjamin Šeško", round.Players["289155"].Name);
        Assert.Equal(2, round.Players["289155"].Assists);
    }

    [Fact]
    public void Reading_picks_tolerates_the_comment_and_competition_fields()
    {
        // picks.json carries "$comment" and "competition", neither of which the model has.
        const string json = """
            {
              "$comment": "explanatory prose",
              "season": "2026",
              "seasonLabel": "2026/27",
              "competition": "eng.1",
              "players": { "1": { "name": "A Player", "club": "Some FC" } },
              "entrants": [
                {
                  "name": "Solo",
                  "goalscorers": [ { "espnId": "1", "odds": "8/1" } ],
                  "assisters":   [ { "espnId": "1", "odds": "8/1" } ]
                }
              ]
            }
            """;

        var picks = SweepstakeJson.ReadPicks(json);

        Assert.Equal("2026/27", picks.SeasonLabel);
        Assert.Equal("A Player", picks.Players["1"].Name);
        Assert.Null(picks.Players["1"].SheetName);
        Assert.Equal("1", picks.Entrants.Single().Goalscorers.Single().EspnId);
    }
}

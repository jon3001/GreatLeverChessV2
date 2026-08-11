// Session 3: capture-to-content transform.
//
// Reads captured ECF LMS JSON from ecf-captures/lms/, writes Hugo content
// files to content/fixtures/. Batch transform over static files on disk —
// no HTTP, no auth, no scheduling, no idempotency logic. That belongs to
// EcfLmsSync later.
//
// Run from the repo root:
//   dotnet run --project tools/ecf-lms-transform

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

const string CapturesRoot = "ecf-captures/lms";
const string DataRoot = "data";
const string OutputRoot = "content/fixtures";

// --- org_id -> organisation slug, and season resolution -----------------
// own_team.json and source_ids.json are both keyed BY organisation slug, but
// a capture path only ever gives us the numeric org_id. Originally two flat
// files (data/organisation.json, data/season.json), each keyed directly by
// a raw source id with no namespace. Consolidated into data/source_ids.json
// on 11 August 2026, namespaced by source.system — see docs/content-model.md
// §4. organisationSlugs and seasonLookup below are just the two sub-maps
// pulled out of that one file; everything downstream that consumes them is
// unchanged.
var sourceIds = JsonSerializer.Deserialize<SourceIdsFile>(
    File.ReadAllText(Path.Combine(DataRoot, "source_ids.json")))
    ?? throw new InvalidOperationException("data/source_ids.json did not parse");

var organisationSlugs = sourceIds.Ecflms.Organisations;
var seasonLookup = sourceIds.Ecflms.Seasons;

var ownTeamLookup = JsonSerializer.Deserialize<Dictionary<string, List<OwnTeamEntry>>>(
    File.ReadAllText(Path.Combine(DataRoot, "own_team.json")))
    ?? throw new InvalidOperationException("data/own_team.json did not parse");

var yamlSerializer = new SerializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .WithTypeConverter(new RatingCodeConverter())
    .Build();

var captureFiles = Directory.GetFiles(CapturesRoot, "event-*.json", SearchOption.AllDirectories)
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToList();

int written = 0;
int skippedNotOurs = 0;

foreach (var captureFile in captureFiles)
{
    var (orgId, seasonId, eventId) = ParseCapturePath(captureFile);

    if (!organisationSlugs.TryGetValue(orgId.ToString(CultureInfo.InvariantCulture), out var organisation))
    {
        Console.WriteLine($"SKIP {captureFile}: no organisation slug configured for org_id {orgId}");
        continue;
    }

    if (!ownTeamLookup.TryGetValue(organisation, out var ownTeamEntries))
        throw new InvalidOperationException($"data/own_team.json has no entry for organisation '{organisation}'");

    if (!seasonLookup.TryGetValue(organisation, out var orgSeasons) ||
        !orgSeasons.TryGetValue(seasonId.ToString(CultureInfo.InvariantCulture), out var seasonSlug))
    {
        throw new InvalidOperationException(
            $"data/source_ids.json has no entry under ecflms.seasons for organisation '{organisation}', season_id {seasonId}");
    }

    var payload = JsonSerializer.Deserialize<EventPayload>(File.ReadAllText(captureFile))
        ?? throw new InvalidOperationException($"{captureFile} did not parse");

    var eventSlug = Slugify(payload.EventName);
    var outputDir = Path.Combine(OutputRoot, seasonSlug);
    Directory.CreateDirectory(outputDir);

    int eventSkipped = 0;

    foreach (var fixture in payload.Fixtures)
    {
        var homeTeam = fixture.HomeTeam?.Trim();
        var awayTeam = fixture.AwayTeam?.Trim();

        var homeIsOwn = homeTeam is not null && ownTeamEntries.Any(e => e.RawName == homeTeam);
        var awayIsOwn = awayTeam is not null && ownTeamEntries.Any(e => e.RawName == awayTeam);

        if (!homeIsOwn && !awayIsOwn)
        {
            // NOT the same thing as docs/content-model.md §2.3's "fail loudly"
            // case. That note was written before a full event draw was
            // inspected end to end: event-08865 alone has 7 fixtures and
            // only 1 involves Great Lever, event-08866 has 30 and only 10
            // do. Most fixtures in any captured event are between other
            // clubs. Treating every non-Great-Lever fixture as a hard
            // error would make the transform unusable on real data — so
            // this is a deliberate, counted skip instead. §2.3 needs a
            // rewrite; see the note above the output for what I'd propose.
            eventSkipped++;
            continue;
        }

        if (homeIsOwn && awayIsOwn)
        {
            throw new InvalidOperationException(
                $"Fixture {fixture.FixtureId}: both '{homeTeam}' and '{awayTeam}' matched " +
                $"own_team.json for '{organisation}' — Great Lever can't play itself.");
        }

        var venue = homeIsOwn ? "home" : "away";
        var ownTeamSlug = ownTeamEntries.First(e => e.RawName == (homeIsOwn ? homeTeam : awayTeam)).Slug;
        var opponent = homeIsOwn ? awayTeam! : homeTeam!;

        var frontMatter = new FixtureFrontMatter
        {
            Date = fixture.Date,
            Time = fixture.Time,
            Round = fixture.Round,
            Organisation = organisation,
            Event = eventSlug,
            EventType = payload.EventType,
            Seasons = new List<string> { seasonSlug },
            OwnTeam = ownTeamSlug,
            Opponent = opponent,
            Venue = venue,
            HomeScore = fixture.HomeScore,
            AwayScore = fixture.AwayScore,
            ScoreFor = homeIsOwn ? fixture.HomeScore : fixture.AwayScore,
            ScoreAgainst = homeIsOwn ? fixture.AwayScore : fixture.HomeScore,
            DeclaredBoardCount = fixture.BoardCount,
            Boards = fixture.Games.Select(g => BuildBoard(g, homeIsOwn)).ToList(),
            Source = new SourceBlock
            {
                OrgId = orgId,
                SeasonId = seasonId,
                EventId = eventId,
                FixtureId = fixture.FixtureId,
            },
        };

        var outputPath = Path.Combine(outputDir, $"ecflms-{fixture.FixtureId}.md");
        var yamlText = yamlSerializer.Serialize(frontMatter);
        File.WriteAllText(outputPath, $"---\n{yamlText}---\n");
        written++;
    }

    Console.WriteLine($"{captureFile}: {payload.Fixtures.Count - eventSkipped} written, {eventSkipped} skipped (not a Great Lever fixture)");
    skippedNotOurs += eventSkipped;
}

Console.WriteLine();
Console.WriteLine($"Total: {written} fixture files written, {skippedNotOurs} fixtures skipped (not ours)");

// --- helpers -------------------------------------------------------------

static (int orgId, int seasonId, int eventId) ParseCapturePath(string path)
{
    var segments = path.Replace('\\', '/').Split('/');

    var orgSegment = segments.FirstOrDefault(s => s.StartsWith("org-", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"'{path}' has no org-{{id}} path segment");
    var seasonSegment = segments.FirstOrDefault(s => s.StartsWith("season-", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"'{path}' has no season-{{id}} path segment");
    var eventSegment = segments.FirstOrDefault(s => s.StartsWith("event-", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"'{path}' has no event-{{id}} path segment");

    // Parse the numeric value only — the zero-padding (org/season to 4
    // digits, event to 5) is a sort-order convenience, never meaningful.
    var orgId = int.Parse(orgSegment["org-".Length..], CultureInfo.InvariantCulture);
    var seasonId = int.Parse(seasonSegment["season-".Length..], CultureInfo.InvariantCulture);
    var eventFileName = Path.GetFileNameWithoutExtension(eventSegment);
    var eventId = int.Parse(eventFileName["event-".Length..], CultureInfo.InvariantCulture);

    return (orgId, seasonId, eventId);
}

static string Slugify(string value)
{
    var lowered = value.Trim().ToLowerInvariant();
    var hyphenated = Regex.Replace(lowered, @"[^a-z0-9]+", "-");
    return hyphenated.Trim('-');
}

static BoardFrontMatter BuildBoard(GamePayload game, bool homeIsOwn)
{
    // "Own" colour: if we're the home side, it's home_colour as-is;
    // if we're away, it's the opposite of home_colour (the payload
    // only ever states colour from the home side's perspective).
    var ownColour = homeIsOwn
        ? game.HomeColour
        : (game.HomeColour == "W" ? "B" : "W");

    var ownPlayer = homeIsOwn ? game.HomePlayer : game.AwayPlayer;
    var opponentPlayer = homeIsOwn ? game.AwayPlayer : game.HomePlayer;

    return new BoardFrontMatter
    {
        Board = game.Board,
        Colour = ownColour,
        Result = game.Result,
        Player = MapPlayer(ownPlayer),
        Opponent = MapPlayer(opponentPlayer),
    };
}

static PlayerFrontMatter MapPlayer(PlayerPayload player) => new()
{
    RatingCode = player.RatingCode,
    LmsId = player.LmsId,
    // Cosmetic trim only — rating_code, not name, is the aggregation key
    // (spike finding: player identity is ID-based, not name-based).
    Name = player.Name.Trim(),
    Rating = player.Rating,
    SecondaryRating = player.SecondaryRating,
};

// --- front matter model (output shape — matches docs/content-model.md §1) -

public sealed class FixtureFrontMatter
{
    [YamlMember(Order = 1)]
    public string Date { get; set; } = "";

    // Forced double-quoted: "19:30" unquoted is a valid YAML 1.1 plain
    // scalar that a Go YAML parser reads as sexagesimal (base 60) —
    // 19:30 becomes the integer 1170. This is exactly why the schema
    // doc shows it quoted.
    [YamlMember(Order = 2, ScalarStyle = ScalarStyle.DoubleQuoted)]
    public string? Time { get; set; }

    [YamlMember(Order = 3)]
    public int Round { get; set; }

    [YamlMember(Order = 4)]
    public string Organisation { get; set; } = "";

    [YamlMember(Order = 5)]
    public string Event { get; set; } = "";

    [YamlMember(Order = 6)]
    public string EventType { get; set; } = "";

    [YamlMember(Order = 7)]
    public List<string> Seasons { get; set; } = new();

    [YamlMember(Order = 8)]
    public string OwnTeam { get; set; } = "";

    [YamlMember(Order = 9)]
    public string Opponent { get; set; } = "";

    [YamlMember(Order = 10)]
    public string Venue { get; set; } = "";

    [YamlMember(Order = 11)]
    public double HomeScore { get; set; }

    [YamlMember(Order = 12)]
    public double AwayScore { get; set; }

    [YamlMember(Order = 13)]
    public double ScoreFor { get; set; }

    [YamlMember(Order = 14)]
    public double ScoreAgainst { get; set; }

    [YamlMember(Order = 15)]
    public int DeclaredBoardCount { get; set; }

    [YamlMember(Order = 16)]
    public List<BoardFrontMatter> Boards { get; set; } = new();

    [YamlMember(Order = 17)]
    public SourceBlock Source { get; set; } = new();
}

public sealed class BoardFrontMatter
{
    [YamlMember(Order = 1)]
    public int Board { get; set; }

    [YamlMember(Order = 2)]
    public string Colour { get; set; } = "";

    [YamlMember(Order = 3)]
    public string Result { get; set; } = "";

    [YamlMember(Order = 4)]
    public PlayerFrontMatter Player { get; set; } = new();

    [YamlMember(Order = 5)]
    public PlayerFrontMatter Opponent { get; set; } = new();
}

public sealed class PlayerFrontMatter
{
    // RatingCode is a struct wrapper, not string?, so that a null code
    // still reaches RatingCodeConverter below instead of being short-
    // circuited into a plain null scalar by YamlDotNet before the
    // converter ever runs (a bare string? would skip WriteYaml entirely
    // when null, and there'd be no way to force quoting back on for the
    // non-null case from inside the converter).
    [YamlMember(Order = 1)]
    public RatingCode RatingCode { get; set; }

    [YamlMember(Order = 2)]
    public int LmsId { get; set; }

    [YamlMember(Order = 3, ScalarStyle = ScalarStyle.DoubleQuoted)]
    public string Name { get; set; } = "";

    // Nullable and left null when null: rating: null is a genuine
    // unrated-player state. Never coerce to 0 — 0000 is the LMS site's
    // own display convention for null, not a real rating.
    [YamlMember(Order = 4)]
    public int? Rating { get; set; }

    [YamlMember(Order = 5)]
    public int? SecondaryRating { get; set; }
}

// A null rating_code (sentinel "Default" players, or a real player with no
// ECF code on file yet) must serialize as a true YAML null — same as
// Rating above — not as "", which would misrepresent "unknown" as "known
// to be empty". A plain string? can't do this: forcing ScalarStyle on the
// YamlMember applies even when the value is null, so YamlDotNet quotes the
// empty string instead of emitting null. Wrapping in a struct keeps the
// value non-null at the CLR level, so RatingCodeConverter.WriteYaml always
// runs and can pick the style itself: quoted for a real code (protecting
// codes like "0123A" from being misread as numbers), a true null otherwise.
public readonly struct RatingCode
{
    public string? Value { get; }

    public RatingCode(string? value) => Value = value;

    public static implicit operator RatingCode(string? value) => new(value);
}

public sealed class RatingCodeConverter : IYamlTypeConverter
{
    private static readonly TagName NullTag = "tag:yaml.org,2002:null";

    public bool Accepts(Type type) => type == typeof(RatingCode);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) =>
        throw new NotSupportedException("This transform only ever serializes RatingCode, never reads it back.");

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var ratingCode = (RatingCode)value!;
        if (ratingCode.Value is null)
        {
            // Mirrors the null Scalar YamlDotNet emits internally for a
            // bare null (tag + IsPlainImplicit=true, no ScalarStyle
            // forced) so this renders identically to Rating's blank output.
            emitter.Emit(new Scalar(AnchorName.Empty, NullTag, "", ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false));
        }
        else
        {
            emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ratingCode.Value, ScalarStyle.DoubleQuoted, isPlainImplicit: false, isQuotedImplicit: true));
        }
    }
}

public sealed class SourceBlock
{
    [YamlMember(Order = 1, Alias = "system")]
    public string SourceSystem { get; set; } = "ecflms";

    [YamlMember(Order = 2)]
    public int OrgId { get; set; }

    [YamlMember(Order = 3)]
    public int SeasonId { get; set; }

    [YamlMember(Order = 4)]
    public int EventId { get; set; }

    [YamlMember(Order = 5)]
    public int FixtureId { get; set; }
}

// --- capture JSON model (input shape) -------------------------------------

public sealed class EventPayload
{
    [JsonPropertyName("event_name")]
    public string EventName { get; set; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("fixtures")]
    public List<FixturePayload> Fixtures { get; set; } = new();
}

public sealed class FixturePayload
{
    [JsonPropertyName("fixture_id")]
    public int FixtureId { get; set; }

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("home_team")]
    public string? HomeTeam { get; set; }

    [JsonPropertyName("away_team")]
    public string? AwayTeam { get; set; }

    [JsonPropertyName("home_score")]
    public double HomeScore { get; set; }

    [JsonPropertyName("away_score")]
    public double AwayScore { get; set; }

    [JsonPropertyName("winner")]
    public string Winner { get; set; } = "";

    [JsonPropertyName("board_count")]
    public int BoardCount { get; set; }

    [JsonPropertyName("games")]
    public List<GamePayload> Games { get; set; } = new();
}

public sealed class GamePayload
{
    [JsonPropertyName("board")]
    public int Board { get; set; }

    [JsonPropertyName("home_colour")]
    public string HomeColour { get; set; } = "";

    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonPropertyName("home_player")]
    public PlayerPayload HomePlayer { get; set; } = new();

    [JsonPropertyName("away_player")]
    public PlayerPayload AwayPlayer { get; set; } = new();
}

public sealed class PlayerPayload
{
    [JsonPropertyName("lms_id")]
    public int LmsId { get; set; }

    [JsonPropertyName("rating_code")]
    public string? RatingCode { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("rating")]
    public int? Rating { get; set; }

    [JsonPropertyName("secondary_rating")]
    public int? SecondaryRating { get; set; }
}

public sealed class OwnTeamEntry
{
    [JsonPropertyName("raw_name")]
    public string RawName { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";
}

// --- data/source_ids.json model -------------------------------------------
// Top-level object is namespaced by source.system. Only "ecflms" exists
// today (docs/content-model.md §4); a second source gets its own top-level
// key shaped however that source's ids actually work — this transform only
// ever reads the ecflms branch.

public sealed class SourceIdsFile
{
    [JsonPropertyName("ecflms")]
    public SourceIdsBlock Ecflms { get; set; } = new();
}

public sealed class SourceIdsBlock
{
    // org_id (string) -> organisation slug
    [JsonPropertyName("organisations")]
    public Dictionary<string, string> Organisations { get; set; } = new();

    // organisation slug -> { raw season_id (string) -> normalised season slug }
    [JsonPropertyName("seasons")]
    public Dictionary<string, Dictionary<string, string>> Seasons { get; set; } = new();
}

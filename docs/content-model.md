# Great Lever Chess Club V2 — Fixture Content Model

**Session 2 output — 7 August 2026.** Schema and lookup files settled against
real captured JSON (Bolton Division 1, Central Lancashire Division A, Vaux
Cup, Friendly Development League — 2025/26 season, both leagues). Companion
to `great_lever_chess_website_v2.md` §5, which this document makes concrete
enough to hand to Session 3's transform.

---

## 1. Fixture front matter

One file per fixture, content/fixtures/{slug}.md, build.render: "link" via a cascade in content/fixtures/_index.md — see §2.5 for why link rather than never. Machine-written and
machine-owned — the sync tool is the sole writer; a human never edits a
fixture file directly.

    date: 2025-11-25
    time: "19:30"                      # LMS fixture-level field; CAN BE NULL — see §3.1
    round: 4                           # LMS fixture-level field; meaning varies by event — see §3.2
    organisation: central-lancashire   # normalised org slug, from data/own_team.json's keys
    event: division-a                  # normalised event slug
    event_type: team_league            # team_league | knockout_draw_round
    seasons: ["2025-26"]                # normalised slug, list-valued — from data/season.json — NOT the raw ECF sid — see §2.5
    own_team: great-lever-a            # resolved via data/own_team.json, per organisation
    opponent: Heywood A                # display string, trimmed — no stable ID exists, see §2.3
    venue: home                        # home | away
    home_score: 1                      # source fidelity, as returned — CAN BE NEGATIVE
    away_score: 3
    score_for: 1                       # derived from venue at transform time — see §2.2
    score_against: 3
    declared_board_count: 6            # as declared; may not equal games.length — see spike findings
    boards: []                         # see §1.1
    lms_id: 130772                     # the LMS fixture_id — NOT the same field as boards[].player.lms_id, see §3.5

No `opponent_id`. No `last_synced` (lives in `data/sync-status.json`, unchanged
from the 5 August decision). No `winner` (unreliable — derive outcome from
scores; `games: []` / `winner: "unknown"` in the source is a played/unplayed
signal only, see §2.1, never an outcome signal).

### 1.1 `boards[]`

    boards:
      - board: 3
        colour: W                        # own side's colour, derived from home_colour + venue
        result: draw                     # home_win | away_win | draw | home_default_win
                                         # | away_default_win | double_default
        player:
          rating_code: "143105F"         # primary key — always quoted
          lms_id: 39229
          name: "Lonsdale, Jon I"        # display only; trim whitespace; always quoted
          rating: 1902
          secondary_rating: 1797
        opponent:
          rating_code: "105488A"
          lms_id: 38921
          name: "Adams, Philip"
          rating: 2036
          secondary_rating: 2028

Sentinel player IDs `lms_id: -2` (`"Default"`) and `lms_id: -5` (`"Not Named"`)
are excluded from any player-level aggregation but kept in `boards[]` verbatim
— they're the source data for a future "defaults conceded" statistic. Every
name string must be emitted quoted (`"Null, AJ"` is a real player; unquoted
it's a YAML null literal).

---

## 2. Derivation rules

### 2.1 Played vs. upcoming — corrected 7 August 2026

**Do not derive from "presence of a score."** Confirmed against real data: an
unplayed, postponed, or withdrawn fixture still carries `home_score: 0,
away_score: 0` — a score is present, it's just zero, and a naive check
misreads these as completed 0–0 draws.

**Derive from `games.length > 0`** (equivalently, source `winner` not equal
to `"unknown"` — a valid but previously uncatalogued value, distinct from the
already-known-unreliable `home_win`/`away_win`/`draw`/etc. vocabulary used
for genuine results). Either check works; `games.length` is the more direct
one to build the front matter from since `boards[]` is populated from the
same array.

The API gives no way to distinguish "postponed, will be replayed" from
"conceded/withdrawn, never will be" — both serialise identically
(`0–0`, `winner: "unknown"`, `games: []`). Not solvable from this data;
don't build anything that assumes it's solvable.

### 2.2 `score_for` / `score_against`

Derived from `venue` once, at transform time: `venue: home` → `score_for =
home_score`, `score_against = away_score`; inverted for `venue: away`.
`home_score`/`away_score` remain source-fidelity fields; never assert they
agree with the sum of `boards[]` results (they routinely don't — see spike
findings, defaults are not consistently reconciled by the API).

### 2.3 `own_team` / `opponent` resolution

Match `home_team` and `away_team` against `data/own_team.json`'s entries for
the fixture's `organisation`. Whichever side matches is `own_team` (resolved
to its slug) and determines `venue`; the other side's raw string becomes
`opponent` (trimmed, stored verbatim). No `opponent_id` — confirmed 6 August
2026 that `home_team`/`away_team` are bare strings in every league, with no
underlying identifier of any kind.

### 2.4 `season` resolution and the current-season pointer

Look up `(organisation, raw season_id)` in `data/season.json` → normalised
slug. The raw `sid` is not stored in front matter — once resolved, it's not
needed downstream.

`data/current_season.json` holds the currently-live season slug **per
organisation** (not per team — a season is an organisational property; all of
an org's teams share it). `/fixtures/` and `/results/` templates filter to
fixtures where `season` matches that organisation's entry. An organisation
absent from this file means Great Lever isn't fielding a team there this
cycle — same graceful-degradation shape already used for the sync tool's org
allowlist, not a special case.

Hand-maintained through Stage 1/2, one line flipped at each season rollover.
Stage 3's `EcfLmsSync` can overwrite it wholesale from whichever `season_id`
the API reports `status: active` for, per organisation in its allowlist.

**Field list — replace the existing `season` entry with:**

    seasons: ["2025-26"]               # normalised slug, list-valued — NOT the raw ECF sid — see §2.5

**Corrected 7 August 2026 — Session 2, second half.** Renamed `season` →
`seasons`, and changed from a scalar string to a single-element list. Both
changes are required by Hugo's taxonomy front matter convention, discovered
while wiring the taxonomy declaration in `hugo.toml` — full finding in §2.5.
The value still only ever holds one entry; the list shape is Hugo's
requirement, not a modelling decision, and nothing about the season lookup
files or the eventual transform's logic changes because of it.

---

### §2.5 — Hugo taxonomy wiring: `render: link`, not `render: never`

**Confirmed 7 August 2026 — Session 2, second half.** `hugo.toml` declares:

    [taxonomies]
      season = 'seasons'

Front matter uses the plural key (`seasons`), per Hugo convention — see the
amended field list above.

`content/fixtures/_index.md` cascades build options to every fixture:

    ---
    title: "Fixtures"
    cascade:
      - build:
          render: link
          list: always
    ---

**Why `link`, not `never`.** The delivery plan originally specified
`render: never`. Verified against three hand-created test fixtures that
`render: never` — despite `list: always` — silently excludes pages from
whatever internal collection Hugo scans to build `.Site.Taxonomies`, even
though the same pages still appear correctly in `.Site.RegularPages`.
Isolated by changing only the cascade's `render` value and nothing else:
switching to `render: link` fixed it immediately. `render: link` still
writes no HTML file to disk — a fixture's computed `.Permalink` 404s exactly
as `render: never`'s would have — so the practical outcome (no browsable
per-fixture page) is unchanged. The difference is entirely about which
internal Hugo collection the page counts as a member of, not about anything
a site visitor could observe.

**Governing rule for future sessions:** any page needing taxonomy membership
without its own rendered page uses `render: link`, not `render: never`.

**Also worth knowing:** the reserved front matter object is `build` on the
Hugo version this project uses (0.164.0), not `_build`. Older tutorials and
this plan's own earlier drafts use the pre-rename `_build`, which Hugo
silently ignores rather than erroring on — no warning, no build failure,
just a cascade that quietly does nothing.

---

## 3. Data-quality notes for the Session 3 transform

**3.1 — `time` can be null.** Only observed case: a bye (no opponent drawn
for a knockout slot — `home_team` is also null there). Every other fixture in
both leagues, played or not, carries a constant `"19:30"`, matching an
organisation-level "Match Time" default visible in the LMS's own settings
page. Capture it as planned (the calendar feed needs it), but treat it as
mostly-constant and handle `null` without erroring.

**3.2 — `round` is not comparable across events.** Bolton's Division 1 numbers
real round-robin weeks (1–10). Central Lancashire's Division A and Friendly
Development League set `round: 1` on every fixture — untracked there. Central
Lancashire's Vaux Cup (knockout) uses real round numbers. Capture it
regardless (already decided, cheap, unreconstructable later), but don't build
any ordering or display logic that assumes uniform meaning.

**3.3 — Rearranged fixtures keep `fixture_id` and `round`; `date` updates.**
Confirmed directly in both leagues' live 2025/26 data (Bolton `123667`,
`123671`; Central Lancashire `130780` — each ID-sequenced among fixtures from
one month but actually played months later). `date` should always be read as
"the current scheduled/played date," never assumed frozen at an original
slot.

**3.4 — Postponed and withdrawn fixtures are indistinguishable from the API
alone.** Both serialise as `0–0`, `winner: "unknown"`, `games: []`. See §2.1.

**3.5 — `lms_id` names two unrelated things.** The API's `fixture_id` becomes
front matter's top-level `lms_id`. The API's `player.lms_id` becomes
`boards[].player.lms_id` / `boards[].opponent.lms_id` — a completely
different value, scoped to a player not a fixture. Same field name, no
relationship. Worth a comment in the transform code itself, not just here.

---

## 4. `data/` lookup files

Three files, JSON (matching the format already established by
`data/sync-status.json` and `data/stats/season.json` elsewhere in the spec —
no new format introduced for these).

### `data/own_team.json`

Keyed by organisation. Each entry is a known Great Lever team-name variant
for that organisation, with the slug used in fixture front matter. Bolton
fields one unlabelled team; Central Lancashire fields (at least) three,
lettered.

    {
      "bolton-district": [
        { "raw_name": "Great Lever", "slug": "great-lever" }
      ],
      "central-lancashire": [
        { "raw_name": "Great Lever A", "slug": "great-lever-a" },
        { "raw_name": "Great Lever B", "slug": "great-lever-b" },
        { "raw_name": "Great Lever C", "slug": "great-lever-c" }
      ]
    }

Manchester's `"Great Lever 1"` variant deliberately not included — out of
scope until Stage 4 backfill.

### `data/season.json`

`(organisation, raw ECF season_id)` → normalised slug. Supports many-to-one
(confirmed necessary for Manchester's COVID-era seasons; not exercised by
either row below, but the shape already handles it with no change). Only
current-season rows for tonight's two leagues — historic rows get appended
in the same shape during Stage 4 backfill, keyed by string since JSON object
keys are always strings.

    {
      "bolton-district": {
        "1758": "2025-26"
      },
      "central-lancashire": {
        "1805": "2025-26"
      }
    }

### `data/current_season.json`

The pointer file from §2.4. One row per organisation currently fielding a
Great Lever team; absence means "not playing there this cycle."

    {
      "bolton-district": "2025-26",
      "central-lancashire": "2025-26"
    }

---

## 5. Explicitly out of scope tonight

Hugo taxonomy wiring for `season` (`/archive/{season}/` generation,
`taxonomy.html`/`term.html` templates) — separate sitting, per the delivery
plan's Session 2 split. Nothing above blocks it; these three files are its
prerequisite, not its implementation.
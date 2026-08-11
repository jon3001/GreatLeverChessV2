# Great Lever Chess Club V2 — Fixture Content Model

**Session 2 output — 7 August 2026.** Schema and lookup files settled against
real captured JSON (Bolton Division 1, Central Lancashire Division A, Vaux
Cup, Friendly Development League — 2025/26 season, both leagues). Companion
to `great_lever_chess_website_v2.md` §5, which this document makes concrete
enough to hand to Session 3's transform.

**Revised 11 August 2026, pre-Session 3.** `lms_id` replaced by a nested
`source` block (§2.6), generated filename convention fixed (§1), a fourth
lookup file added (§4), and two new data-quality notes recorded (§3.6, §3.7).
All settled before the transform wrote its first file, so no migration is
implied — but nothing below is committed to the repo yet either.

**Revised again 11 August 2026, during Session 3.** The `own_team`/`opponent`
fail-loudly rule in §2.3 corrected against real captured data — most
fixtures in a full event draw aren't Great Lever's, and that's the normal
case, not an error. `data/organisation.json` (itself only hours old) and
`data/season.json` both retired in favour of a single `data/source_ids.json`,
namespaced by `source.system` — see §4. Two things deliberately recorded as
open questions rather than resolved: §3.8 (`event_type` carries raw LMS
vocabulary, outside the `source` block) and §3.9 (`event` has no lookup file
backing its slug, unlike every other normalised field). Still nothing below
is committed to the repo — Session 3's stop condition hasn't been reached.

---

## 1. Fixture front matter

One file per fixture. `build.render: "link"` via a cascade in
`content/fixtures/_index.md` — see §2.5 for why `link` rather than `never`.
Machine-written and machine-owned — the sync tool is the sole writer; a human
never edits a fixture file directly.

**Path and filename — fixed 11 August 2026:**

    content/fixtures/{season}/ecflms-{fixture_id}.md

The season subdirectory is a filesystem convenience only; it contains no
`_index.md` and is therefore not its own Hugo section. The `ecflms-` prefix
mirrors the `ecf-captures/lms/` provenance convention already used for the
inputs, and matches `source.system` in the front matter below.

Three properties the filename must keep, in priority order:

1. **Deterministic and stable.** `EcfLmsSync` will later match-and-overwrite
   the existing file. A filename derived from a mutable field — date or
   opponent name — moves when a fixture is rearranged (§3.3), turning every
   subsequent sync into a duplicate create rather than an update.
2. **Derivable from the source identifier alone.** Nothing else in the
   payload is guaranteed stable.
3. **Source-tagged.** Cheap here specifically because fixtures render no page
   (§2.5), so the filename is never a user-facing slug — it is purely a git
   and idempotency key. No readability or SEO cost to weigh a prefix against.

**Front matter:**

    date: 2025-11-25
    time: "19:30"                      # LMS fixture-level field; CAN BE NULL — see §3.1
    round: 4                           # LMS fixture-level field; meaning varies by event — see §3.2
    organisation: central-lancashire   # normalised org slug, resolved from source.org_id via data/source_ids.json — see §2.4
    event: division-a                  # normalised event slug — slugified from event_name, no lookup file — see §3.9
    event_type: team_league            # team_league | knockout_draw_round — raw LMS vocabulary — see §3.8
    seasons: ["2025-26"]               # normalised slug, list-valued — from data/source_ids.json — NOT the raw ECF sid — see §2.5
    own_team: great-lever-a            # resolved via data/own_team.json, per organisation
    opponent: Heywood A                # display string, trimmed — no stable ID exists, see §2.3
    venue: home                        # home | away
    home_score: 1                      # source fidelity, as returned — CAN BE NEGATIVE
    away_score: 3
    score_for: 1                       # derived from venue at transform time — see §2.2
    score_against: 3
    declared_board_count: 6            # as declared; may not equal games.length — see spike findings
    boards: []                         # see §1.1
    source:                            # provenance block — see §2.6
      system: ecflms
      org_id: 779                      # parsed from the capture PATH, not the payload
      season_id: 1805                  # parsed from the capture PATH, not the payload
      event_id: 9131                   # parsed from the capture PATH, not the payload
      fixture_id: 130772               # the LMS fixture_id, from the payload

No `opponent_id`. No `last_synced` (lives in `data/sync-status.json`, unchanged
from the 5 August decision). No `winner` (unreliable — derive outcome from
scores; `games: []` / `winner: "unknown"` in the source is a played/unplayed
signal only, see §2.1, never an outcome signal). No stored URLs — see §2.6.

**Superseded 11 August 2026:** the top-level `lms_id` field is gone,
replaced by `source.fixture_id`. Anything referring to a fixture by its LMS
identity — the News article `related_fixture` join, the calendar feed's
`UID`, the eventual sync tool's upsert key — now reads `source.fixture_id`.

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

`rating: null` is a genuine unrated-player state and must be preserved as
`null`, never coerced to `0`. The LMS's own match card renders it as `0000`,
which is a display convention on ECF's side, not a rating value. Distinguish
a real unrated player from a placeholder by `rating_code`, not by `rating`: a
real player always carries a `rating_code` even when `rating` is null; only
the `-2`/`-5` sentinels have `rating_code: null`. Both cases occur in the same
captured fixture — `org-1097/season-1758/event-08866.json`, Atherton A v
Radcliffe — which makes it a useful transform test case.

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

**Trim `home_team` and `away_team` before matching** — see §3.6. This is not
the same cosmetic concern as player-name whitespace, because team names are
the join key.

Match the trimmed `home_team` and `away_team` against `data/own_team.json`'s
entries for the fixture's `organisation`. Whichever side matches is `own_team`
(resolved to its slug) and determines `venue`; the other side's trimmed string
becomes `opponent`, stored verbatim otherwise. No `opponent_id` — confirmed
6 August 2026 that `home_team`/`away_team` are bare strings in every league,
with no underlying identifier of any kind.

**Corrected 11 August 2026, during Session 3.** This originally said a
fixture where neither side matches `own_team` should fail loudly. That was
written before a full event draw had been inspected end to end.
`event-08865.json` (Bolton League Cup Div 1) has 7 fixtures and only 1
involves Great Lever; `event-08866.json` (Division 1, the full round-robin)
has 30 and only 10 do. A captured event is the *whole* division or cup, not
a Great-Lever-filtered view of it — most fixtures in any capture are between
two other clubs entirely, and that's the normal case, not an error.

**Neither side matching is a counted skip, not a failure.** The transform
logs a per-event summary (`N written, M skipped`) rather than erring, or
silently dropping the count — visible without being noisy about it. The
genuine error conditions are narrower than the original wording implied:
**both** sides matching (Great Lever can't play itself — a real bug in
`own_team.json` or a data anomaly worth investigating), and an
`organisation` with no `own_team.json` entry at all (the lookup has fallen
out of sync with `source_ids.json`'s organisation list). Both of those still
fail loudly.

### 2.4 `organisation` and `season` resolution, and the current-season pointer

**`organisation`** — look up the capture path's `org_id` in
`data/source_ids.json`'s `ecflms.organisations` map → normalised org slug.
This has to resolve before anything else in the transform: `own_team.json`
and `source_ids.json`'s own `seasons` map are both keyed by the slug this
step produces, not by `org_id` directly.

**`season`** — look up `(organisation, raw season_id)` in
`data/source_ids.json`'s `ecflms.seasons` map → normalised slug. The raw
`sid` is not stored under `seasons`, which holds only the normalised slug —
but it *is* retained in `source.season_id` for provenance and link
construction (§2.6). These are two different jobs: `seasons` is the taxonomy
term the site groups by, `source.season_id` is the identifier the LMS knows
the season by.

**Revised 11 August 2026, during Session 3.** Both lookups originally lived
in separate files (`data/organisation.json`, `data/season.json`), each keyed
directly by the raw source id with no namespace. Consolidated into
`data/source_ids.json` once it was clear both were doing the same job —
translating a source-internal id into project vocabulary — and that job is
inherently per-source. Full reasoning in §4.

`data/current_season.json` holds the currently-live season slug **per
organisation** (not per team — a season is an organisational property; all of
an org's teams share it). `/fixtures/` and `/results/` templates filter to
fixtures where `seasons` contains that organisation's entry. An organisation
absent from this file means Great Lever isn't fielding a team there this
cycle — same graceful-degradation shape already used for the sync tool's org
allowlist, not a special case.

Hand-maintained through Stage 1/2, one line flipped at each season rollover.
Stage 3's `EcfLmsSync` can overwrite it wholesale from whichever `season_id`
the API reports `status: active` for, per organisation in its allowlist.

**Corrected 7 August 2026 — Session 2, second half.** Renamed `season` →
`seasons`, and changed from a scalar string to a single-element list. Both
changes are required by Hugo's taxonomy front matter convention, discovered
while wiring the taxonomy declaration in `hugo.toml` — full finding in §2.5.
The value still only ever holds one entry; the list shape is Hugo's
requirement, not a modelling decision, and nothing about the season lookup
files or the eventual transform's logic changes because of it.

### 2.5 Hugo taxonomy wiring: `render: link`, not `render: never`

**Confirmed 7 August 2026 — Session 2, second half.** `hugo.toml` declares:

    [taxonomies]
      season = 'seasons'

Front matter uses the plural key (`seasons`), per Hugo convention — see the
field list in §1.

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

### 2.6 The `source` block — added 11 August 2026

Replaces the flat `lms_id` field. Five keys: `system`, `org_id`, `season_id`,
`event_id`, `fixture_id`.

**Why it exists.** Three pressures, resolved together:

- **Deep links back to the LMS.** Members should be able to verify a fixture
  against the authoritative source. This is not merely a hedge against sync
  delay: the API exposes no verification status at all (§3.7), so the
  generated site structurally cannot show whether a result has been checked,
  and must point at the page that can.
- **`event_id` is not in the payload.** It exists only in the capture file
  path. Without lifting it into front matter during Session 3, the
  event-level link is unconstructable, and recovering it after backfill means
  re-deriving it across every generated file.
- **`lms_id` was the one ECF-specific name in an otherwise portable schema.**
  Every other field — `date`, `own_team`, `opponent`, `boards[]` — would
  apply unchanged to a differently-sourced fixture.

**Why nested rather than `source_`-prefixed flat fields.** It quarantines the
source-specific part of the schema in one visible place, and lets a
differently-shaped source carry a different key set without the schema
accumulating mostly-null columns. Hugo reads nested params without ceremony
(`.Params.source.fixture_id`; `where` accepts the dotted path). The one thing
nesting cannot do is back a taxonomy, which requires top-level values — not a
constraint here, since a `/source/ecflms/` archive page isn't wanted.

**Where the IDs come from.** `fixture_id` is in the payload. The other three
are parsed out of the capture path
(`ecf-captures/lms/org-{orgId}/season-{seasonId}/event-{eventId}.json`) —
parse the numeric value, never infer meaning from the zero-padding width.
Re-confirmed 11 August 2026 that the payload's complete top-level key set is
`event_name`, `event_type`, `fixtures[]` and nothing else: no event, org or
season context anywhere in the response body.

**URLs are not stored in front matter.** IDs live in the fixture files; URL
patterns live in `data/sources.json` (§4), keyed by `source.system`; the
template assembles the link. Same division of labour as `own_team.json` and
`data/source_ids.json`. The LMS is Drupal, and Drupal path structures do get
restructured across major versions — a pattern change should be a one-line
edit rather than a regeneration and re-commit of every content file. Storing
IDs also keeps fixture files honest about what the API actually returned.

**Deliberately not designed yet.** An optional `source.url` escape hatch for
sources with no derivable per-fixture URL, and the template's graceful "no
link available" path (a Session 8 concern). Neither is needed while ECF LMS
is the only source.

**Not a multi-source abstraction.** If a non-LMS league is ever ingested it
gets its own disposable transform writing this same front matter — not an
extended `ecf-lms-transform` understanding two formats. Two alternative
sources were examined concretely on 11 August (South East Lancashire Summer
League's MS Access XML export; the ECF Ratings site's per-event game lists)
and both are structurally unlike the LMS payload in ways no interface
invented from a sample of one would have predicted.

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
slot. This is also why the generated filename keys on `fixture_id` and not on
the date (§1).

**3.4 — Postponed and withdrawn fixtures are indistinguishable from the API
alone.** Both serialise as `0–0`, `winner: "unknown"`, `games: []`. See §2.1.

**3.5 — `lms_id` no longer names two things at fixture level.** Historically
the API's `fixture_id` became a top-level front matter `lms_id`, colliding
confusingly with `player.lms_id` — a completely different value scoped to a
player, not a fixture. The `source` block resolves this: the fixture
identifier is now `source.fixture_id`, and `lms_id` appears only inside
`boards[].player` / `boards[].opponent`, where it means exactly what the API
means by it. Still worth a comment in the transform code, since the API's own
naming retains the collision.

**3.6 — Team names carry stray whitespace, and it matters more than player
whitespace does.** *(new 11 August 2026)* Bolton Division 1 returns
`"Radcliffe "` with a trailing space on every one of the ten fixtures
involving that club. Not observed in Central Lancashire Division A (event
9131), so it is likely per-organisation rather than universal.

Player-name whitespace was downgraded to a cosmetic display concern on
3 August because `rating_code` carries the join instead. **That downgrade does
not transfer to teams:** `home_team`/`away_team` have no identifier at all
(§2.3), so the name *is* the key for the `own_team` and `opponent` lookups.
An untrimmed `"Radcliffe "` simply fails to match a clean `"Radcliffe"` and
falls through — silently, unless §2.3's fail-loudly rule catches it. Trim both
team names at transform time, alongside player names.

**3.7 — The API exposes no verification status.** *(new 11 August 2026)* The
LMS fixtures page carries a Status column (`OV` / `OU` — verified /
unverified) and each match card footer names who reported and verified the
result. None of it is in the payload. The generated site therefore cannot
display whether a result has been verified and **must not imply that it has**
— which is the practical argument for the per-fixture deep link in §2.6.

**3.8 — `event_type` carries raw LMS vocabulary, outside the `source`
block.** *(open question, 11 August 2026)* `knockout_draw_round` and
`team_league` are Drupal content-type names, not chess vocabulary — no
player would describe a match that way. Strictly, this breaches the rule
that everything outside `source` stays source-neutral (§2.6). Deliberately
**not** fixed now: fixing it means inventing neutral terms (`knockout`,
`league`) against a sample of one source, which is exactly the trap §2.6
already names for multi-source abstractions in general — a second source
might need a third term, or a different split entirely. Revisit when a
second source is real, not before. The same, more mildly, applies to
`boards[].result` (`double_default` etc.) — but not to
`boards[].player.lms_id`, which §3.5 already deliberately keeps as raw LMS
vocabulary, since it means exactly what the API means by it there.

**3.9 — `event` has no lookup file backing it, unlike `organisation`,
`season` and `own_team`.** *(open question, 11 August 2026)* It's slugified
directly from the source's `event_name` string at transform time
(`"Bolton League Cup Div 1"` → `bolton-league-cup-div-1`) — the one
normalised field where a source's display text flows straight into front
matter with no translation table governing it. If the LMS renames a
competition mid-season, or a second source names the same competition
differently, two slugs result for one competition and nothing catches it.
No concrete trigger yet — event names have been stable throughout the
2025/26 capture — but worth a `data/event.json` lookup, mirroring
`own_team.json`'s shape, the day one actually renames.

---

## 4. `data/` lookup files

Four files, JSON (matching the format already established by
`data/sync-status.json` and `data/stats/season.json` elsewhere in the spec —
no new format introduced for these). Filenames use underscores, not hyphens —
Hugo exposes `data/` filenames as Go template fields under `.Site.Data`, and
a hyphenated name doesn't parse as dot notation.

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

`raw_name` values are stored already-trimmed; the transform trims the
incoming API string before comparing (§3.6), so no entry should ever be added
here with leading or trailing whitespace to "match" a dirty source value.

Manchester's `"Great Lever 1"` variant deliberately not included — out of
scope until Stage 4 backfill.

**Considered and judged safe, 11 August 2026.** Raised during Session 3:
whether pre-LMS-era imports (a league before it joined the LMS) would strain
this file the way `organisation.json`/`season.json` did. They won't — this
file's job is already "map of display-name aliases to our slug," and a
pre-LMS source calling the club something else (`"Gt Lever"`, say) is
handled by adding another alias under the same slug, same as any other
naming variant. No source-internal id appears anywhere in this file; the
coupling problem that hit the other two doesn't apply here.

### `data/source_ids.json` — replaces `organisation.json` and `season.json`, 11 August 2026

Namespaced by `source.system`, holding every source-internal id this
transform needs to translate into project vocabulary. Two sub-maps for
`ecflms` today: `organisations` (`org_id` → organisation slug) and `seasons`
(`(organisation, raw season_id)` → normalised slug — nested the same way
the old `season.json` was, just moved under a namespace).

    {
      "ecflms": {
        "organisations": {
          "1097": "bolton-district",
          "779": "central-lancashire"
        },
        "seasons": {
          "bolton-district": {
            "1758": "2025-26"
          },
          "central-lancashire": {
            "1805": "2025-26"
          }
        }
      }
    }

**Why this exists.** `data/organisation.json` was created earlier the same
day as a hardcoded 2-entry dictionary in the transform, promoted to a real
lookup file once Manchester backfill turned a third row from hypothetical
into planned. Almost immediately after, it became clear `organisation.json`
and `season.json` had the identical problem: both keyed directly by an
LMS-internal id, with no namespace saying so, sitting in a folder next to
files (`own_team.json`, `current_season.json`) that are genuinely
source-neutral. That's the same shape of problem captures already solved —
`ecf-captures/lms/...` is nested under `lms` specifically because ECF runs
more than one system (the LMS, and a separate Ratings API), and a flat
`ecf-captures/` would have hidden that distinction. `source_ids.json` applies
the same fix to `data/`.

**Not a multi-source abstraction either** — same caveat as §2.6's on the
`source` block. A second source (SELSL, say) gets its own top-level key
shaped however *that* source's ids actually work, which may not resemble
`organisations`/`seasons` at all. The namespacing is the only contract;
nothing assumes a common shape across sources.

**Deliberately kept separate from `data/sources.json`**, despite both being
keyed by `source.system` — a judgement call, not a certainty. `sources.json`
holds URL patterns consumed by *templates* (Session 8); `source_ids.json`
holds id mappings consumed only by *the transform*. Different consumers and
different change cadence tipped this towards two files, but it's genuinely
close — revisit if the two start drifting out of sync in practice.

Manchester's `org_id: 1237` deliberately not added yet, mirroring
`own_team.json`'s own "out of scope until Stage 4 backfill" line above — add
both together when that day comes, not one ahead of the other.

### `data/current_season.json`

The pointer file from §2.4. One row per organisation currently fielding a
Great Lever team; absence means "not playing there this cycle."

    {
      "bolton-district": "2025-26",
      "central-lancashire": "2025-26"
    }

### `data/sources.json` — added 11 August 2026

URL patterns keyed by `source.system`, so templates can build deep links from
the IDs in front matter rather than having URLs baked into every content file
(§2.6). Placeholders are substituted from the fixture's `source` block.

    {
      "ecflms": {
        "label": "ECF LMS",
        "fixture_url": "https://lms.englishchess.org.uk/lms/fixture/{fixture_id}",
        "event_url": "https://lms.englishchess.org.uk/lms/event/{event_id}/fixtures"
      }
    }

No template consumes this until Session 8. It lands in Session 3 because it
is part of the same decision as the `source` block, and splitting the two
invites the URL patterns being hard-coded into a template later "just for
now."

---

## 5. Explicitly out of scope

**Session 2's original scope note, retained:** Hugo taxonomy wiring for
`season` was completed in Session 2's second half — see §2.5.

**Session 3 non-goals**, unchanged from the delivery plan: no HTTP, no auth,
no scheduling, no idempotent upsert. The transform reads files from disk and
writes files to disk. Anything that would need an `HttpClient` belongs to
`EcfLmsSync`.

**Not designed until a concrete need exists:** any shared abstraction over
multiple data sources (§2.6), a `source.url` escape hatch, and the
"no link available" template fallback.

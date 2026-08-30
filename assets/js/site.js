/*
  Whole-row click-to-expand for .fixture-table rows — a progressive
  enhancement over the native <details>/<summary> toggle already in
  the Result cell (layouts/_partials/fixture-item.html), which keeps
  working exactly as it does today if this script never loads or
  fails: no tabindex is added, nothing is focused or .click()'d, and
  keyboard activation of <summary> is untouched.

  Event delegation on each table's <tbody>, not a listener per row —
  archive term pages can hold many rows, and this scales without
  per-row wiring.

  Wrapped in try/catch, like every block in this file now: each piece
  of progressive enhancement here is independent, and a bug in one
  must never silently take out an unrelated, already-working one.
  Concretely useful this round — see the league-accordion block
  below for what actually broke this one, and why it wasn't a crash
  at all, but the principle holds regardless of what causes a future
  failure.
*/
try {
  document.querySelectorAll('.fixture-table tbody').forEach((tbody) => {
    tbody.addEventListener('click', (event) => {
      // Don't hijack a text-selection drag (e.g. copying a date or score).
      if (window.getSelection().toString().length > 0) return;

      const row = event.target.closest('tr');
      if (!row) return;

      /*
        Clicks inside the row's OWN per-board <details> (the summary
        itself — mouse or keyboard activation both dispatch a click
        there — or its revealed content) already toggle correctly on
        their own; only handle clicks that land elsewhere in the row.

        details.contains(event.target), NOT the previous
        event.target.closest('details'): fixture-collection.html's
        league accordion now wraps the WHOLE table in a second, OUTER
        <details data-league-accordion>. closest('details') walks
        every ancestor, not just the nearest one, so it started
        matching that outer wrapper on every click anywhere in the
        table — the guard fired unconditionally and this handler
        never reached the toggle below, for any click, anywhere. No
        console error involved: the DOM this code runs against
        changed shape underneath it; the code itself (unedited since
        Session 9 — see git blame) never got the chance to be wrong
        on its own terms. Scoping the check to THIS row's own
        <details> via .contains() fixes it by construction — a
        row-scoped lookup can't see an ancestor outside the row,
        regardless of how many <details> wrap the table from here on.

        .fixture-detail-row (the revealed board-by-board content) has
        no <details> of its own, so `details` is null for a click
        landing there — the check below is skipped, and so is the
        toggle, letting a link in that row navigate normally instead
        of being re-toggled.
      */
      const details = row.querySelector('details');
      if (details && details.contains(event.target)) return;
      if (details) details.open = !details.open;
    });
  });
} catch (err) {
  console.error('site.js: row click-to-expand setup failed', err);
}

/*
  League-accordion default open/closed state (layouts/_partials/
  fixture-collection.html). Markup always ships closed — that's the
  no-JS fallback — so this only runs to set the DESKTOP default open.
  [data-league-accordion] is a plain attribute rather than a class
  specifically so this selector can't also catch the per-board
  <details> the click-to-expand handler above already owns; the two
  features share the file but never touch each other's elements
  DIRECTLY. (They did collide indirectly this round, the other
  direction — this block's own MARKUP, not this block's code, made
  the row-click handler's old guard match too broadly. See that
  block's comment for the actual bug and fix; nothing here needed to
  change for it.)

  Evaluated once, at load, and never again: no matchMedia change
  listener and no resize handler. A visitor who's manually toggled a
  league open or shut is trusted to keep it that way — a live
  listener would silently flip it back to the breakpoint's default on
  the next resize or device rotation, overriding a choice they just
  made.

  40rem matches the one "counts as desktop" breakpoint already used
  for the nav dropdown and the fixture-table/board-row layout switch
  (site.css) — not a second value invented here.

  Self-guarding: window.matchMedia has shipped everywhere for over a
  decade, but if it's ever missing this block quietly no-ops rather
  than throwing, leaving every accordion in its closed markup default
  — a safe fallback, not a broken page.

  try/catch, matching the block above: this file previously had two
  independent features as bare top-level statements with nothing
  between them — had EITHER thrown, everything textually after it in
  the file would never have run (a single <script>, no module
  boundaries, nothing isolating one feature's init from another's).
  That's not what broke row-click this time (this block never threw,
  and it runs after the row-click block anyway — file order alone
  would have protected it even unwrapped), but it's a real latent
  risk this file had no guard against, so both blocks get one now.
*/
try {
  if (window.matchMedia) {
    const isDesktop = window.matchMedia('(min-width: 40rem)').matches;
    document.querySelectorAll('[data-league-accordion]').forEach((details) => {
      details.open = isDesktop;
    });
  }
} catch (err) {
  console.error('site.js: league-accordion default state setup failed', err);
}

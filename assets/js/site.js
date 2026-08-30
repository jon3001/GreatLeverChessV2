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
*/
document.querySelectorAll('.fixture-table tbody').forEach((tbody) => {
  tbody.addEventListener('click', (event) => {
    // Don't hijack a text-selection drag (e.g. copying a date or score).
    if (window.getSelection().toString().length > 0) return;

    /*
      Anything inside <details> — the summary itself (mouse or
      keyboard activation both dispatch a click there) — already
      toggles correctly on its own; only handle clicks that land
      outside it. .fixture-detail-row (the revealed board-by-board
      content, including its own ECF LMS link) has no <details> of
      its own, so it's not excluded here — it doesn't need to be: the
      querySelector below finds nothing inside it and this is a no-op,
      letting a link in that row navigate normally instead of being
      re-toggled.
    */
    if (event.target.closest('details')) return;

    const row = event.target.closest('tr');
    const details = row && row.querySelector('details');
    if (details) details.open = !details.open;
  });
});

/*
  League-accordion default open/closed state (layouts/_partials/
  fixture-collection.html). Markup always ships closed — that's the
  no-JS fallback — so this only runs to set the DESKTOP default open.
  [data-league-accordion] is a plain attribute rather than a class
  specifically so this selector can't also catch the per-board
  <details> the click-to-expand handler above already owns; the two
  features share the file but never touch each other's elements.

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
*/
if (window.matchMedia) {
  const isDesktop = window.matchMedia('(min-width: 40rem)').matches;
  document.querySelectorAll('[data-league-accordion]').forEach((details) => {
    details.open = isDesktop;
  });
}

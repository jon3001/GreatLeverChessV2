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

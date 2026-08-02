# Great Lever Chess Club — Website V2

Static site for a Bolton chess club. Replaces a decade-old table-based
site. Hugo -> Cloudflare Pages. Currently building a demo for club
feedback; public launch targeted before the October 2026 season.

## Stack

- Hugo extended v0.164.0 (pinned; Cloudflare HUGO_VERSION must match)
- Hand-rolled CSS from design tokens. No Bootstrap, Tailwind, or any
  CSS framework.
- Single assets/js/site.js via Hugo Pipes. No JS framework, no bundler,
  no npm build step of any kind.
- Cloudflare Pages, git-push deploy. Trunk-based, main only.

## Hugo conventions — important

Use the post-0.146 layouts structure:
layouts/baseof.html, layouts/home.html, layouts/page.html,
layouts/section.html, layouts/_partials/, layouts/_shortcodes/.
Do NOT use layouts/_default/ or layouts/partials/ — deprecated.
Most tutorials and older documentation still show the old structure.

Since Hugo 0.162, content files of type text/html are rejected by
default under /content. Content is Markdown. Do not add HTML content
files or relax security.allowContent without asking.

## How to work with me

Explain non-trivial decisions inline. Do not produce finished files
silently. For CSS, justify every non-obvious property choice as a
comment. When there is more than one reasonable approach, present two
and state the trade-off.

I am an experienced C#/.NET backend developer, a Hugo novice, and my
frontend skills are about a decade stale. Assume competence, assume no
current frontend idiom.

## Constraints

- The site must build successfully with the ECF LMS API completely
  unreachable. Data is committed to the repo, never fetched at build
  time.
- Progressive enhancement only. Everything must render and work with
  JavaScript disabled. JS adds convenience, never function.
- Semantic HTML and WCAG 2.2 AA as a baseline, not a later pass.
- British English in all site content. Dates as "Tue 14 Oct 2026".

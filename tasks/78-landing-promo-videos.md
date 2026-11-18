---
title: 'Promo videos on the landing page'
purpose: 'Checklist for showing the recorded promo clips in the landing page feature showcase.'
description: >-
  Four silent, looping promo clips (schema conflicts, vikardækning, ugeplan,
  parent view on a phone) are recorded in web/public/media/promo as WebM and
  MP4. FeatureShowcase only takes an image today; this adds an optional video
  with the current screenshot as poster, maps the clips to blocks and adds two
  blocks for vikardækning and ugeplan.
status: 'Ready'
---

# Promo videos on the landing page

## TL;DR

- The clips exist but nothing shows them. [LandingPage.tsx](../web/src/pages/LandingPage.tsx) `FeatureShowcase` renders an `<img>` only.
- Add an optional `video` prop: `<video autoPlay muted loop playsInline poster={imageSrc}>` with a WebM and an MP4 `<source>`, and the existing PNG as poster and fallback.
- Size S. Owner: Agent, with Niels approving the copy for the two new blocks.

## Clips

In `web/public/media/promo/`, each as `.webm` (VP9) and `.mp4` (H.264). They're untracked when this task starts, so commit them in this task's PR.

| Clip | Size (px) | Length | Shows | Block |
|---|---|---|---|---|
| `skema-konflikt` | 1200×750 | 14.6 s | A lesson dragged onto a busy teacher gets "1 konflikt", moving it clears it | Skemabygger (or Konfliktkontrol) |
| `foraeldre-mobil` | 390×844 | 14.2 s | Parent ugeplan on a phone: tomorrow's vikar notice, day chips, homework | Forældremodul |
| `vikardaekning` | 1200×750 | 13.9 s | First lesson covered from the dropdown, the rest with "Sæt på alle" | New block: Vikardækning |
| `ugeplan` | 1200×750 | 19.5 s | Teacher writes a lesson and lektier, "Gemt", then the parent preview | New block: Ugeplan |

They're recorded with fictional demo data ("Lindely Friskole") and have no audio or captions.

## Steps

- [ ] `FeatureShowcase`: optional `video?: { webm: string; mp4: string }`. Without it, render the `<img>` as today.
- [ ] Video element: `autoPlay muted loop playsInline`, `preload="metadata"`, `poster={imageSrc}`, WebM source before MP4, and `aria-label` from `imageAlt`. Tailwind classes only.
- [ ] `prefers-reduced-motion: reduce`: show the poster image instead of the video.
- [ ] Phone clip: `foraeldre-mobil` is portrait. Show it in a narrow phone-shaped frame, centred in the 7/12 column, not stretched to full width.
- [ ] Wire `skema-konflikt` and `foraeldre-mobil` into the existing blocks. The Skemabygger and Konfliktkontrol blocks show nearly the same thing in motion, so pick one for the video and keep the other a screenshot.
- [ ] Add Vikardækning and Ugeplan blocks. Niels approves the Danish eyebrow, title and description. Check that they match the cards in [features.tsx](../web/src/content/features.tsx).
- [ ] Posters for the new blocks: a still from each clip's last frame, saved as PNG next to the existing screenshots in `web/public/media/`.
- [ ] Check page weight: about 3 MB in total for the four MP4s, and `preload="metadata"` keeps that off the first paint. Check Lighthouse on `/` before and after.
- [ ] Check in Chrome, Firefox and Safari (desktop and iPhone): autoplay, loop, the poster fallback and reduced motion.

## Re-recording

The clips come from a local, gitignored skill (`.claude/skills/demo-video/`, on Niels's machine). It records against the local Aspire stack, and its post-process section has the ffmpeg commands that produce both formats. If the UI changes visibly, re-record before shipping instead of showing an outdated screen.

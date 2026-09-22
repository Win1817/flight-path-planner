# UAS Tool — User Guide

UAS Tool is a desktop application for viewing and cross-referencing UAS (drone) flight
operation plans and areas of responsibility on a map. It runs on your own computer as a
single program — nothing is installed to your system and no account or internet login is
needed. You load your own JSON files; the app draws them on a map and lets you search,
compare and export them.

This guide describes what you see on screen and how to use each part of the app. For
installation, build and developer information, see [README.md](README.md).

## What the app does

You give the app two kinds of data, both as JSON files:

- **OPS** — flight operation plans: where and when a drone flight is authorized to happen.
- **AoR** — areas of responsibility: named zones on the map, such as controlled airspace
  or a NOTAM area.

Once loaded, you can:

- See every OPS and AoR plotted on an interactive map.
- Search and filter the lists (by text, by closure reason, by date range).
- Ask "which flight plans fall inside this area?" (the **Report** tab).
- Ask "which flight plans are within X km of this address or coordinate?" (the
  **Lookup** tab).
- Export any selection to JSON or XLSX (Excel).
- Keep a local history of everything you've uploaded or exported, and reload it later.

Nothing you load or export leaves your computer, except for two things that need the
internet: the map's background imagery, and looking up an address you type into the
Lookup tab (see [Internet access](#internet-access)).

## Starting the app

Run the program file for your operating system (for example `FlightPathPlanner.exe` on
Windows). It opens directly to the main window — there is no login screen and no setup
wizard.

The window is laid out in three parts:

- **Navigation rail** on the left — switch between OPS, AoRs, Report, Lookup and Saved
  Locally. Each item shows a live count badge (e.g. the number of OPS currently loaded).
- **Content panel** in the middle — the list, filters and details for whichever section
  you're on.
- **Map** on the right — shows the shapes relevant to whatever you're currently looking
  at, and highlights whatever is selected.

Short notifications appear at the top of the content panel to confirm uploads, exports,
file actions, and to report warnings or errors — they dismiss themselves after a few
seconds.

## Loading data

### OPS tab

1. Click **Upload OPS JSON** and choose a file.
2. The plans appear as cards in the list and as shapes on the map. The date filter
   (below) is automatically set to cover the full range of dates found in the file.

Accepted files: a single plan, a JSON array of plans, or an object containing a `plans`,
`operations` or `flight_plans` array. Field names can be either camelCase or
snake_case — you don't need to convert your file.

### AoRs tab

Click **Upload AoR JSON** and choose a file. Two AoR file formats are understood
automatically — a "responsibility area" style and an airspace-zone style (circle,
polygon or multi-polygon shapes) — so you can load either without converting it.

### If a record can't be read

Individual entries that don't match a recognised shape are skipped rather than stopping
the whole import; a notification tells you how many were skipped so you can check the
source file if that number looks wrong.

### Large files

If a file is large (40 MB or more), the app shows a notice first — telling you the file
size and that it will load in the background — with **Continue** or **Cancel** buttons,
rather than importing immediately. This is just a heads-up: there's no limit on how many
records you can load. While a large import is running, a progress card shows the current
stage, how many records have been processed, percent complete, elapsed time and an
estimated time remaining, plus a **Cancel import** button. Cancelling leaves whatever was
loaded before untouched — it never leaves you with a half-loaded file.

## Working with the OPS list

- **Search box** — matches title, ID, operator and description.
- **Closure-reason chips** — appear automatically based on what's in your file; click one
  (or several) to show only plans with that closure reason.
- **From / To date filter** — narrows the list to plans active in that window. It starts
  set to the file's full date range, so nothing is hidden until you change it.
- **Select all** — ticks every plan currently visible under the active filters.
- Click a card to open its **details panel**: title, status, description, ID, state and
  closure reason, operator, times (UTC), altitude range, area and number of zones.
- Click a shape on the map to select the matching plan in the list, and vice versa —
  hovering a card highlights its shape, and hovering a shape shows a small popup.
- **Delete** removes the selected plan(s) from the current session (it does not touch a
  saved copy — see [Saved Locally](#saved-locally)).
- **Export JSON** / **Export XLSX** save the ticked plans through your system's normal
  save dialog.

## Working with the AoRs list

Same pattern as OPS — search box, select all, details panel, delete, export — but search
matches name, designator, ID, message, restriction and reason text, and there is no
closure-reason or date filter (AoRs don't carry that information). The details panel
shows restriction, message, applicability start/end times, vertical limits and the
auto-reject setting, whichever of those the loaded schema provides.

Each AoR gets a distinct colour, shown as a dot next to its name in the list and as its
fill colour on the map, so you can tell zones apart at a glance.

## Report tab: which flights fall inside a zone?

1. Load both OPS and AoR data first.
2. Open **Report** and pick an AoR from the searchable picker.
3. The tab lists every OPS that geographically intersects that AoR, counted against
   whatever filters are currently active on the OPS tab (so narrowing OPS by date or
   closure reason before running a report narrows the report too).
4. **Export** writes a table with every AoR and how many OPS matched each one, to JSON or
   XLSX.

## Lookup tab: what's near a place?

1. Type a coordinate (`lat, lon`, `lat:lon`, or `lat lon`) or an address into the search
   box and press Enter, or the search button.
   - A coordinate is recognised and used immediately, with no internet needed.
   - An address is looked up online; if more than one place matches, you're shown a list
     to pick from.
2. Choose a radius — either type a number or click a preset (0.5, 1, 2, 5 or 10 km).
3. The tab lists every OPS whose location falls within that radius of the point, along
   with a summary card for the search centre.
4. **Export** saves the matched OPS to JSON or XLSX.

## The map

- Shows whatever the active tab is currently displaying: all OPS, all AoRs, a Report
  result, or a Lookup radius and its matches.
- Selecting, hovering or clicking a card in the list highlights the matching shape, and
  the reverse — clicking a shape on the map selects the matching card.
- With a very large number of shapes loaded, the map only draws what's currently in the
  visible viewport (rather than every shape at once) and shows a small counter such as
  "1,204 in view of 24,975 — zoom in to see all". Panning or zooming updates what's drawn;
  no data is lost, it's just not all drawn until you zoom in.
- If the map area can't start (for example a required system component is missing), the
  rest of the app still works — see [Troubleshooting](#troubleshooting).

## Saved Locally

Every OPS/AoR file you upload, and every report you export, is automatically kept as a
local copy — you don't need to remember to save. Open **Saved Locally** at the bottom of
the navigation rail to:

- **Reload** an earlier upload — this switches you to the matching tab with that data
  loaded again.
- **Archive** / **Restore** a file, to tidy the list without deleting anything.
- **Delete** a file permanently. This asks for confirmation: the first click on a row's
  delete button turns it into a "Confirm delete" button that states what will happen;
  click it again to actually delete. Deleting several at once works the same way, with a
  "Permanently delete N selected file(s)? This cannot be undone." confirmation.

## Internet access

The app itself needs no internet connection to load, filter, cross-reference or export
your files. Two features do reach out to external services:

- **Map background imagery** — loaded from a map tile provider whenever the map is
  shown.
- **Address search on the Lookup tab** — your typed address text is sent to an address
  look-up service (not your coordinates, and not your OPS/AoR data). Entering a
  coordinate directly avoids this entirely.

If you have no internet connection, everything except these two features keeps working
normally.

## Troubleshooting

| What you see | What it means | What to do |
| --- | --- | --- |
| A message in the map area about a missing runtime component (Windows) | A required system web-browser component isn't installed | Install it from Microsoft's website; everything except the map still works meanwhile |
| The map is blank, or shows an error message in its corner | The map page failed to start | The rest of the app still works; ask whoever manages your installation to check the log files next to the program |
| The map has no background imagery | No internet connection, or the imagery provider is unreachable | Check your connection; the shapes and lists still work without it |
| Address search fails or returns nothing | No internet, or the look-up service didn't respond | Try again, or type a coordinate instead (works offline) |
| Some records were skipped on import | Those entries didn't match a recognised file format | Check the reported skipped count against your source file |

If you hit something not covered here, see the **Troubleshooting** section of
[README.md](README.md) for the technical detail (log file names and locations).

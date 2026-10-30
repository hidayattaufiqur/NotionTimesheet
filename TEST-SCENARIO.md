# Manual acceptance test — Notion Timesheet

Everything is already built, signed and installed (`NotionTimesheet 1.0.0.0`).
Two commands are registered: **Start task** and **Finish task**.

Palette hotkey on this machine is **Alt+Space** (see
`%LOCALAPPDATA%\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json`,
`Hotkey.alt=true, code=32`).

Before you start: `%LOCALAPPDATA%\CmdPalNotion\token.txt` must exist with the integration token
(it does right now).

---

## Test 1 — Start task creates a correct row

1. `Alt+Space` → type `start task` → `Enter`.
2. The form should show six fields:
   **Task title\*** (text), **Description (optional)** (multi-line), **Priority**,
   **Category**, **FDD/Project (optional)**, **Status**.
3. Fill:
   * Task title: `ACCEPTANCE TEST — delete me`
   * Description: `manual acceptance run`
   * Priority: `Crucial`
   * Category: `Comlab`
   * FDD/Project: type a few letters to filter, pick any project (or `No project`)
   * Status: leave the default — it must now be **Do Today**
4. Click the **Start task** action at the bottom of the card.

**Expected:** a toast like
`Started "ACCEPTANCE TEST — delete me" at 15:42 WIB https://app.notion.com/p/...`
and the palette returns to the search page.

**Row must have:** Start Time = the submit minute with a `+07:00` offset, Timeline Date =
today, Priority `Crucial`, Category `Comlab`, Status `Do Today`, Month linked to the
`October 2026` row.

## Test 1b — project search and create-new

1. `start task` again.
2. In **FDD/Project**, type a few characters — the list should filter as you type (it now
   loads all 112 project pages, not the 25 most recent).
3. Pick nothing there; instead put a fresh name in **New FDD/Project name**, e.g.
   `ZZ MANUAL TEST PROJECT — delete me`.
4. Submit.

**Expected:** toast mentions `created project "ZZ MANUAL TEST PROJECT — delete me"`, the task
row is created, and `FDD/Project` points at a new page in the FDD/Projects database. Repeating
the same name links the existing page instead (toast says `linked existing project`).

## Test 2 — Empty title is blocked
1. `Alt+Space` → `start task` → `Enter`.
2. Leave **Task title** empty, press the **Start task** action.

**Expected:** the card refuses to submit / shows `A task title is required.` — no row is created.

## Test 3 — Finish task stamps the end time

1. `Alt+Space` → type `finish task` → `Enter`.
2. The list shows **Refresh list** first, then every row whose Status is `In-Progress` or
   `Do Today`, sorted by Start Time ascending, each with subtitle
   `Start <ddd HH:mm> WIB · <Category>`.
3. Select `ACCEPTANCE TEST — delete me` and press `Enter`.

**Expected:** toast `Finished "ACCEPTANCE TEST — delete me" at HH:mm WIB`, the row disappears
from the list, and in Notion **End Time** is now set, **Status** = `Completed`, Start Time
unchanged.

> Shortcut: the row `CmdPal NotionTaskClock E2E 15-30 scratch` (Status `Do Today`,
> Start `15:20 WIB`) is already sitting in the list from my automated run — you can finish
> that one instead of creating a new row.

## Test 4 — Start Time backfill (optional)

1. In Notion, create a Tasks Kanban row with Status `In-Progress` and **no** Start Time.
2. `finish task` → select it → `Enter`.

**Expected:** End Time is stamped **and** Start Time is set to the same moment (never left
empty).

## Test 5 — No token shows the setup page, not a 401

1. Rename the token file: `Rename-Item "$env:LOCALAPPDATA\CmdPalNotion\token.txt" token.bak`
   (and make sure `NOTION_API_KEY` is not set).
2. `reload` → **Reload Command Palette extensions**, then `start task`.

**Expected:** a page titled **Notion token not configured** with the exact path
`C:\Users\hidayat.taufiqur\AppData\Local\CmdPalNotion\token.txt` and the
`NOTION_API_KEY` alternative — never an HTTP 401.

3. Restore: `Rename-Item "$env:LOCALAPPDATA\CmdPalNotion\token.bak" token.txt`, then `reload`.

## Test 6 — Failure surfacing (optional)

Temporarily edit `token.txt` so the token is invalid (`ntn_bogus`), `reload`, then `start task`
and submit a title.

**Expected:** the form stays open and the toast starts with `Nothing was created in Notion.
HTTP 401: ...` plus a body excerpt — no silent success, no half-created row.

---

## After the run

Tell me the row title(s) you created (or just say "done") and I will:

* read each row back through the Notion API and report Status / Start Time / End Time /
  Timeline Date / Month / Priority / Category,
* archive every scratch row with `{"archived": true}` and list the page ids, so nothing is
  left on your board.


---
name: moyu-icons
description: Make, regenerate or wire in the 24×24 Grasshopper toolbar icons of a Moyu panel with Codex image generation. Writes the function prompt and the Codex generation prompt in the repo's format, has the local Codex CLI draw the transparent sprite sheet, slices it with tools/make_icons.py, checks a real-size preview, rebuilds the plugin and runs the --icons test. Use when the user asks for icons for Moyu components or a panel ("生成图标", "做图标", "更新图标", "换图标", "用 codex 生成图标", "icons for the Landscape panel"), or to wire in a sheet Codex already produced.
---

# Moyu icons with Codex

Every Moyu panel has its icons drawn by Codex as one transparent sprite sheet. The sheet is then sliced into `src/Moyu/Icons/<ComponentClass>.png` (24×24). This skill runs the whole chain. Steps 1–3 produce a new sheet; if the user already has one (`docs/images/moyu-icons-<set>-source.png`), start at step 4.

## 0. Settle the inputs

- **Set name**: lower-case panel name (`landscape`, `massing`, …).
- **Components**: component class names in reading order. Class name = file name in `src/Moyu/Components/<Panel>/`; the main game component comes first.
- **Layout**: columns × rows that fit the count, with empty cells last. Past layouts: 2D 4×2, 3D 4×2 with the 8th cell empty, Massing 2×2 with the 4th cell empty, Landscape 2×1.
- **Style reference**: the newest `docs/images/moyu-icons-*-source.png` of another panel, so the family stays consistent (blue-violet and ice-white, indigo outlines).

## 1. Function prompt (the human-readable record)

Write or update `docs/icon-prompts-<set>.md` in the format of `docs/icon-prompts-landscape.md`, as a text block:
1. One paragraph of **game background**: what the 摸鱼 game is, the boss key, and why the panel and components carry architecture or landscape names.
2. Each component's **function only**. Never describe the icon design here (the user's explicit rule).
3. One **layout line**: columns × rows, which cell is empty, transparent background.

## 2. Generation prompt (what Codex gets)

Write `docs/moyu-icons-<set>-generation-prompt.txt` in the format of `docs/moyu-icons-landscape-generation-prompt.txt`.

- **Header lines**: Mode: built-in image_gen; Style reference; Final output; Actual size (fill in after step 3); Layout.
- **GENERATION PROMPT**, in this order:
  - **Reference image role**: the attached sheet is only a visual-family reference; do not copy its glyphs or its grid.
  - **Exact layout**: cell order, each icon centred in its own cell inside the central 68–70 %, at least 15 % transparent clearance, empty cells fully transparent, no borders or labels.
  - **Functional context**: the game explains the functions, but no overt game imagery.
  - **One section per icon**, giving its function and its single dominant visual idea.
  - **Coherent design**: palette indigo #263867 contours, cornflower #477DDA faces, iris violet #8067C7 accents, ice-white #EFF4FF light areas; flat colour faces with sturdy outlines; readable at 24×24.
  - **Exclusions**: no text, gloss, glow, shadow or backdrop; real transparent alpha; exactly N icons.

Design direction belongs here, not in step 1.

## 3. Generate with Codex

```bash
python tools/codex_image.py --check
python tools/codex_image.py --prompt-file docs/moyu-icons-<set>-generation-prompt.txt --ref <style reference> --out docs/images/moyu-icons-<set>-source.png
```

- `--check` shows which `codex` is used: the npm install on PATH, otherwise the copy bundled with the Codex desktop app. It also shows whether it is logged in. If not, ask the user to run `codex login`; never handle their credentials.
- A run takes a few minutes. Run it in the background and wait for the notification; don't poll.
- It uses the user's Codex quota, and sends the prompt and reference image to OpenAI. Generate once; regenerate only if the result is wrong.
- Look at the sheet with Read. Check: icon count and order, empty cells, no text, transparency (the script prints the alpha range; the minimum must be 0).
  - If something is wrong, tighten the prompt and try again, at most twice.
  - After that, show the user and ask.
- Write the actual size into the record's header.

## 4. Slice and preview

- Make sure `SETS` in `tools/make_icons.py` lists the set's class names in reading order.
- Slice and write the preview:

  ```bash
  python tools/make_icons.py docs/images/moyu-icons-<set>-source.png --set <set> --cols C --rows R --source-out docs/images/<set>-icons-hd --preview docs/images/moyu-icons-<set>-preview.png
  ```
- `--source-out` keeps each cell at full resolution (for later edits or other sizes); only the 24×24 files go into `src/Moyu/Icons/`.
- Read the preview. Each icon must stay distinguishable at 24 px on both the light and the dark strip. If one turns to mush, go back to step 2 with fewer, bolder features.

## 5. Build and check

- Rhino locks `src/Moyu/bin/Release/net7.0-windows/Moyu.gha`.
  - If a Rhino slot you started is running (`mcp__rhino__list_slots`), close it with `run_command "_-Exit _No"`.
  - If it is the user's own Rhino, ask them to close it.
- Build, then run the icon test:

  ```bash
  dotnet build src/Moyu -c Release
  dotnet run -c Release --project tests/Moyu.RhinoTests -- --icons
  ```
- Expect 0 warnings and ALL PASSED. If the panel added components, update the expected count in `tests/Moyu.RhinoTests/Scenarios.cs` (`Icons`).
- Optional: open the panel's test GH file in Rhino and look at the canvas.

## 6. Record and hand over

- In `docs/icon-prompts-<set>.md`, record:
  - the source sheet, preview and generation record;
  - the re-export command;
  - the cell → file → component table;
  - an **接入状态（date）** line saying exactly what was verified (build, `--icons`, GH canvas or not).
- In the README's 图标 section, drop any "临时图标" wording and show the re-export command.
- Send the user the preview image (SendUserFile).
- Do not commit unless asked.

# Session Formatting Templates

Standardized formatting for all session co-pilot output. Use these templates exactly to maintain visual consistency.

---

## Session Briefing Dashboard

Used at session start (Phase 1).

```
================================================================
                    DEV SESSION BRIEFING
================================================================

  Project:    {project_name}
  Branch:     {current_branch}
  Session:    {date} #{session_number}

────────────────────────────────────────────────────────────────

  RECENT ACTIVITY
  - {commit_or_change_1}
  - {commit_or_change_2}
  - {commit_or_change_3}

  OPEN ITEMS
  - [ ] {open_item_1}
  - [ ] {open_item_2}

────────────────────────────────────────────────────────────────

  MENU

  [1] Plan      — break work into steps
  [2] Decide    — evaluate options, pick a direction
  [3] Feature   — build something new
  [4] Debug     — find and fix a problem
  [5] Scope     — assess what fits in the timeline
  [6] Resume    — pick up where you left off

  Type a number or describe what you need.

================================================================
```

### Field Descriptions

- **project_name**: Name of the project (from directory name, package.json, or .csproj)
- **current_branch**: Active git branch
- **date**: Current date (YYYY-MM-DD)
- **session_number**: Incremented from session state, or 1 if first session
- **Recent Activity**: Last 3 git commits (short hash + message), or "No recent commits" if empty repo
- **Open Items**: Carried forward from previous session state, or "None" if first session

---

## Step Progress Header

Displayed at the start of each step within a path.

```
┌─────────────────────────────────────────────────┐
│  {PATH_NAME}  ·  Step {n}/{total}: {step_title} │
└─────────────────────────────────────────────────┘
```

### Examples

```
┌──────────────────────────────────────────────┐
│  PLAN  ·  Step 1/6: Goal Check               │
└──────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────┐
│  FEATURE  ·  Step 3/5: Design                │
└──────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────┐
│  DEBUG  ·  Step 4/5: Test & Fix              │
└──────────────────────────────────────────────┘
```

---

## Mode Tip Callout

Used to surface relevant guidance or doc references during a step.

```
╭─ TIP ──────────────────────────────────────────╮
│  {tip_text}                                     │
│  📖 Related: {doc_title} ({doc_id})             │
╰─────────────────────────────────────────────────╯
```

### Examples

```
╭─ TIP ──────────────────────────────────────────╮
│  Consider the Observer pattern for event-driven │
│  communication between systems.                 │
│  Related: Game Programming Patterns (G18)       │
╰─────────────────────────────────────────────────╯
```

When no specific doc is referenced:

```
╭─ TIP ──────────────────────────────────────────╮
│  Break large features into vertical slices —    │
│  each slice should be testable on its own.      │
╰─────────────────────────────────────────────────╯
```

---

## Path Completed Banner

Displayed when all steps in a path are finished.

```
================================================================
  ✓  {PATH_NAME} COMPLETE
================================================================

  Summary:
  {brief_summary_of_what_was_accomplished}

  Artifacts:
  {list_of_files_changed_or_created}

────────────────────────────────────────────────────────────────
  What's next?

  [1] Plan  [2] Decide  [3] Feature  [4] Debug  [5] Scope
  Or type "done" to wrap up the session.
────────────────────────────────────────────────────────────────
```

---

## Session Summary Dashboard

Displayed when the developer types "done" to end the session.

```
================================================================
                    SESSION SUMMARY
================================================================

  Session:    {date} #{session_number}
  Duration:   ~{estimated_duration}
  Branch:     {current_branch}

────────────────────────────────────────────────────────────────

  COMPLETED
  - {completed_item_1}
  - {completed_item_2}
  - {completed_item_3}

  DECISIONS MADE
  - {decision_1}: {brief_rationale}
  - {decision_2}: {brief_rationale}

  OPEN ITEMS (carry forward)
  - [ ] {open_item_1}
  - [ ] {open_item_2}

  COMMITS THIS SESSION
  - {short_hash} {commit_message_1}
  - {short_hash} {commit_message_2}

  NEXT SESSION SUGGESTION
  {what_to_pick_up_next_time}

================================================================
```

### Field Descriptions

- **estimated_duration**: Rough estimate based on session start time
- **Completed**: Items finished during this session
- **Decisions Made**: Any decisions captured (reference ADR numbers if applicable)
- **Open Items**: Unfinished work to carry forward to next session
- **Commits**: All git commits made during the session
- **Next Session Suggestion**: Recommended starting point for the next session

---

## Routing Confirmation

Used when classifying freeform text input to a path (Phase 2).

```
> Sounds like a **{Path}** task. Starting that now.
> (Say "no" to pick a different path.)
```

---

## Inline Step Transition

Used between steps within a path to show progression.

```
────────────────────────────────────────────────────────────────
  Step {n} complete. Moving to Step {n+1}...
────────────────────────────────────────────────────────────────
```

---

## Error / Clarification Prompt

Used when input is ambiguous or more information is needed.

```
╭─ CLARIFY ──────────────────────────────────────╮
│  {clarification_question}                       │
╰─────────────────────────────────────────────────╯
```

---

## Decision Summary Card

Used in the Decide path to present options side by side.

```
┌─ OPTION A: {name} ─────────────────────────────┐
│  {description}                                   │
│  + {pro_1}                                       │
│  + {pro_2}                                       │
│  - {con_1}                                       │
│  Effort: {small|medium|large}                    │
└──────────────────────────────────────────────────┘

┌─ OPTION B: {name} ─────────────────────────────┐
│  {description}                                   │
│  + {pro_1}                                       │
│  - {con_1}                                       │
│  - {con_2}                                       │
│  Effort: {small|medium|large}                    │
└──────────────────────────────────────────────────┘
```

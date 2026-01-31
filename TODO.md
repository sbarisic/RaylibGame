# Project Name - TODO

A list of planned features, improvements, and tasks for this project.

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

> Instructions for the TODO list:
- Move all completed TODO items into a separate Completed document (DONE.md) and simplify by consolidating/combining similar ones and shortening the descriptions where possible

> How TODO file should be iterated:
- First handle the Uncategorized section, if any similar issues already are on the TODO list, increase their priority instead of adding duplicates (categorize all at once)
- When Uncategorized section is empty, start by fixing Active Bugs (take one at a time)
- After Active Bugs, handle the rest of the TODO file by priority and complexity (High priority takes precedance, then CPX points) (take one at a time).

---

## Features

### High Priority

*No high priority items*

### Medium Priority

*No medium priority items*

### Lower Priority

*No lower priority items*

---

## Improvements

*No improvements pending*

---

## Documentation **LOW PRIORITY**

- [ ] API reference documentation
- [ ] Getting started guide
- [ ] Architecture overview

---

## Code Cleanup & Technical Debt

### Code Refactoring

*No refactoring items*

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Uncategorized (Analyze and create TODO entries in above appropriate sections with priority. Do not fix or implement them just yet. Assign complexity points where applicable. Do not delete this section when you are done, just empty it)

* Go trough the existing project code, add a small section outlining it

* [ ] Graphics
  * [ ] Lighting system
  * [ ] Fullbright and debug modes which can be enabled from settings?

* [ ] Voxel world
  * [ ] Procedurally generated island
  * [ ] Ability to create and destroy blocks in realtime
  * [ ] Procedurally generated buildings and structures
  * [x] Transparent blocks

* [ ] Quake like player movement

* [ ] GUI System
  * [ ] Window elements
  * [ ] Input elements
  * [ ] Label elements
  * [ ] Button elements
  * [ ] Image elements

* [ ] Centralized physics system
  * [ ] Move collision detection into physic
  * [ ] Move all logic from player and entity into physics

* [ ] NPC AI System
  * [ ] Use pathfinding
  * [ ] AI Goals system

* [ ] Pathfinding
  * [ ] Voxel pathfinding
  * [ ] Air pathfinding for flying entities

* [ ] Entity system
  * [ ] Pickup entity (wapons, ammo, armor...)
  * [ ] Sliding door entity (slides into wall only when player approaches, simple, toggles collision mesh)
  * [ ] NPC entity

* [ ] Particle system
  * [ ] Fix depth ordering
  * [ ] Make it work underwater

* [ ] Mod system
  * [ ] Expose all required functionality
  * [ ] Implement an example mod
  
* [ ] Animation system
  * [ ] Easings

---

## Notes

- Try to edit files and use tools WITHOUT POWERSHELL where possible, shell scripts get stuck and then manually terminate
- Maintain the "dependency-free" philosophy - keep the core library minimal
- Do not be afraid to break backwards compatibility if new changes will simplify or improve the project
- Do not use powershell commands unless absolutely necessary

---

## Completed

### Features

*No completed features yet*

### Improvements

*No completed improvements yet*

### Fixed Bugs

*No fixed bugs yet*




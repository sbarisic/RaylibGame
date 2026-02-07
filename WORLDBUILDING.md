# WORLDBUILDING

## High-Level Concept

Aurora Falls - game

The game takes place on a large floating landmass in a science-fiction universe. Although the setting is technologically advanced, the player begins with primitive tools and little understanding of the world. Over time, exploration reveals that the island and its structures are artificial remnants of a forgotten system.

The experience should transition from **low-tech survival → discovery → technological awakening**.

Players should *realize* they are inside a sci-fi world rather than being told directly.

---

## Core Fantasy

The player is stranded on a fractured floating island filled with buried machinery and guarded infrastructure.

Primary pillars:

- Survival through building and resource gathering  
- Exploration-driven discovery  
- Gradual technological progression  
- Defensive construction against hostile forces  
- Environmental mystery  
- Cooperative survival with other players  

The world should reward curiosity — and shared effort.

---

## World Logic

The floating island must obey a believable internal rule.

### Recommended Direction: Artificial Habitat

The island is part of a massive engineered structure — possibly a terraforming platform, megastructure fragment, orbital installation, or ancient war remnant.

Structures found across the terrain are **system components**, not decorative ruins.

Examples:

- Gravity anchors  
- Energy relays  
- Fabrication facilities  
- Defense systems  
- Transit shafts  
- Crashed vessels  

Large landmarks should be visible from far away to encourage exploration.

---

## Tone and Progression

The tone should evolve as the player advances:

**Early Game**
- Natural materials dominate
- Unknown metallic debris
- Silent or dormant machines

**Mid Game**
- Powered doors
- Active defense drones
- Energy-based technology
- Environmental anomalies

**Late Game**
- Gravity manipulation
- Advanced weapons
- Reactivated infrastructure
- Large-scale world interaction

Avoid predictable tier ladders such as “wood → metal → laser.”  
Instead, progression should come from **discovery**.

Example: finding a conductive crystal unlocks energy tools.

---

## World Structure

The world is fixed-size but procedurally generated with handcrafted elements.

### Generation Model: Hybrid

- Procedural terrain ensures replayability.
- Fixed artifacts provide meaning and identity.

Avoid fully random placement of important structures.

### Recommended Geography

**Vertical layering is strongly encouraged.**

Example:

- Upper layers — safer, natural terrain  
- Middle layers — ruins and enemy activity  
- Lower layers — hazardous, industrial, unstable  
- Deep interior — ancient core systems  

Falling and height should matter.

Consider internal spaces such as hollow sections, megastructures, or underground facilities.

---

## Enemy Presence

Hostile patrol units guard the island.

Avoid immersion-breaking random spawning.

### Preferred Spawn Logic

Enemies originate from identifiable sources:

- Dropships  
- Fabricator towers  
- Signal beacons  
- Defense stations  

Allow players to influence enemy activity by destroying or disabling these sources.

Player actions (mining, activating tech, power usage) may increase patrol frequency.

Enemies should feel like part of an automated security system rather than monsters.

---

## Resource Philosophy

Crafting should remain streamlined.

Avoid complex dependency trees or excessive material variants.

### Core Resource Types

Use abstract materials that scale across technological eras:

- **Biomass** — plants, organic matter, alien growth  
- **Fiber/Wood** — structural natural material  
- **Metal** — construction and machinery  
- **Crystal/Tech** — advanced conductive or computational material  
- **Water** — survival and processing  
- **Energy** — stored power rather than directly mined  

Most objects should yield a combination of these resources.

Crafting consumes them to create specific items without unnecessary complexity.

### Multiplayer Resource Dynamics

Resources are shared world state — when a player mines a deposit, it is gone for everyone. This creates natural cooperation pressure: players must coordinate gathering and distribution rather than competing for the same nodes.

- **Shared storage** — communal containers at a base allow pooling resources  
- **Specialization** — different players can focus on different resource types  
- **Scarcity tension** — limited high-tier resources (Crystal/Tech) force group decision-making  
- **No instanced loot** — what exists in the world is finite until new sources are discovered

---

## Shelter and Building

Building must serve a functional purpose beyond creativity.

Shelter should protect the player from real threats such as:

- Enemy scans  
- Environmental hazards  
- Temperature extremes  
- Radiation zones  
- Night dangers  
- Aerial attacks  

If shelter is optional, players will stop building.

Encourage defensive architecture.

### Multiplayer Base Building

With up to 10 players, building becomes a shared endeavor:

- **Shared construction** — any player can contribute blocks to a base; collaborative builds emerge naturally  
- **Functional roles** — bases benefit from specialized rooms (armory, workshop, medical bay, watchtower) that encourage division of labor  
- **Defensive pressure** — enemy attacks scale with player count, requiring larger and more sophisticated fortifications  
- **Territory** — the group's built area defines their safe zone; expanding outward increases both resources and risk  
- **Grief prevention** — server authority ensures block changes are validated; consider optional block ownership or trust systems for public servers

---

## Technology Should Feel Dangerous

Activating ancient devices should create tension.

Possible consequences:

- Attracting patrols  
- Power overloads  
- Terrain deformation  
- Gravity disturbances  
- Environmental shifts  

Players should hesitate before activating unknown systems.

Mystery is more powerful than safety.

---

## Combat Philosophy

Firearms shift the tone toward sci-fi but should not trivialize danger early.

Recommended constraints:

- Limited ammunition  
- Weapon instability  
- Overheating  
- Recoil  
- Inaccuracy  

Power growth should be earned.

Early encounters should feel like survival, not farming.

### Multiplayer Combat

Multiplayer introduces both cooperative and competitive combat:

- **Cooperative PvE** — groups can coordinate against patrols, with flanking and covering fire  
- **Player vs Player** — friendly fire and PvP are server-configurable; competitive servers allow territorial disputes  
- **Shared risk** — a player death in a dangerous zone affects the whole group (lost carried resources, reduced defense)  
- **Respawn cost** — death should matter but not punish too harshly; respawn at base with a brief delay  
- **Weapon scarcity** — limited ammunition and weapon instability are amplified with multiple players competing for the same supply

---

## Environmental Storytelling

Avoid heavy exposition.

Let players infer the past through:

- Ruined infrastructure  
- Broken transit systems  
- Sealed bunkers  
- Massive cables disappearing into rock  
- Half-buried machines  

Minimal direct lore is preferred.

One strong implied history is enough:

> The world was engineered — and something went wrong.

---

## Persistent World Change

The world should feel dynamic and reactive.

Possible large-scale changes:

- Structural failures  
- Island quakes  
- Sections breaking off  
- Gravity fluctuations  
- New landmasses drifting nearby  
- Increasing enemy activity  

Players remember worlds that evolve.

In multiplayer, persistent changes are amplified — one player's actions reshape the world for everyone. A group that disables a fabricator tower changes the threat landscape for all. Destruction is permanent and collaborative.

---

## Long-Term Pressure

Prevent the sandbox from becoming directionless.

Introduce escalating tension such as:

- Failing infrastructure  
- Resource depletion  
- Rising patrol density  
- Environmental decay  
- Orbital drift toward danger  

Avoid strict timers — escalation is more effective than deadlines.

In multiplayer, escalation scales with group activity — more players mining, building, and activating technology accelerates the island's response. A larger group progresses faster but also draws more attention.

---

## Primary Design Question

Decide early:

### Is the goal to HOLD the island or ESCAPE it?

**Holding** creates a defensive, territorial experience focused on stabilization.

**Escaping** emphasizes discovery, ascent, and mystery.

Either path is strong, but the decision should guide future systems.

In multiplayer, **holding** becomes a team defense game (coordinate building, assign guard shifts, manage shared resources under siege). **Escaping** becomes a cooperative expedition (divide exploration tasks, share discoveries, pool resources toward a group objective). Both benefit from player cooperation.

---

## Multiplayer (Up to 10 Players)

The world supports cooperative survival for up to 10 simultaneous players on a single island. Multiplayer is not a separate mode — it is the same world with shared consequences.

### Social Dynamics

- **Drop-in/drop-out** — players can join and leave a running session; the world persists on the server  
- **No enforced roles** — players self-organize; natural specialization emerges from the world's demands  
- **Proximity matters** — players near each other benefit from shared defense; spreading out covers more ground but increases individual risk  
- **Communication** — text chat for coordination; player name tags visible overhead for identification  

### Cooperation Design

The world should be **difficult enough that cooperation feels necessary**, not optional:

- **Enemy scaling** — patrol frequency and strength increase with active player count  
- **Large projects** — some structures or activations require more resources than one player can reasonably gather alone  
- **Exploration range** — the island is large enough that splitting up to explore is efficient, but dangerous solo  
- **Shared knowledge** — discoveries (new materials, activated systems) benefit everyone on the server  

### Competitive Tension

Even in cooperative play, natural tension should exist:

- **Resource scarcity** — finite resources force prioritization discussions  
- **Risk tolerance** — cautious players may conflict with those who activate dangerous technology  
- **Leadership vacuum** — no built-in hierarchy; social dynamics determine group direction  
- **Optional PvP** — server configuration determines whether players can harm each other  

### Session Model

- **Listen server** — one player hosts and plays; up to 9 others connect  
- **Dedicated server** — headless server runs the world; all players connect as clients  
- **World persistence** — the server saves world state; players can reconnect to a continuing session  
- **Player absence** — disconnected players leave no physical presence; their contributions to the world remain  

### Multiplayer World Rules

- **Server-authoritative** — the server owns all game state to prevent cheating and ensure consistency  
- **Shared world** — block changes, entity states, and resource depletion are global  
- **Individual progression** — each player has their own inventory, health, and position  
- **Synchronized time** — day/night cycle is server-controlled; all players experience the same time of day  
- **Spawn point** — new and respawning players arrive at a fixed location; building a base near spawn is a natural first objective  

---

## Design Pillars

When in doubt, align features with these pillars:

- Discovery over exposition  
- Systems over scripted events  
- Meaningful building  
- Environmental mystery  
- Player-driven problem solving  
- Cohesive world logic  
- Cooperation through shared consequence  

Every mechanic should feel like a natural consequence of the world — whether experienced alone or with others.

---

## One-Sentence World Anchor

> You awaken on a floating artificial landmass guarded by forgotten machines. As you salvage lost technology, you uncover that the island is failing — and must decide whether to stabilize it or seek a way beyond. You may face this alone, or with up to nine others.

# 📜 Changelog

How Tiny Blacksmith grew, milestone by milestone.

---

## v0.1.0 — First playable release (October 2026)

The first build you can download and play without Unity. Get it from the **Releases** page.

---

## Development milestones

### 2026-09-28 — Main menu & settings
- New main menu with Continue, New Game, Load and Settings pages
- Hand-made wood-and-brass UI style, with full keyboard and mouse support
- Settings for volume and screen, saved between sessions

### 2026-09-27 — Living world, landmarks & scenery
- **World director:** every save gets its own seeded world, so each playthrough is different
- **City events:** 8 kinds of events that change the city and its customers
- Referrals, special commissions, race standing, and "notable" weapons that become famous
- A Chronicle page added to the journal
- **Landmarks:** castle, church, clock house and fountain, built in Blender
- Cream-stone castle on a hill, mountain ranges, a wider river and a new north quarter with bridges

### 2026-09-26 — Blender environment library
- All houses, props and trees rebuilt in Blender from reference sheets, using Python build scripts
- 63 shared materials, and prefabs placed in the village automatically

### 2026-09-25 — Characters & game clock
- **Skinned low-poly NPCs:** humans, elves and dwarves with modular hair, beards, hats and outfits
- Generated animations: walk, idle, talk, sit, give/receive and impatient
- **Game clock:** every action takes game time, the day runs from dawn to night, and customers arrive on a schedule
- Rush orders and multi-weapon contracts

### 2026-09-24 — City, NPC stories & order book
- **City:** an avenue, a plaza, a city wall and gate, a castle, and a river with bridges, a watermill and a tannery
- **Crowd:** townsfolk, merchants, guards and knights walk a real street graph
- **Persistent NPCs:** every customer has a name, age, traits, a backstory and life events
- **Order book:** accept orders, reserve weapons for them, meet deadlines, and get judged on pickup
- **Material Order Book + delivery crate:** order ore and it arrives the next morning
- Day/night cycle, lanterns, rain, ambient sounds
- Balance pass: metal unlocks, reputation tiers, an economy tuned with test bots

### 2026-09-23 — Life between swords
- **Economy:** gold, days, reputation, and customer archetypes in 4 tiers
- **Sword history:** every weapon gets an ID and a story, and can come back for repairs
- **Save system** and the player's **house + journal**
- **Village:** houses, a stream, bridges and paths around the smithy
- **5 weapon types:** sword, dagger, sickle, spear, hammer
- **3 races:** Humans, Elves, Dwarves
- Seasons, weather, maker's mark, scrap crate

### 2026-09-22 — Better stations
- New furnace mini-game: keep the fire inside your catcher bar
- Real models for the anvil, barrel and grindstone (with a spinning wheel)
- A 3D weapon rack in the storage area
- Customers walk in and offer orders

### 2026-09-21 — Main room & low-poly art
- Main room hub: turn between the forge, the meadow and storage
- Low-poly art pass for every workstation
- First customers and storage

### 2026-09-20 — Prototype 1: the forging loop
- Complete forging loop: **pick metal → furnace → anvil → water barrel → grindstone → result**
- Stats: Durability, Balance, Sharpness

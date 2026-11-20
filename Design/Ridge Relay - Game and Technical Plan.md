# Ridge Relay

**Working title.** A landscape mobile 3D footrace through a sandstone wind farm. Four animal couriers sprint automatically, swap lanes, jump hazards, and spend a held gadget to change the outcome of a 60–90 second race. The camera follows directly behind the local runner. A solo developer is the planning assumption. Android comes first, with iOS and live PvP after the offline slice.

## Research and references

| Reference | Observe | Apply to Ridge Relay |
|---|---|---|
| [Dirtybit Fun Run 4 overview](https://www.dirtybit.com/funrun4) and [official press kit](https://www.dirtybit.com/funrun4-press-kit) | Four-player Dash, 2v2 Duos, animal abilities, and pickups. Official screenshots show side-on framing with 3D art. | Retain the short four-runner race and item decisions. Build depth-based spacing, lane choices, turn anticipation, and rear-attack cues for the chase view. |
| [Dirtybit official gameplay trailer](https://www.youtube.com/watch?v=EtVLzDob8w0) | Compare how much of a hazard is visible before contact and how quickly racers rejoin the contest after a hit. | Prototype readable warning and quick recovery on a phone. Tune timing from our own playtests. |
| [SEGA HARDlight Sonic Forces mobile listing](https://play.google.com/store/apps/details?id=com.sega.sprint) and [mobile trailer](https://www.youtube.com/watch?v=PtzRHTJD2go) | Read lane occupancy, target silhouettes, incoming threats and tracks that curve into the distance. | Use a centered chase camera and lane-aligned telegraphs. Compare warning readability on a small screen. |
| [Unity 6.3 manual](https://docs.unity.com/en-us/engine/6000.3/manual) and [Blender FBX manual](https://docs.blender.org/manual/en/5.0/addons/import_export/scene_fbx.html) | Unity setup and baked animation export. | Verify one runner and one meter-scale track module before building the art kit. |
| [Unity competitive multiplayer quickstart](https://docs.unity.com/en-us/multiplayer/quickstarts/competitive-quickstart), [Relay](https://docs.unity.com/en-us/mps-sdk/networking/relay-servers), and [NGO anticipation](https://mp-docs.dl.it.unity3d.com/netcode/current/advanced-topics/client-anticipation/) | Dedicated authority, private Relay routing, and client anticipation limits. | Prove online race feel before building queues, ranks, or hosting infrastructure. |

During reference play, record three races per game. For each obstacle note camera framing, first visible cue, required move, contact time, and recovery time. For items record where the cue appears when the attacker is behind. These timings are research tasks and have yet to be measured. Use publisher materials as study references and original assets in production.

## Playable slice

- One player, three bots, one point-to-point course, one playable rig with color variants, three gadgets, and a local best time. Flow is Play → countdown → race → results → retry.
- Auto-run follows the track heading. Swipe horizontally to change one of three lanes. Tap a bottom-corner jump button or the opposite gadget button. Allow simultaneous jump and lane inputs. Add keyboard bindings for development.
- Route **Windmill Gorge** opens with lane changes and a single hurdle, builds into a banked curve, then offers a risky jump shortcut before a wide final sprint. Every section has a clear traversable route. Add a split/rejoin after the basic route is readable.
- Pickups fill one slot. **Tailwind** gives a short speed burst. **Bristle Pad** leaves a visible temporary slowing patch in your lane. **Shell Film** absorbs one hit within a short window. Test spawn spacing, repeated hits, shields and jump avoidance. Falls return through a checkpoint with a brief delay and immunity.
- HUD shows place, course progress, jump and held item. Threats outside the view get a directional marker plus an audio cue. Use shape as well as color. Add a reduced-motion setting and mirrored buttons during polish.
- Later scope includes human races, individual character abilities, a second track, iOS, matchmaking, and 2v2. Ranked play and persistent progression come after race quality and server validation.

## Camera and track prototype

Start with a centered camera about 5 m back and 2 m above the runner, approximately 55° vertical FOV, and a look target 8–12 m along the course. These are tuning values. Track yaw comes from the path tangent ahead. Blend lateral motion so a lane change stays legible, damp vertical jumps, keep the horizon level on banks and reset the rig on respawn. Use a short obstruction cast and avoid overhead geometry in the first course. Compare fixed FOV against a small optional boost effect on device.

Start around 9 m/s, with lanes spaced 2 m apart and a roughly 0.2 s lane transition. Put first warnings about 12–16 m ahead of the decision and derive the real visible window from speed and geometry, including while boosted. Test jump landing visibility, outside-lane camera clipping, split/rejoin orientation, thumb occlusion and 16:9 to 20:9 framing. Target four of five new testers completing a race unaided after one practice attempt. Record causes of misses before changing control sensitivity.

## Unity architecture

Use a pinned Unity 6.3 LTS patch with URP and Input System. Start with a small custom camera rig following a track-relative pivot. Evaluate Cinemachine if blending or obstruction handling begins to dominate camera code.

- **Track and progress.** Author a centerline with sampled position, tangent and up, lane offsets, checkpoint gates, hazards, pickups, and segment IDs. Store progress as an ordered gate plus normalized distance within the gate interval. Map split routes to the same progress interval and prevent skipping gates. Finish order uses validated crossings and a stable tie rule.
- **Input and movement.** Touch, keyboard and bots produce the same tick-indexed commands. A kinematic RunnerMotor owns forward speed, lateral interpolation, grounded state, jump, status effects and checkpoint recovery. Use swept collision checks where needed and explicit obstacle volumes. Animation is in-place. Track queries and motion state stay independent of touch UI, Animator and network components.
- **Race rules.** RaceSimulation owns countdown, movement tick, pickup arbitration, gadget state, progress and results. Start with a fixed 60 Hz local step. Resolve same-tick pickups with a stable tie rule and guard one-shot effects against duplicate application. ScriptableObjects hold authored runner, track and gadget configuration. Live state is per race and resets on retry.
- **Presentation.** Runner view, camera, HUD, audio, haptics and pooled VFX consume snapshots and events. Hide camera corrections via a smoothed view target. Render frame rate stays independent of simulation ticks.
- **Bots.** Look ahead for obstacles and nearby rivals using the same track data. Use configurable reaction delays and seeded decisions. Restrict bots to the same legal commands and speed limits as players.
- **Online.** Test NGO and Unity Transport with a private join-code session through Unity Multiplayer Services and Relay. Host runs authoritative rules for the prototype. Sequence inputs, deduplicate one-shot commands, replicate tick/acknowledgment, progress, charges and results, and interpolate remote snapshots. Begin with local visual anticipation. If gameplay responsiveness requires prediction, prototype replay/reconciliation for the small kinematic state and measure corrections. For ranked build, run the simulation on a dedicated server, validate inputs and issue results server-side. Test host exit, app suspend, late packets and disconnects. Initial private matches can return affected players to the menu with a clear message.

Suggested folders under Assets/RidgeRelay are Scenes, Scripts, Prefabs, Data, Art, Audio and UI. Split scripts by Race, Track, Runner, Items, Presentation and Network when those boundaries become useful. Store editable Blender sources under ArtSource outside Assets. Commit Unity metadata with assets, pin project settings and package versions, and use Git LFS for large source binaries if version control is configured.

## Blender and art production

- **Direction.** Chalk sandstone, teal equipment and water, coral hazard markers, ochre cloth, and lavender shadows. Broad forms and clean mid-distance silhouettes. Couriers carry distinctive backpacks so they're identifiable from behind. The lead is a coral jerboa with a teal vest and ochre pack.
- **Runner.** Block out silhouette at gameplay-camera size. Model and UV a single skinned mesh, bind a simple biped armature with ear and tail bones, and animate in-place idle, run, jump, fall, land, stumble, finish and gadget use. Initial budget is 4–8k triangles, one material and one 1024px atlas per shared rig. Use capsule collision separate from the mesh.
- **Environment.** Make grid-snapped straight and curved decks, ramp, wide landing, cliff, rail, wind gate, windmill, chevrons and hazard props. Match module pivots and road seams to the Unity centerline. Use shared atlases, simple collision and cheaper distant meshes.
- **Handoff.** Use metric scale, predictable pivots, applied transforms before rigging, and baked actions. Export selected mesh and deform armature as FBX. In Unity use a Generic rig with root motion disabled, explicitly assign URP materials and loop only appropriate clips. Validate a one-meter reference, facing, foot contact, clip boundaries and texture compression on phone before exporting the rest.
- **Audio and feedback.** Produce footfalls, landing, pickup, hazard warning, hit, boost, countdown and finish cues. Distinguish warnings from reward sounds and check legibility over music.

## Development milestones

Estimates are focused solo workdays. Online and art estimates can grow with playtest findings.

| Step | Working build | Exit check | Estimate |
|---|---|---|---|
| 1. Project setup | URP project, Android build profile, test scene and touch input | Install a landscape build on a physical phone | 1–2 days |
| 2. Camera and controls | Capsule runner on a straight, bend, crest and jump | Read the landing, keep runner visible through turns, and complete the loop on touch | 3–5 days |
| 3. Race loop | Countdown, ordered checkpoints, three bots, respawns, finish and retry | Finish order stays correct across falls and shortcuts. Ten retries reset cleanly | 4–6 days |
| 4. Gadgets and HUD | Three gadgets, one item slot, HUD, warning cues | Verify simultaneous pickups, shields, offscreen threats and back-to-back hits | 3–5 days |
| 5. Art vertical slice | One Blender rig, modular course, effects, sound and results screen | A complete 60–90 second race reads on a phone with final camera distance | 7–12 days |
| 6. Device and UX pass | Quality tiers, onboarding, safe areas and reduced motion | 60 FPS target on selected mid-tier Android and stable 30 FPS on selected lower tier during a 15-minute loop. Capture frame timing and thermals | 3–6 days |
| 7. Private online prototype | Join code, multi-device race, server-driven items and results | Four clients agree on results. Record responsiveness at 50/100/150 ms RTT and 0–2% loss, plus a disconnect case | 5–10 days |
| 8. Candidate release | Accessibility pass, licensing inventory, platform setup and closed testing | Resolve release-blocking bugs and verify actual target devices and current store requirements | 4–8 days before store review |

Capture CPU and GPU frame time, memory and allocations on device, especially during the busiest item exchange and after repeated retries. Start with baked lighting on static geometry, pooled effects and modest shadows. Profile before raising material complexity or effect counts.

The first implementation ticket is the Android chase-camera course using primitives. It needs a straight, a banked bend, a blind crest and a jump, plus touch lane change and jump. Record a full device run with camera tuning values before committing to final models.

## Concept art

- [Chase camera](Concepts/chase-camera.png)
- [Jerboa turnaround](Concepts/character-sheet.png)
- [Windmill Gorge and modular track kit](Concepts/track-kit.png)
- [Image generation prompts](Concepts/Image%20Prompts.md)

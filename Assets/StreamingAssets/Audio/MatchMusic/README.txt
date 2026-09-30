Reactive match music — Options > Gameplay > Reactive Match Music.

PC DROP-IN (next to DCGO.exe)
-----------------------------
  Assets/Audio/MatchMusic/pack1/music1.mp3   (full security 5+)
  Assets/Audio/MatchMusic/pack1/music2.mp3   (half 2-4)
  Assets/Audio/MatchMusic/pack1/music3.mp3   (critical 0-1)

STANDARD LAYOUT
---------------
Each music group is a folder under MatchMusic with fixed track names:

  pack1/music1   (full security 5+)
  pack1/music2   (half 2-4)
  pack1/music3   (critical 0-1)

  pack2/music1
  pack2/music2
  pack2/music3

catalog.json lists pack folder names only:

{
  "packs": [ "pack1", "pack2" ]
}

When a match starts, one pack is chosen at random.

DEFAULTS (when a pack file is missing)
--------------------------------------
  full     -> Assets/Sound/BGM/BGM_Battle3.mp3
  half     -> Assets/Sound/BGM/BGM_Battle4.mp3
  critical -> Assets/Sound/BGM/BGM_Battle.mp3

Supported formats: .ogg (preferred), .mp3, .wav
(music1 / music2 / music3 — extension is auto-detected)

On Android, every pack listed in catalog.json must exist in the build.

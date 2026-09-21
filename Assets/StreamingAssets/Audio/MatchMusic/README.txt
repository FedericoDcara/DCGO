Reactive match music for Options > Gameplay > Reactive Match Music.

STANDARD LAYOUT (required)
--------------------------
Each music group is a folder under MatchMusic with fixed track names:

  Audio/MatchMusic/pack1/music1.mp3   (full security 5+)
  Audio/MatchMusic/pack1/music2.mp3   (half 2-4)
  Audio/MatchMusic/pack1/music3.mp3   (critical 0-1)

  Audio/MatchMusic/pack2/music1.mp3
  Audio/MatchMusic/pack2/music2.mp3
  Audio/MatchMusic/pack2/music3.mp3

catalog.json lists pack folder names only:

{
  "packs": [ "pack1", "pack2" ]
}

When a match starts, one pack is chosen at random.

Supported formats: .ogg (preferred), .mp3, .wav
(music1 / music2 / music3 — extension is auto-detected)

On Android, every pack listed in catalog.json must exist in the build.

Place official (compiled) profile-icon images here.
They ship inside the game build and appear first in Options > Play Area > Icon.

Recommended size: 256x256 (square).
In Unity Inspector, set Texture Type = Sprite (2D and UI) if it does not auto-detect.

The filename (without extension) becomes the picker label and the synced id:
  MyIcon.png  ->  res:MyIcon

Both players need the same build to see these icons.
For personal extras that are not in the build, use:
  Assets/StreamingAssets/Textures/ProfileIcons/

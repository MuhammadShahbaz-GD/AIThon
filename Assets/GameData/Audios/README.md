# Candy Plastic Doll Audio Library

This folder contains the project's original, royalty-free audio masters.

- `SFX`: short mono gameplay, UI, character, tool, cannon, and flow cues.
- `Music`: seamless stereo menu and gameplay loops with subtle candy-factory ambience.
- `Candy Plastic Doll Audio Catalog.asset`: the shared runtime catalog used by every scene.
- `Generation/generate_candy_plastic_audio.py`: deterministic NumPy generator for all WAV masters.

Run the generator from the project root when source audio must be rebuilt:

```powershell
python "Assets/GameData/Audios/Generation/generate_candy_plastic_audio.py"
```

Then use **Tools > Gameplay > Audio > Setup Candy-Glass Audio** to reimport clips,
refresh the catalog, and wire scenes. Use the adjacent validation commands before a build.

Short SFX are imported as mono ADPCM and decompressed on load. Music remains stereo,
Vorbis-compressed, background-loaded, and preloaded. Runtime concurrency, cooldown,
pitch variation, bus volume, and voice priority are authored in the AudioCatalog.

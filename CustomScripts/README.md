# Custom Skua scripts

This folder keeps our locally maintained farm scripts and launcher in the Skua-Linux repository, separate from the official script tree that can be replaced by script updates.

- `HDBoN/HollowbornDragonBladeOfNulgath.cs`: Bengt's ordered HDBoN progression runner. It runs SmartEnhance after each successful class change made through `Core.EquipClass`, once per distinct class.
- `ButlerV4/Butlerv4.cs`: the TCP Butler V4 used by the farm.
- `HDBoN/HDBoNConsumables.cs`: stocks battle consumables and applies tonic and elixir before combat.
- `start_farm.sh`: launcher for Bengt and five Butler clients, including Little_mett and Bella of Worlds.

The launcher reads the saved account credentials from the local Skua settings file. In the HDBoN setup, the leader waits for the five helpers to reach the same boss cell and pauses after three leader deaths on a boss. Run it from this checkout with `bash CustomScripts/start_farm.sh`.

- `Ultras/UltraTyndarius.cs`: 4-account coordinated daily runner for `/ultratyndarius` (Quest `8245`, `Avatar Tyndarius Insignia`, 2 insignias/day). Uses `bengt of worlds` (`Legion Revenant` with `Ravenous` + `Penitence`, `Body Tonic` + `Potent Malevolence Elixir`), `Little_mett` (`ArchPaladin` with `Lacerate` + `Penitence`, smart heal priority `2 (<70% HP) | 3 | 2 | 1` + direct `Body Tonic` craft fallback from stocked reagents), `MrPresident BR` (`StoneCrusher` with `Forge` + `Valiance` + `Penitence`, orb burn + mutual `BossFocus` lease backup taunt), and `LeesDragon Prime` (`Lord of Order` with `Awe_Blast`).
- `Ultras/start_tyndarius.sh`: 4-client launcher (`Twilly`, 4s launch stagger, clears stale `ultra_tyndarius*.sync` and `UltraTyndarius*.sync` files).

Run `/ultratyndarius` daily from this checkout via `systemd`:
```bash
systemd-run --user --collect --unit=skua-tyndarius --working-directory=/home/max/Skua-Linux /bin/bash /home/max/Skua-Linux/CustomScripts/Ultras/start_tyndarius.sh
```
Check progress/results in `~/.config/Skua/logs/tyndarius_{bengt,little_mett,mrpresident,leesdragon}.log` and stop afterwards with `systemctl --user stop skua-tyndarius`.

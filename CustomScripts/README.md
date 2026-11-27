# Custom Skua scripts

This folder keeps our locally maintained farm scripts and launcher in the Skua-Linux repository, separate from the official script tree that can be replaced by script updates.

- `HDBoN/HollowbornDragonBladeOfNulgath.cs`: Bengt's ordered HDBoN progression runner. It runs SmartEnhance after each successful class change made through `Core.EquipClass`, once per distinct class.
- `ButlerV4/Butlerv4.cs`: the TCP Butler V4 used by the farm.
- `HDBoN/HDBoNConsumables.cs`: stocks battle consumables and applies tonic and elixir before combat.
- `start_farm.sh`: launcher for Bengt and four Butler clients, including Little_mett.

The launcher reads the saved account credentials from the local Skua settings file. In the HDBoN setup, the leader waits for the four helpers to reach the same boss cell and pauses after three leader deaths on a boss. Run it from this checkout with `bash CustomScripts/start_farm.sh`.

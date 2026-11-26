# Custom Skua scripts

This folder keeps our locally maintained farm scripts and launcher in the Skua-Linux repository, separate from the official script tree that can be replaced by script updates.

- `HDBoN/HollowbornDragonBladeOfNulgath.cs`: Bengt's ordered HDBoN progression runner. It runs SmartEnhance after each successful class change made through `Core.EquipClass`, once per distinct class.
- `ButlerV4/Butlerv4.cs`: the TCP Butler V4 used by the farm.
- `start_farm.sh`: launcher for Bengt and the three Butler clients.

The launcher reads the saved password from the local Skua settings file; no credential is stored in this repository. Run it from this checkout with `bash CustomScripts/start_farm.sh`.

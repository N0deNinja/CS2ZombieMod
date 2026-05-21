# Invisible Knife Replacement Model

This is a tiny transparent mesh intended to test replacing the zombie knife
visual while keeping `weapon_knife` active for gameplay and hit detection.
The plugin can force the knife econ item definition to `516` for Shadow Daggers
animations. `weapon_knife_push` was tested as a spawned weapon entity, but it
caused knife drops and broke the zombie attack/viewmodel path locally.

Source files are generated into `source/` by:

```bat
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --python tools\create-invisible-knife-asset.py
```

The important output is:

```text
assets/zombiemod/invisible_knife/source/zm_invisible_knife.fbx
```

Import that FBX into a CS2 Workshop Tools addon with ModelDoc, assign a fully
transparent material if ModelDoc does not preserve the Blender alpha material,
and compile it to:

```text
models/zombiemod/viewmodels/v_invisible_knife.vmdl
```

After the compiled model is mounted on both the server and client, enable:

```text
ZombieMeleeVisualConfig.EnableZombieKnifeReplacementModel = true
ZombieMeleeVisualConfig.ZombieKnifeReplacementModelPath = "models/zombiemod/viewmodels/v_invisible_knife.vmdl"
ZombieMeleeVisualConfig.ZombieMeleeWeaponName = "weapon_knife"
ZombieMeleeVisualConfig.ZombieMeleeItemDefinitionIndex = 516
```

Do not enable the replacement before the compiled `.vmdl` exists and is mounted,
or CS2 will render the giant missing-model `ERROR` placeholder.

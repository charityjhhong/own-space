# Contributing

Rules for agents are in [AGENTS.md](AGENTS.md). This file covers one-time machine setup.

## One-time setup

1. Unity **6000.3.20f1** with Android Build Support.
2. Git LFS:
   ```sh
   brew install git-lfs
   git lfs install
   ```
3. UnityYAMLMerge for scenes and prefabs:
   ```sh
   git config merge.unityyamlmerge.name "Unity YAML merge"
   git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
   git config merge.unityyamlmerge.recursive binary
   ```

## Checks

- Repository checks run in CI on every PR: `.meta` files, the agent boundary check, and no Korean in tracked files.
- Unity compile and EditMode tests run locally until CI has a Unity license:
  ```sh
  .github/scripts/unity-check.sh
  ```
  Close the Unity Editor on `app/` first; batch mode can't open a project that is already open.

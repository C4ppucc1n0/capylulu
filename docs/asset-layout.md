# 素材目录与构建约定

所有成品素材与人物参考归到项目根目录 `assets/`，分类保留，不把图集、GIF 与参考图片平铺混放。

| 原目录 | 当前目录 | 用途 | 是否打包 |
| --- | --- | --- | --- |
| `generated_actions/` | `assets/pet-atlases/` | 宠物动作图集及同名 `.pet.json` | 是 |
| `gif_resources/` | `assets/animations/` | 演出 GIF、配套封面、消消乐方块与结算 GIF | 是 |
| `video_references/` | `assets/character-references/` | 每段视频的参考 JPG、时间戳清单及浏览页 | 否 |

## 输入到产物

`video/` 原始视频 → `video-character-reference` 提取 → `assets/character-references/` → `capylulu-pet` 选取少量参考并生成、验收 → `assets/pet-atlases/`。

人物参考浏览入口是 `assets/character-references/index.html`，组内链接采用相对路径。提取脚本和宠物素材准备脚本的默认目录已对齐；临时工作区仍使用 `.pet-work/`，验收证据仍使用 `artifacts/pet-qa/`。旧临时工作区内记录的绝对路径是历史信息，迁移后应使用新工作区准备素材。

演出素材继续使用 `assets/animations/match-game/block/` 和 `assets/animations/match-game/celebrate/`；编码与验收要求见 [GIF 规范](gif-extraction-standards.md)。参考 JPG 不是可直接播放的精灵帧，不得混入图集目录。

## 兼容边界

- 只迁移目录，不重编码或重命名素材，不改变图集尺寸、帧序、角色 ID、清单格式。
- `CapyLulu.csproj` 从新路径显式收录资源，但保持 `CapyLulu.GeneratedActions.*`、`CapyLulu.GifResources.*` 内部标识。这些是 EXE 内部逻辑名称，不是旧磁盘目录残留。
- 不使用覆盖整个 `assets/` 的递归打包规则，避免将参考 JPG、HTML 和索引编进 EXE。
- 字体、文案、屏幕庆祝 Emoji 等仍在 `src/CapyLulu/Resources/`，本次不迁移。
- `build.ps1` 中旧名称仅用于清理 `dist/CapyLulu/` 下早期版本的外置副本，不作为当前输入路径。

## 验证

```powershell
python tests/asset_layout_test.py
.\.dotnet\dotnet.exe run --project tests\CapyLulu.Validation\CapyLulu.Validation.csproj --configuration Debug
.\build.ps1
```

目录检查覆盖两项技能的默认输入/输出、参考页本地链接和工作区隔离；应用验证覆盖正式图集、演出素材、24 个方块与 3 段结算动画。发布后仍只需分发一个 EXE。

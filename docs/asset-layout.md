# 素材目录与构建约定

成品素材位于 `assets/`；新的动作参考和语义索引位于 `reference-library/`，与提取工具及临时生成工作区分开。原有参考技能继续使用 `assets/character-references/`。

| 原目录 | 当前目录 | 用途 | 是否打包 |
| --- | --- | --- | --- |
| `generated_actions/` | `assets/pet-atlases/` | 宠物动作图集及同名 `.pet.json` | 是 |
| `gif_resources/` | `assets/animations/` | 演出 GIF、配套封面、消消乐方块与结算 GIF | 是 |
| `video_references/` | `assets/character-references/` | 每段视频的参考 JPG、时间戳清单及浏览页 | 否 |
| `video-actions/output/` | `reference-library/actions/` | 连续动作帧、八帧摘要、原速预览、来源及历史实验记录 | 否 |
| 新增 | `reference-library/semantics/` | 带来源证据的动作与形态语义索引 | 否 |
| `.pet-work/`、根目录 `output/` | `artifacts/work/` | 按任务保存的生成输入、提示词、候选、缓存及临时报告 | 否 |
| `test-artifacts/` | `artifacts/tests/` | 测试截图、日志及本地调试材料 | 否 |

开发过程产物统一放在 `artifacts/` 下，新任务使用 `artifacts/work/<run>/`，测试输出使用 `artifacts/tests/`。`.gitignore` 默认忽略 `artifacts` 下的产物，仅放行原有 `pet-qa/` 验收资料，并保留其中已有的本地排除项。任务完成且必要记录已保留后再清理工作文件；未完成任务的输入、提示词和续跑信息继续保留。

## 输入到产物

当前动作流程：`video/` → `video-actions/extract.py` → `reference-library/actions/` → `reference-library-semantics` 按需标注 → `reference-library/semantics/index.jsonl`。`pet-action-atlas` 以固定角色图及身体约束为必备输入，从素材库补充动作或形态证据，完成生成与验收后才输出到 `assets/pet-atlases/`。

原有流程仍可用：`video/` → `video-character-reference` → `assets/character-references/` → `capylulu-pet` → `assets/pet-atlases/`。这两类参考目录不能互换读取，它们的清单结构不同。

新动作素材入口是 `reference-library/actions/index.html`，原有人物参考入口是 `assets/character-references/index.html`，均为生成后的本地浏览页。新素材库迁移保留分组名称和组内相对路径；历史报告中的绝对路径只代表当时位置。临时生成输入、提示词和试产结果放在 `artifacts/work/`，需要保留的最终验收证据放在 `artifacts/pet-qa/`。

演出素材继续使用 `assets/animations/match-game/block/` 和 `assets/animations/match-game/celebrate/`；编码与验收要求见 [GIF 规范](gif-extraction-standards.md)。参考 JPG 不是可直接播放的精灵帧，不得混入图集目录。

## 版本管理与恢复

标准 v2 横条的后处理使用 `pet-action-atlas/scripts/atlas_pipeline.py`，见 [程序处理说明](../.agents/skills/pet-action-atlas/references/program-processing.md)。配置、行缓存及候选预览保存在 `artifacts/work/<run>/`；视觉验收后再按上述正式目录交付，程序不自动发布或改变运行时契约。

- 提交工具代码、测试、技能、文档和可复用的语义记录。语义记录使用相对路径、源视频哈希和时间区间定位证据；模型标注不等于人工验收。
- `reference-library/actions/` 和 `assets/character-references/` 的提取媒体与浏览页不提交 Git；在本机长期保存。虚拟环境、缓存、`artifacts/work/`、`artifacts/tests/` 和构建产物也不提交。
- 新检出仓库按 [提取工具说明](../video-actions/README.md) 安装依赖后，运行 `video-actions/extract.py video` 恢复动作媒体。历史实验图不属于普通提取输出，文档中的相关链接仅在保留实验记录的本机可用。
- 语义记录可以随仓库保存，但不能据此假定本机证据已存在。恢复媒体后检查视频哈希、时间区间和图片指纹；跨机器分组目录名可能变化，按稳定片段键重新定位，证据变化时补标，保留锁定的人工记录。

## 兼容边界

- 参考目录迁移不改变正式图集尺寸、帧序、角色 ID 或清单格式；新的生成流程也不自动修改运行时。
- `CapyLulu.csproj` 从新路径显式收录资源，但保持 `CapyLulu.GeneratedActions.*`、`CapyLulu.GifResources.*` 内部标识。这些是 EXE 内部逻辑名称，不是旧磁盘目录残留。
- 构建继续显式收录正式资源，避免将 `assets/character-references/` 或 `reference-library/` 的媒体和索引编进 EXE。
- 字体、文案、屏幕庆祝 Emoji 等仍在 `src/CapyLulu/Resources/`，本次不迁移。
- `build.ps1` 中旧名称仅用于清理 `dist/CapyLulu/` 下早期版本的外置副本，不作为当前输入路径。

## 验证

```powershell
python tests/asset_layout_test.py
.\.dotnet\dotnet.exe run --project tests\CapyLulu.Validation\CapyLulu.Validation.csproj --configuration Debug
.\build.ps1
```

`asset_layout_test.py` 检查原有参考流程，需要先具备本地 `assets/character-references/`。新动作提取工具可独立运行以下测试，不依赖已有素材库：

```powershell
.\video-actions\.venv\Scripts\python.exe -m unittest discover -s video-actions -p "test_*.py" -v
```

应用验证覆盖正式图集、演出素材、24 个方块与 3 段结算动画。仅修改技能、文档或提取工具时无需重新构建 EXE；交付运行时素材时再执行应用验证和构建。

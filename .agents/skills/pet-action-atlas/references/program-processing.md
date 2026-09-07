# CapyLulu v2 程序处理

仅在目标是本项目标准 8×11 图集时使用。其他布局、现成图集直接保留网格或非独立横条输入，不强行转换成此配置，继续复用适配工具。

## 两个命令

使用已安装 Pillow 的 Python；程序复用 `hatch-pet/scripts`，默认从 `CODEX_HOME` 或用户 `.codex` 解析，可用 `--helpers <目录>` 指定，不自动下载或调用图像 API。

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_pipeline.py init --run .pet-work/<run>
python .agents/skills/pet-action-atlas/scripts/atlas_pipeline.py build --run .pet-work/<run>
```

`init` 只生成 `pipeline.json`，已有配置不覆盖。把逐行生成结果保存到该目录的 `decoded/<行名>.png`，或修改 `rows.<行名>.source`。所有相对路径按工作目录解析。先检查固定角色、背景与输入行，再运行 `build`，不必阅读脚本或临时编写拼接代码。

行名和数量自动初始化：idle 6、running-right 8、running-left 8、waving 4、jumping 5、failed 8、waiting 6、running 6、review 6、look-row-9 8、look-row-10 8。语义以 [项目契约](../../capylulu-pet/references/atlas-contract.md) 为准；不是通用宠物动作菜单。

## 只调整必要参数

- `chroma_key`、`threshold`：默认绿色和 96，须按角色配色选择不冲突的键色。真正的 alpha 输入保留透明度；画出来的棋盘格不是透明背景，会被当成错误输入而非自动消除。
- 每行 `height`：首帧目标高度，默认 170；整行共用同一缩放。可设置明确的 `scale` 覆盖它，例如移植此前已核实的整行缩放。**注视首帧可能低头，不能靠相同高度自动证明体型一致**，应对照中性姿态的身体／头部尺寸再调整整行尺度。
- `baseline`、`offset_x`：默认脚底 199、水平偏移 0。按下半身中心对齐；不适合该锚点的素材改用适配处理，不强行接受。
- `preserve_y`：保留源图相对首帧的脚底位移；提起和左右拖动默认开启，其他行默认关闭。输入有横向轨迹、特殊锚点或非独立姿态时，此简化处理不适用。
- `durations_ms`：可覆盖该行预览时长，长度必须等于有效帧数。默认复用既有工具时序；提起预览附加逆序展示放下。**这只配置预览，不修改应用播放时序。**

不把某个角色的短裤颜色、体型倍率或具体动作硬编码进工具。初始数值只是本项目工作起点，不是质量合格证。

## 自动结果与返工

`build` 完成分离姿态、整行尺度、下半身对齐、透明边缘清理、WebP 拼接、必需／空槽校验、逐行 GIF、16 方向对照和 HTML 预览；第 0 行第 6 格自动放置中性姿态。尺寸保持 1536×2288、单格 192×208。

终端只返回结果路径、耗时、处理行数和缓存命中数。细节在 `processing.json`，需要诊断时才读，不把全部像素报告、提示词或生成图片 data URL 打进上下文。程序不评判形象或动作，不把数值通过写成视觉通过。

源文件内容、参数和工具实现参与缓存指纹。修改一行再执行同一命令，其他行复用缓存；整体仍重新拼接、清边和验证。不确定姿态数量、触边或缩放溢出时报错，不自动丢帧、裁切、逐帧缩小或用等分猜测来凑齐。

候选保存在 `.pet-work/<run>/pipeline-output/<指纹>/`，缓存位于同一 run 的 `pipeline-cache/`；旧候选保留。失败返回非零，不能使用失败目录作为交付物。相同输入复跑可复用已核验的行缓存，**仍需最后的整套视觉复核**。

检查候选的总览、实际尺寸动作播放、16 方向与跨行衔接，记录视觉结论。播放访问受限时如实保留未验证项，不绕过浏览器限制、不将静帧检查冒充动态验收。

通过验收后，按项目目录规范复制 WebP 与既有契约的同名 `.pet.json` 到 `assets/pet-atlases`；精选 QA 放 `artifacts/pet-qa/<run>`，其中预览链接需指向最终图集，不复制原始行图或重复最终图集。**程序不自动发布、覆盖角色、生成通过结论、打包 EXE 或 push。** 是否打包应用按用户任务范围决定。

维护脚本时运行 `python -B tests/pet_atlas_pipeline_test.py`。真实素材回归应额外比较解码后 RGBA 像素、未变行的一致性，以及修改一行后的缓存命中数；不为验证后处理而重复调用图像生成。

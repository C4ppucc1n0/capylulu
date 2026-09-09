# 任务目录与复盘记录

新建及已有动作校准都使用 `artifacts/work/<run>/`。任务名表达角色及本次工作；同一次返工复用任务，换输入图或目标则新建。原始文件保留，输入副本、输出和关键事实集中保存。此目录默认不提交 Git，技能代码及简短回归结论可以提交。

```text
artifacts/work/<run>/
  run.json                 模式、输入来源／指纹、最新验收状态
  inputs/                  原图、选定参考、布局与配置的内容指纹副本
  motion-lock.json          角色纠正项、尺度依据、各行易丢失的原动作关键帧
  requests/                实际提示词、按顺序附上的图片、工具／模型、重试原因
  decoded/                 工具实际返回的行图，各次结果独立保留
  pipeline.json            标准 v2 新建流程的处理配置
  pipeline-cache/          标准流程可验证的行缓存
  pipeline-output/         所有候选与处理报告
  reviews/                 原图对照、尺寸报告、验收证据与结论
  events.jsonl             按时间追加的执行事实
  timeline.md              工具自动生成的可点击执行轨迹
  summary.md               结果、未完成项和可直接打开的本地链接
```

`inputs` 中按内容指纹保存副本，并记录原路径、真实图片格式、尺寸及 SHA-256。扩展名不一定代表编码；工具按图片实际格式命名副本。无需重复复制整个素材库，只保存实际选用的证据。日志记录可观察的决策与工具事实，不保存密钥、授权头或私有推理。

## 开始与绘图记录

在项目根目录使用已验证可用的 Python（需要 Pillow）。以下示例中的路径和行名按任务替换：

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_run.py init --run artifacts/work/lulu-align --mode align --source raw_images/spritesheet.webp --reference <选定参考图>
```

新建用 `--mode generate`，可用 `--source` 保存固定角色图；标准 v2 的 `atlas_pipeline.py init` 也会初始化任务记录。源图变化时工具拒绝复用旧任务。将此次角色纠正项、尺度依据、原动作的关键帧观察写入 `motion-lock.json`，不要直接照抄别的角色或行号。

每次绘图 **调用前** 记录实际使用的提示词与全部图片，图片顺序与实际请求一致：

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_run.py request --run artifacts/work/lulu-align --row row-02 --prompt <提示词文件> --image <原动作行> --image <共用角色图> --image <本行参考图> --tool image_gen --reason "修正口鼻厚度，保留支撑脚和眼睑阶段"
```

工具返回请求 ID 及已保存的图片路径。实际调用使用这些副本，模型名称已知时填写 `--model`，未知时留空。记录工具不调用图像 API、不替代图像技能，也不产生额外授权要求。

调用结束后关联真实返回文件；失败也立即记录。记录命令按顺序执行；图像调用可按主技能约定并发，返回后分别关联请求。重试创建新请求并填写原因，不能覆盖上一次结果：

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_run.py result --run artifacts/work/lulu-align --request <请求ID> --file <返回的行图>
python .agents/skills/pet-action-atlas/scripts/atlas_run.py result --run artifacts/work/lulu-align --request <另一个请求ID> --error "工具返回的实际错误摘要"
```

`result` 保存返回文件并计算从记录请求到关联结果的实际耗时。一次任务中断时保留 started 状态，恢复后说明中断，不能补造已完成调用。已有图集仅有最终文件而缺少生成记录时，明确标为历史导入，不编造提示词、模型或执行参数。

标准 v2 处理时，将返回的 `result.path` 填入 `pipeline.json` 对应 `rows.<行名>.source`；其他布局的适配程序也读取该已保存结果，不必改名覆盖之前的返回文件。

## 原图与候选对照

使用当前输入的明确布局 JSON：`width`、`height`，以及 `rows` 中的 `row`、`slots`；每个槽位包含 `rectangle: [left, top, right, bottom]` 与 `occupied`。可以读取已有可信布局，但须核对本次源图；不根据 9 行或 11 行猜测动作名称。已知帧时长写入每行 `durations_ms`，未知则工具标明人工复查速度。

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_compare.py compare --run artifacts/work/lulu-align --candidate <组装候选图> --layout <本次布局.json>
```

候选被复制到任务内，输出 `reviews/<对照指纹>/index.html`、逐行同尺度 PNG／GIF、`comparison.json` 和 `assessment-template.json`。默认尺寸差异 10%、位置差异 3 像素仅触发复核提示，可通过命令参数调整；工具不会自动改变人物大小，也不声称识别了换脚、闭眼或人物身份。

结构检查验证画布与槽位；视觉检查由实际看图／播放的人或模型完成。复制验收模板为自己的 assessment 文件，填写 reviewer，逐行为 `appearance`、`motion`、`geometry`、`playback` 填写 `pass`、`fail` 或 `pending`，并用 evidence 写具体观察、失败帧或经证据支持的比例改动。未播放就保持 pending。

```powershell
python .agents/skills/pet-action-atlas/scripts/atlas_compare.py review --run artifacts/work/lulu-align --comparison <本次comparison.json> --assessment <已填写的assessment.json>
```

工具检查对照 ID、文件指纹、逐行覆盖和观察记录。结构失败或视觉任一失败为 rejected；有未检查项为 pending；全部通过才记录 accepted。新候选重新比较、重新验收，不能沿用旧图的通过结论。验收后按项目约定交付正式素材及精选 QA；本工具不自动发布或打包。

结束时写 `summary.md`：采用哪些参考、实际改动与关键返工、输入和最终候选链接、结构／形象／动作／尺度／播放分别通过了什么、未完成项。`timeline.md` 自动列出重点事件及文件链接，`events.jsonl` 保留机器可读明细，避免只有一张最终图而无法复盘。

# 视频动作参考提取

独立的项目命令行工具，不调用模型，不依赖 Codex 技能。把一个或多个视频切成动作候选，每个候选输出按时间排序的连续采样 JPG、默认 8 帧摘要、联系图、无声原速 MP4 预览和来源清单。用于后续理解和重绘动作，不直接生成宠物图集。

## 安装与运行

需要 Python 3.11+。FFmpeg 随 `imageio-ffmpeg` 安装，不要求另外安装 FFprobe。

在项目根目录运行一次：

```powershell
python -m venv video-actions/.venv
.\video-actions\.venv\Scripts\python.exe -m pip install -r video-actions/requirements.txt
```

提取整个目录，或指定多个视频：

```powershell
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video/22e8cf8ca76742f928803ac3d02c3292.mp4 video/b31bb762450eb79cbf2138af5e10faf4.mp4
```

打开 [素材库浏览页](../reference-library/actions/index.html) 查看结果。默认输出为项目根目录的 `reference-library/actions/`，与 `video-actions/` 工具代码分开长期保存。提取媒体沿用 Git 忽略策略；虚拟环境和 Python 缓存也不提交。源视频保持不变，已有旧参考技能可以继续独立使用。

## 输出

```text
reference-library/actions/
  index.html                        # 本地可打开，原速预览和摘要
  index.json                        # 每个源视频的最新运行结果
  <视频名>-<内容与参数指纹>/
    manifest.json                   # 源视频、SHA256、参数、候选列表、排除区间
    analysis.json                   # 逐采样帧运动值、切镜原因与空白标记
    timeline.jpg                    # 完整源视频时间线，包含被过滤的区域
    action-01/
      frames/0001.jpg ...            # 时间连续、最多 12 帧/秒的源帧采样
      summary/01.jpg ... 08.jpg      # 保留首尾，按源帧顺序均匀选择
      contact-sheet.jpg
      preview.mp4                   # 从原视频对应区间制作，不用摘要帧拼动画
      manifest.json                 # 起止、帧对应关系、可见性提示及其时间区间
```

时间戳以第一个解码视频帧为零点；区间为 `[start, end)`。JPG 采样直接选择源帧，保留变帧率视频的实际时间间隔，不补帧、不插值、不用重复图片凑满 8 帧。默认 JPG 最宽 960 像素、不放大。MP4 预览最宽 640 像素，以 24 fps 保持原始时间进度；变帧率间隙和结尾用邻近画面保持显示，不生成新姿势。保留原始背景、服装和水印。

## 切分方式与边界

程序用低分辨率帧差、颜色分布和局部变化峰值检测镜头切换，同时比较画面外围的布局突变，补充检测相似配色或镜像切镜；同一镜头内优先按持续停顿切分，过长连续运动按时长切分并提示检查。默认候选长 0.8–5 秒，近乎静止、采样不足和过短片段不输出为动作。连续采样默认最多 12 帧/秒，8 帧摘要供快速阅读；更细的动作仍可从连续帧和原速预览中查看。

末尾稳定暗色卡片及其淡入会按启发式排除，回溯时补偿整体亮度变化，避免把渐亮卡片当成动作；它不识别平台文字，可能误判暗色静止结尾。镜头边界也不等于语义动作边界，镜头移动、渐变转场及没有停顿的多个动作仍可能需要人工指定区间。所有结果标记为 `unreviewed_candidate`，不会自动命名动作或声称已通过视觉验收。

## 参考适用性提示

默认从保留的候选中均匀选取最多 24 个低分辨率样本，估计中央区域相对背景更突出的颜色，再跟踪最大相连色块。整段取样避免只由开头的道具决定跟踪颜色；根据参考色饱和度排除较淡的同色背景。这个步骤不调用模型，不修改或删除候选。

- 色块持续减少或消失：提示可能离场、遮挡或跟踪失败，并记录时间区间。
- 色块持续接触画面边缘：提示可能出框或近景裁切，并记录时间区间。
- 色块面积大幅变化：提示可能靠近、远离、遮挡或缩放，后续生成需固定角色尺度。

提示出现在浏览页和动作清单的 `reference_quality` 中。躲藏等有意遮挡仍保留完整候选。`no_issue_detected` 只代表未触发这些规则，不能视为动作通过验收；无法确定参考色时标记 `unassessed`。

这是颜色启发式，不能可靠识别角色身份、脸部朝向或服装。灰白角色、多个角色、相似颜色背景、大面积道具及照明变化都可能造成漏提示或误提示。可以明确指定角色颜色，也可关闭提示；遮挡、运动模糊、背景和水印仍需后续技能结合角色参考理解。

同一视频、内容和参数再次运行会复用已有结果；内容或参数改变会产生新目录，旧目录保留。失败视频会记录错误，其他视频继续处理；只要有失败，进程返回非零退出码。没有合格动作的有效视频是正常结果。

## 调整

```powershell
# 同一视频指定多个区间，覆盖自动切分；较复杂动作可手动取更长区间
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video/b31bb762450eb79cbf2138af5e10faf4.mp4 --range 0:3 --range 5:8.5 --out-dir reference-library/actions/manual

# 更密集采样，允许更长的连续动作候选
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video --sample-fps 24 --max-duration 8

# 自动颜色选错时，指定角色的代表颜色；PowerShell 中需要引号
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video --subject-color '#f2991f'

# 关闭颜色跟踪提示
.\video-actions\.venv\Scripts\python.exe video-actions/extract.py video --subject-color off
```

其他选项：`--min-duration`、`--min-motion`（降低可保留更细微动作）、`--scene-threshold`（降低使切镜更敏感）、`--frame-width`、`--summary-frames`、`--keep-end-card`、`--ffmpeg`。关闭片尾过滤后，静止段筛选仍然生效。`--range` 只接受一个源视频，可重复提供；跨镜头区间会带提示。完整参数见 `--help`。

## 验证

```powershell
.\video-actions\.venv\Scripts\python.exe -m unittest discover -s video-actions -p "test_*.py" -v
```

测试包含镜头边界、镜像切镜、快速中央动作、停顿切分、静止视频、渐亮片尾、长动作、摘要顺序、离场与遮挡提示、参考色误选回归，以及真实小视频的变帧率时间戳、预览解码、手动区间、缓存和失败隔离检查。全部 8 个项目视频的前后对照与限制见 [EXPERIMENTS.md](EXPERIMENTS.md)。

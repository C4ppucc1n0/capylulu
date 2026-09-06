# 参考素材库

项目内长期保存的参考素材，独立于 `video-actions/` 工具代码。

[浏览动作素材](actions/index.html)

`actions/` 保存视频提取结果：连续采样帧、八帧摘要、联系图、原速预览、来源清单和历史实验记录。现有结果已从 `video-actions/output/` 整体迁入，视频分组名称与组内相对路径保留。历史检查报告中的绝对路径记录的是当时位置；当前浏览入口以上面的链接为准。

在项目根目录运行 `video-actions/extract.py video` 时，默认输出到这里的 `actions/`。相同输入和参数复用已有结果，参数变化保留旧版本；`--out-dir` 可以指定其他目录。

提取媒体沿用迁移前的 Git 忽略策略，在本机长期保留，不作为临时工作目录清理；不会打包进桌宠 EXE。提取与迁移本身不生成固定角色参考、角色约束或动作标签；语义索引通过下述技能单独建立。

需要语义建库时，使用 [reference-library-semantics](../.agents/skills/reference-library-semantics/SKILL.md)，从实际图像生成动作模式、形态与视角描述，增量保存到 `semantics/index.jsonl`；首次运行才创建索引。该目录与被忽略的 `actions/` 媒体分开，语义记录可随项目保存。

[pet-action-atlas](../.agents/skills/pet-action-atlas/SKILL.md) 先固定角色图和身体约束，再按需读取语义记录选择参考。动作没有命中时仍保留角色依据，自由设计相容动作；形态证据可按视角或身体部位独立查找。

当前语义索引包含 3 条按需标注的素材记录，并未覆盖全部 38 个动作候选。新检出仓库需要先按 [工具说明](../video-actions/README.md) 恢复提取媒体，才能查看对应证据；语义记录通过源哈希与时间区间关联素材，目录变化时重新定位并检查图片指纹。历史实验图仅在保留了实验文件的本机可用。完整流程与验证范围见 [生成流程文档](../docs/pet-generation-pipeline.md)。

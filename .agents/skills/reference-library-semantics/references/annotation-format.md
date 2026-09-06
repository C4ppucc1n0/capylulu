# 语义记录格式

默认位置为项目根目录的 `reference-library/semantics/index.jsonl`，UTF-8，每行一个 JSON 对象。使用标准 JSON 序列化，不手工拼接转义字符。目录不存在时按需创建；提取媒体与其索引保持独立。

## 来源与缓存

程序从当前动作库读取以下信息，生成每条记录的元数据：

| 字段 | 规则 |
| --- | --- |
| `schema_version` | 当前为 `1` |
| `clip_id` | 对 `source_sha256:start_seconds:end_seconds` 的 UTF-8 文本取完整 SHA256；起止秒数统一保留六位小数 |
| `source_sha256` | 视频分组清单中的 `fingerprint.sha256` |
| `start_seconds` / `end_seconds` | 动作清单中的源时间区间，数值类型，起点包含、终点不包含 |
| `manifest` | 当前动作清单相对于项目根目录的路径，使用 `/` |
| `input_fingerprint` | 对 schema 版本和本片段全部摘要的 `[源时间戳, 图片 SHA256]` 有序列表做稳定 JSON 序列化后取 SHA256；不含目录名或修改时间 |
| `evidence` | 实际查看的图片列表；每项含项目相对 `file`、图片 `sha256`、`source_time_seconds`。可包括摘要或连续帧，必须属于本片段；无需重复保存图片 |
| `annotation` | 含 `producer`（实际模型标识；不可获得时为 `unknown`）、`prompt_version`（当前 `1`）、`created_at`（UTC ISO 8601）、`review_status`（`model_annotated` 或 `needs_review`）、`locked`（默认 `false`） |

指纹的稳定 JSON 约定：`{"schema_version":1,"summary":[[时间戳字符串,图片哈希],...]}`，时间戳保留六位小数，UTF-8，键排序，无额外空格。证据列表按源时间排序。

`clip_id` 与目录迁移、动作编号无关。复用时还需确认摘要输入指纹相同、实际证据图片内容未变化。仅路径改变时重新定位来源与证据，不重做语义；模型名称变化本身不要求全库重标。格式或标注规则升级按本次请求处理，不静默覆盖锁定条目。

同一源视频改变切分时，新的时间区间产生新键，旧键保留并设置 `availability: "stale"`；当前有效条目为 `"current"`。只按完整的当前源索引判断过期，不能因为某条目不在本次标注子集中就将其标为过期。

## 模型填写的内容

各对象与数组始终保留。无法确认的标量填 `null`，不能确认身份时 `character_id` 为 `null`；空数组只表示没有已确认条目，相关未知原因写入 `uncertainties`。下面是字段形状，描述文本只是示例，不能复制到未经观察的素材。

```json
{
  "character_id": null,
  "action": {
    "description": "角色从柱子后侧倾探头，再收回",
    "patterns": ["侧倾", "探头", "收回"],
    "start_pose": null,
    "end_pose": null,
    "tempo": null,
    "amplitude": null
  },
  "appearance": {
    "description": "本片段中实际看清的外观描述",
    "views": ["正面", "侧面"],
    "visible_parts": ["头部", "上半身"],
    "outfit": []
  },
  "constraints": {
    "props": ["柱子作为遮挡物"],
    "hand_use": null,
    "body_visibility": "下半身部分被遮挡",
    "displacement": null
  },
  "suggestions": {
    "mood_tags": ["调皮"],
    "adaptation": "可考虑提炼为侧倾探头；无柱子时需重新设计收势"
  },
  "uncertainties": ["摘要不足以判断手部状态"]
}
```

`action` 与 `appearance` 是独立的检索入口；目标动作没有命中不影响按视角、身体部位查找证据。`suggestions` 是解释或改编设想，不作为视觉事实和硬筛选条件。手部的左右必须说明是角色自身还是画面方向，无法判断时保留未知。只有实际看过连续动作且能判断时才补充起止姿态、节奏等细节。

## 合并与检查

- 以 `clip_id` 合并，未变化的条目复用，保留本次范围外及 `locked: true` 的记录。人工认可后可将状态改为 `human_reviewed` 并锁定；模型不能自行声称人工已确认。
- 元数据、证据和模型内容组成同一条记录，`availability` 与 `annotation.review_status` 分别表示来源是否仍当前、语义是否需复核。失败任务单独记入本次报告，不用空描述覆盖有效旧记录。
- 校验来源哈希、起止范围、图片哈希和证据清单一致；路径限定在指定素材库内。缺失媒体标为待处理，不能将无法验证的记录当成当前可用结果。
- 确认 JSON 可解析、字段类型正确、键唯一后，使用同目录临时文件替换索引。若索引在标注期间已变化，重新读取并合并，保留他人的更新。
- 召回时只使用当前可用条目，并保留需要复核的提示。未知道具或手部状态不能等同“无道具”或“双手空闲”；语义标注通过不等于素材可以直接生成或循环播放。

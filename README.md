# Procedural Lunar Crater Terrains

本仓库提供两个可导入现有 Unity 月球基础工程的程序化撞击坑地形模块。生成器仅通过菜单手动运行，不会在导入时自动覆盖场景。

## 地形内容

### Apollo16 高地及程序化生成退化撞击坑

- 模块：`Assets/Apollo16Highlands1`
- 场景：`Assets/Apollo16Highlands1/Scenes/Apollo16_Highlands.unity`
- 生成器：`Assets/Apollo16Highlands1/Editor/Apollo15HighlandsHybridBuilder.cs`
- 49 个合成撞击坑：21 个避障尺度坑、28 个浅小背景坑
- 宏观地形来源于项目中的 Apollo 16 DTM 工作流；坑的位置和形态为确定性程序化生成

文件夹和生成器中的部分 Apollo15 历史命名为保持 Unity GUID、路径和兼容性而保留，场景实际标注为 Apollo16 地形。

### Apollo15 高地及程序化生成退化深坑以及双坑

- 模块：`Assets/Apollo15HighlandsReal`
- 场景：`Assets/Apollo15HighlandsReal/Scenes/Apollo15_Highlands_Deep.unity`
- 生成器：`Assets/Apollo15HighlandsReal/Editor/Apollo15HighlandsDeepBuilder.cs`
- 41 个合成撞击坑：19 个避障尺度坑、22 个浅小背景坑
- 包含 3 个较深新鲜坑、1 组中等保存重叠坑、1 组退化双坑
- 深坑直径约 26–31 m、深度约 4.2–4.9 m；这是避障训练设计，不是 Apollo 15 当地实测比例

详细参数和每个坑的位置、直径、深度、保存状态见各模块 `Data` 目录中的 JSON 报告。

## 导入方法

1. 使用 Unity `6000.0.58f2` 打开与原作者相同的基础工程。
2. 将仓库中的两个文件夹原样复制到基础工程的 `Assets` 目录。
3. 必须保留所有 `.meta` 文件。
4. 在 Unity 中打开上面列出的场景。

这些场景会引用基础工程中的公共材质、脚本、岩石资源和 MuJoCo 包，因此本仓库不是可独立打开的完整 Unity 工程。

## 重新生成

生成代码位于各模块的 `Editor` 目录。导入后通过 Unity 菜单 `Tools > Lunar Terrain` 手动执行。重新生成前请保存当前场景，并确保生成器中指定的基础地形和源场景存在。

## 数据边界

- TerrainData、可视网格和场景是已生成的 Unity 资产。
- 新增撞击坑的位置与微观形态为合成结果，并非真实坑位复原。
- `Apollo15_Highlands` 是项目标签；其 300 m 基础地形来自项目中的 Apollo 15 着陆区 DTM 裁剪，并未单独验证为纯高地区域。

# SB-11A：GLB 资产映射与实例化设计

## 边界

SB-11A 为 SB-09 的 `CadStaticFacilityObject`、`CadDynamicEquipmentObject` 增加项目级的显式 GLB 资产映射。它不改变 Domain、SceneDraft 或 JobReport v0，不重新解析 CAD 或重新执行分类，也不实现分区、LOD 或 3D Tiles。

Catalog 的 `AssetId` 与根目录内的相对 GLB 路径分离；Binding 明确将语义对象 ID 或 Block 模式映射到 AssetId。禁止由 BlockName、Layer 或文件名猜测资产路径。Block 仅作为显式 Binding 的匹配条件，不会写入公开诊断、Manifest 节点名或结果。

## 契约与解析

Application 定义版本为 `1.0` 的 `CadAssetCatalog`、`CadAssetBindingSet`、`CadAssetDefinition`、`CadAssetBinding` 和 `CadAssetBindingSelector`。JSON loader 拒绝未知字段、缺失字段、错误类型、重复 ID、未知 AssetId、类别不一致和不安全的相对路径。类别固定为 `static-facility`、`dynamic-equipment`。

匹配排序固定为：语义对象 ID 精确匹配 300、Block 精确匹配 200、Block 通配符匹配 100；同 rank 取最大 priority。不同 AssetId 的并列组是冲突，不选择资产；相同 AssetId 的并列组按 Binding ID Ordinal 选择，并记录非敏感重复匹配诊断。一个 Binding 同时设置两个 Selector 时必须同时命中。

## 文件安全与暂存

Blender Adapter 接收调用方明确提供的 AssetRootDirectory。资产路径必须是 `.glb` 相对路径；拒绝绝对路径、UNC、URI、空段和 `..`。解析后确认规范化完整路径仍在根目录内，并拒绝根、目录或任何路径段上的 reparse point（包括符号链接和 junction）。只读取通过该边界的文件。

每个实际使用的 AssetId 只预校验和复制一次。源 GLB 由现有 `BinaryGlbValidator` 只读校验；通过后复制到 Blender 工作目录的 `assets/<ordinal>-<assetId>.glb`，Manifest 仅保存该工作区安全相对路径。源资产和 SceneDraft 都不修改。

## Windows 句柄级资产安全边界

早期“检查完整路径的目录属性，再按字符串路径打开或复制”存在 TOCTOU：检查时目录安全，不等于随后目录段没有被替换为 Junction 或符号链接。重复 `GetFullPath`、`GetAttributes` 或缩短时间窗口都不能消除外部进程的替换竞争。

Windows 资产读取改为 Fail Closed 的句柄链：根目录先以 `CreateFileW` 的 `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT` 打开；之后每个路径段均通过 `NtCreateFile` 和上一层目录句柄的 `OBJECT_ATTRIBUTES.RootDirectory` 相对打开。每段均采用 `FILE_OPEN_REPARSE_POINT`，并从句柄查询 `FileAttributeTagInfo`，任何 Reparse Point 都以 `ASSET_REPARSE_POINT_REJECTED` 拒绝。目录句柄与最终文件句柄均不共享删除权限，因此读取期间不能被按路径替换。

最终 `SafeFileHandle` 的所有权转移给 `SecureAssetFile`；父目录句柄在遍历结束时释放。Validator 通过该同一文件句柄创建的可定位流校验，并恢复流位置；Stager 从同一流复制到受控工作区临时文件，校验临时副本后才原子发布匿名 GLB。资产安全链不会以源路径重新打开、复制或读取。非 Windows 返回 `ASSET_SECURE_OPEN_UNSUPPORTED`，不提供不安全回退。

测试包括句柄持有时的重命名拒绝、原生 API 替身的重解析点拒绝、流验证的所有权/位置保持和资产暂存回归；真实 Blender SmokeTest 继续验证端到端导入。

## Manifest 与 Blender

Manifest 升级为内部 `2.0`，保留 SB-10 的程序化对象，并增加资产实例：稳定实例 ID、资产工作区相对路径、类别和 SceneDraft Transform。Python 只接收 Manifest 引用的工作区相对路径；不扫描资产目录。它使用 `bpy.ops.import_scene.gltf`，追踪新增对象，创建稳定 Empty 父节点，应用位置/绕 Z 轴旋转/缩放，并将静态、动态资产放入不同 Collection。缺失映射、缺文件或无效 GLB 按 `Skip`、`Placeholder`、`Fail` 策略处理；Placeholder 为明确标记的简单几何，不伪装成导入资产。

## 验证

测试覆盖严格 JSON、通配符、冲突、类别、路径穿越/reparse point、源 GLB 校验、暂存去重、Manifest 脱敏与确定性、Fake Blender 端到端。SmokeTest 使用 .NET 8 生成临时合成资产，调用真实 Adapter/Blender，并以真实 `BinaryGlbValidator` 校验最终 GLB。它只输出版本、计数与状态，不输出资产根、Block 或 Manifest。

# CORE-02 实施计划与执行记录

1. 已先完成 Analyze 设计，确认只复用当前 DXF 解析、几何、轮廓、修复和规则能力。
2. 已先新增 Handler 红测；首次命令为 `dotnet test tests/SceneBuilder.Application.Tests/SceneBuilder.Application.Tests.csproj --no-restore --filter FullyQualifiedName~CadImportAnalysisHandlerTests`，失败原因是 CORE-02 的 request 与 Host handler 尚不存在。
3. 已最小化加入 Application 中立 adapter 契约、Cad DXF/DWG adapters、受控输入和稳定 Artifact，再将该目标测试转绿。
4. 已接入 Composition capability 和 CLI；最终以全量 build/test、CLI 和字节确定性检查验证。

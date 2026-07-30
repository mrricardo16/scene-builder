# DESKTOP-08：3D Tiles 预览

## 目标

在 DESKTOP-00 3D Tiles Viewer POC 成功后，预览当前 Build Job 中已验证的本地 Cartesian 3D Tiles 1.1。大厂区表现、内存和长时间浏览验收由 LARGE-04 单独负责。

## 范围与退出

仅加载已验证 Scene Package 内相对路径的 `tileset.json` 与白名单内容；显示加载失败和受控回退。不得把此任务扩展为 Cesium/IDTS 集成、地理配准、任意 URL 浏览或声称任何远端发布能力。坐标、资源释放、打包后的 Viewer 行为和回退结论可验证即退出。

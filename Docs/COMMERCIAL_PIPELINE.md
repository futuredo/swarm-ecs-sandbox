# 商业化管线：YooAsset + HybridCLR

本工程把资源与代码更新管线隔离为独立边界，核心定点数仿真不依赖资源系统或热更新运行时。这样可以保持集成关系清晰，并避免算法验证受到打包工具状态影响。

## 已固定的官方版本

- [YooAsset 3.0.4](https://github.com/tuyoogame/YooAsset/tree/3.0.4)
- [HybridCLR 8.12.0](https://github.com/focus-creative-games/hybridclr_unity/tree/v8.12.0)

版本通过 `Packages/manifest.json` 的 Git tag 固定，避免团队成员在不同时间拉到不兼容的 `main`。

## 工程里已经接通的部分

- `SwarmECS.HotUpdate.asmdef`：独立热更程序集，不被 AOT Runtime 反向引用。
- `HotUpdateEntry`：可替换的 ORCA 参数策略示例。
- `SwarmAssetService`：YooAsset 3.x 包初始化、引用计数句柄和原始 DLL 字节读取边界。
- `HybridClrAssemblyLoader`：补充 AOT 元数据与 `Assembly.Load(byte[])` 的统一入口。
- `CommercialPipelineSetup`：一键登记 HybridCLR 热更程序集、AOT 元数据程序集和 YooAsset Collector。
- `link.xml`：保护 AOT 侧的加载入口，防止 IL2CPP stripping 删除反射调用目标。

## 首次平台初始化

1. 在 Unity 菜单执行 `Swarm ECS > Commercial Pipeline > Configure YooAsset + HybridCLR`。
2. 在 HybridCLR 官方菜单执行 `HybridCLR > Installer...`。这一步会下载与当前 Unity 版本匹配的本地 `libil2cpp`，不应在 CI 中无提示修改。
3. 切换到目标平台并执行 `HybridCLR > Generate > All`。
4. 将 `HybridCLRData/HotUpdateDlls/<BuildTarget>/SwarmECS.HotUpdate.dll` 复制到 `Assets/HotUpdateAssets/`。
5. 用 YooAsset 的 `SwarmAssets` 包构建资源清单和 AssetBundle；该包已经配置 `HotUpdateAndData` Collector。
6. Player 启动后，先通过 `SwarmAssetService` 取 DLL 字节，再通过 `HybridClrAssemblyLoader` 加载补充元数据和热更程序集。

## 发布流水线建议

```text
编译 AOT Player
  → HybridCLR Generate/All
  → 编译 SwarmECS.HotUpdate.dll
  → 复制 DLL/PDB/AOT metadata 到 Assets/HotUpdateAssets
  → YooAsset 构建 SwarmAssets
  → 上传版本清单与 bundles 到 CDN
  → 生成可回滚的版本元数据
```

仓库不会提交 `HybridCLRData/LocalIl2CppData-*`、平台构建产物或 CDN 包；这些应由受控构建机生成。仿真沙盒在没有下载任何更新包时也能独立运行。

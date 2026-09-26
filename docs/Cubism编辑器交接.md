# Cubism Editor 素材交接

项目已经备好独立图层：刘海、面部、开眼、闭眼、身体、两只手、裙摆、两条腿和后发。`tools/export_cubism_psd.py` 将 `app/models/whale-rig/model.json` 对应的贴图组成 1200×1200、13 图层的 `dist/Whale-Cubism-Layers.psd`。这份 PSD 是**待绑定的绘画素材**，并非可播放的 Cubism 模型。原图不会被脚本改写。

在仓库根目录运行（需要 Python 3 和 Pillow）：

```powershell
python tools/export_cubism_psd.py
```

脚本会重新打开 PSD，核对合成预览、图层数量、名称和每层像素。当前已通过这项校验。由于还没有在 Cubism Editor 实际导入，此处不能保证每个部件已经适合最终的参数变形；导入后应检查接缝与遮挡。

## 编辑器中接着完成

1. 在官方 Cubism Editor 的建模工作区打开这份 PSD。官方文档说明，PSD 图层会导入为初始 ArtMesh。
2. 检查图层顺序、透明度、眼睛位置和袖口遮挡；闭眼图层初始隐藏。特别检查刘海与左侧脸部、头发与手臂交叠的位置。
3. 为头部、前后发、躯干、手臂、裙摆和腿建立合适的变形器与关键形状；为眼睛设置开闭参数，补足脸部转动时被遮住的绘画区域。
4. 预览眨眼、呼吸、转头和点击回应，再在编辑器中导出 `.moc3`、`.model3.json`、纹理和动作文件。

官方说明：[导入 PSD](https://docs.live2d.com/en/cubism-editor-manual/psd-import/) · [素材分层](https://docs.live2d.com/en/cubism-editor-manual/divide-the-material/) · [导出模型](https://docs.live2d.com/en/cubism-editor-manual/export-moc3-motion3-files/)。

桌宠程序目前只加载自有的 `whale-layered-1`；导出真正的 Cubism 文件后，还要把获得授权的 Cubism 运行时接入 Windows 程序，才能在桌面直接使用 `.model3.json`。不要把 PSD 改名为 `.moc3`，也不要把官方 SDK Core 二进制直接加入本仓库。

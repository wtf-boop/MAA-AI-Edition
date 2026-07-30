# MAA 原生 AI 助手集成版

本项目是 [MaaAssistantArknights/MaaAssistantArknights](https://github.com/MaaAssistantArknights/MaaAssistantArknights)
`dev-v2` 分支的 Fork 与 AI 增强版本。原版 MAA 的代码、品牌与既有功能归其原作者及贡献者所有；
本 Fork 仅维护这里公开的 AI 集成改动，不冒充官方版本。

本版本直接修改 `src/MaaWpfGui`，AI 助手作为原版 MAA 主导航页面运行，最终程序集名称仍为 `MAA.exe`。

## 已集成功能

- 原生 WPF「AI 助手」页面
- OpenAI-compatible API（Ollama、LM Studio及兼容云接口）
- 扫描 MAA `resource` 目录建立本地知识库
- 导入 `config/ai-knowledge` 下的 `.txt`、`.md`、`.json`
- 本地 RAG 检索后再向模型提问
- 模型地址、模型名与 API Key 持久化

## Windows 编译

安装 Visual Studio 2022/2026、C++ 桌面开发、.NET 10 SDK、Git/CMake，然后按照官方 dev-v2 构建环境编译。也可在仓库根目录运行 `build-ai-maa-windows.bat` 尝试发布 GUI。

本修改遵循仓库 AGPL-3.0 许可证。发布二进制时必须同时提供对应修改源码并保留许可证。

# 第三方组件

本文件说明第三方组件的许可，不为本项目自身另行指定许可证。

| 组件 | 用途 | 许可 / 来源 |
| --- | --- | --- |
| Microsoft.Data.Sqlite 10.0.0 | SQLite 数据访问 | MIT，[dotnet/efcore](https://github.com/dotnet/efcore) |
| SQLitePCLRaw 3.0.5 | SQLite 平台适配 | Apache-2.0，[ericsink/SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) |
| SQLite 3.53.4 | 嵌入的 x86 原生数据库库 | [SQLite public domain](https://www.sqlite.org/copyright.html) |
| Costura.Fody 6.0.0 | 将依赖嵌入 EXE | MIT，[Fody/Costura](https://github.com/Fody/Costura) |
| Fody 6.9.3 | 构建时程序集处理 | MIT，[Fody/Fody](https://github.com/Fody/Fody) |
| System.Memory 及相关 .NET 支持程序集 | .NET Standard 兼容支持 | MIT，[dotnet/runtime](https://github.com/dotnet/runtime) |

SQLitePCLRaw 的 NuGet 元数据包含 `Copyright 2014-2025 SourceGear, LLC`；其配置包等元数据也可能标注 2026。具体组件保留原作者版权，Apache-2.0 许可全文随附于 `licenses/APACHE-2.0.txt`。

Microsoft、Costura 和 Fody 的 MIT 许可文本及版权说明放在 `licenses/` 目录。SQLite 本身的公有领域声明见上表链接。

仓库中的 `src/SeeYou/costura-win-x86/e_sqlite3.dll` 来自 NuGet 包 `SQLite 3.53.4` 的 `runtimes/win-x86/native`，为构建单文件 EXE 而随源码提供。其他 NuGet 依赖在构建时还原，产物通过 Costura 嵌入；各组件仍遵循原有许可。

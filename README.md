# Record

Record 是一个面向 Windows 的本地日常记录应用，第一版聚焦于日常开销记账。

当前仓库同时保留两条实现线：`record_main` 是早期 Python 原型，`Record.Desktop` 是正在开发的 WPF Windows 客户端。两者目前使用独立的本地数据文件，暂不自动同步。

## v0.1.0 第一版功能

- 手动记录收入/支出、日期、金额、类别、说明和支付方式
- 编辑、删除已有记录，并在删除前二次确认
- 导入常见的微信/支付宝 CSV 账单（兼容 UTF-8 与系统中文编码）
- 导入预览、重复记录检测和批量导入
- 按日期、类别、说明和支付方式筛选
- 查看总支出、总收入、本月支出和本月收入
- 按本月、近三月、今年查看收支趋势和支出类别分布
- 导出 UTF-8 CSV 作为本地备份
- 数据仅保存在本机，不上传网络

## 快速开始

### WPF Windows 客户端

在 Windows PowerShell 中执行：

```powershell
dotnet run --project .\Record.Desktop\Record.Desktop.csproj
```

WPF 客户端将记录保存到 Windows 的本地应用数据目录：

```text
%LOCALAPPDATA%\Record\records.json
```

当前版本号为 `0.1.0`。数据层使用仓储接口封装本地读写，后续可以替换为 SQLite，而不需要修改页面层。

### Python 原型

在 Windows PowerShell 中执行：

```powershell
python -m record_main
```

关闭程序后，数据仍会保存在本地数据库中。Python 原型建议定期复制 `record_main/record.db` 做备份。

## 账单导入说明

在微信或支付宝账单页面导出 CSV 文件，然后进入“导入”页选择文件。程序按列名识别日期、金额、收支、类别、商品/说明和支付方式；不能识别的字段会使用“其他”或“未命名记录”，不会修改原始文件。

导入文件后会先显示前 5 条预览记录，并检查与本地记录相同的账单。重复记录不会再次写入。

如果导出的是 Excel `.xlsx` 而不是 CSV，请先在 Excel/WPS 中另存为 CSV UTF-8。

## 项目结构与设计

`record_main/db.py` 负责 SQLite 数据表、金额处理、筛选统计和 CSV 导入；`record_main/app.py` 负责 Tkinter 界面。`Record.Desktop/Models/RecordEntry.cs` 定义 WPF 客户端记录模型，`Data/JsonRecordRepository.cs` 负责本地 JSON 持久化，`Import/CsvRecordImporter.cs` 负责账单读取。数据模型和仓储接口为后续增加其他记录类型预留了边界。

技术选型：WPF、C#、.NET 10、JSON 本地存储；早期原型使用 Python 3.10+、Tkinter、SQLite。金额使用 `decimal` 处理，避免二进制浮点误差。界面使用原创的卡通幻想配色与卡片布局，营造温暖手绘感，不包含第三方游戏素材。

## 开发流程

每个可用版本使用 Git 标签管理。第一版发布时可以执行：

```powershell
git init
git add .
git commit -m "feat: initial local ledger"
git tag -a v0.1.0 -m "release: Record v0.1.0"
git push origin main
git push origin v0.1.0
```

版本号采用 SemVer：`MAJOR.MINOR.PATCH`。提交建议使用 `feat`、`fix`、`docs` 等前缀，数据库文件已在 `.gitignore` 中排除。

## 数据位置与备份

WPF 客户端数据位置：`%LOCALAPPDATA%\Record\records.json`。推荐优先使用应用“导入”页右上角的“导出备份”，生成 CSV 文件后保存到其他磁盘或云盘。CSV 备份不包含应用设置，只包含账单记录。

## 第一版边界

第一版暂不包含预算管理、账号同步、云端服务和 `.xlsx` 原生解析。Excel 账单请先另存为 CSV。后续开发可以在不改变现有记录模型的前提下增加这些能力。

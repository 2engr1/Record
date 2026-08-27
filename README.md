# Record

Record 是一个面向 Windows 的本地日常记录应用，当前版本聚焦于日常开销记账。

## 当前功能

- 手动记录收入/支出、日期、金额、类别、说明和支付方式
- 导入常见的微信/支付宝 CSV 账单（兼容 UTF-8 与 GB18030）
- 按日期、类别、关键词筛选
- 查看支出、收入、结余汇总
- 按类别统计支出
- 数据保存在 `record_main/record.db`，不上传网络

## 快速开始

在 Windows PowerShell 中执行：

```powershell
python -m record_main
```

关闭程序后，数据仍会保存在本地数据库中。建议定期复制 `record_main/record.db` 做备份。

## 账单导入说明

在微信或支付宝账单页面导出 CSV 文件，然后点击“导入账单 CSV”。程序按列名识别日期、金额、收支、类别、商品/说明和支付方式；不能识别的字段会留空，不会修改原始文件。

如果导出的是 Excel `.xlsx` 而不是 CSV，请先在 Excel/WPS 中另存为 CSV UTF-8。

## 项目结构与设计

`record_main/db.py` 负责 SQLite 数据表、金额处理、筛选统计和 CSV 导入；`record_main/app.py` 负责 Tkinter 界面。数据模型保留了 `source` 和 `direction` 等字段，后续可在不破坏记账数据的情况下增加其他记录类型。

技术选型：Python 3.10+、Tkinter、SQLite，均为轻量本地能力；金额以“分”的整数保存，避免浮点误差。界面使用原创的卡通幻想配色与卡片布局，营造温暖手绘感，不包含第三方游戏素材。

## 开发流程

每个可用版本使用 Git 标签管理，例如：

```powershell
git init
git add .
git commit -m "feat: initial local ledger"
git tag v0.1.0
```

版本号采用 SemVer：`MAJOR.MINOR.PATCH`。提交建议使用 `feat`、`fix`、`docs` 等前缀，数据库文件已在 `.gitignore` 中排除。


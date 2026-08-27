from __future__ import annotations

import csv
import sqlite3
from dataclasses import dataclass
from datetime import date
from pathlib import Path


@dataclass
class Entry:
    happened_on: str
    amount_cents: int
    category: str
    note: str = ""
    payment_method: str = ""
    source: str = "手动"
    direction: str = "支出"


class Ledger:
    def __init__(self, path: Path | None = None):
        self.path = path or Path(__file__).with_name("record.db")
        self.connection = sqlite3.connect(self.path)
        self.connection.row_factory = sqlite3.Row
        self.connection.execute("""
            CREATE TABLE IF NOT EXISTS entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                happened_on TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                category TEXT NOT NULL,
                note TEXT NOT NULL DEFAULT '',
                payment_method TEXT NOT NULL DEFAULT '',
                source TEXT NOT NULL DEFAULT '手动',
                direction TEXT NOT NULL DEFAULT '支出',
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            )
        """)
        self.connection.commit()

    def add(self, entry: Entry) -> None:
        self.connection.execute(
            "INSERT INTO entries (happened_on, amount_cents, category, note, payment_method, source, direction) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (entry.happened_on, entry.amount_cents, entry.category, entry.note,
             entry.payment_method, entry.source, entry.direction),
        )
        self.connection.commit()

    def add_many(self, entries: list[Entry]) -> None:
        self.connection.executemany(
            "INSERT INTO entries (happened_on, amount_cents, category, note, payment_method, source, direction) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            [(e.happened_on, e.amount_cents, e.category, e.note, e.payment_method, e.source, e.direction)
             for e in entries],
        )
        self.connection.commit()

    def delete(self, entry_id: int) -> None:
        self.connection.execute("DELETE FROM entries WHERE id = ?", (entry_id,))
        self.connection.commit()

    def list(self, start: str = "", end: str = "", category: str = "全部", keyword: str = "") -> list[sqlite3.Row]:
        sql = "SELECT * FROM entries WHERE 1=1"
        args: list[str] = []
        if start:
            sql += " AND happened_on >= ?"; args.append(start)
        if end:
            sql += " AND happened_on <= ?"; args.append(end)
        if category and category != "全部":
            sql += " AND category = ?"; args.append(category)
        if keyword:
            sql += " AND (note LIKE ? OR payment_method LIKE ? OR source LIKE ?)"
            args.extend([f"%{keyword}%"] * 3)
        return self.connection.execute(sql + " ORDER BY happened_on DESC, id DESC", args).fetchall()

    def categories(self) -> list[str]:
        rows = self.connection.execute("SELECT DISTINCT category FROM entries ORDER BY category").fetchall()
        return [row[0] for row in rows]

    def totals(self, start: str = "", end: str = "") -> tuple[int, int]:
        rows = self.list(start, end)
        expense = sum(row["amount_cents"] for row in rows if row["direction"] == "支出")
        income = sum(row["amount_cents"] for row in rows if row["direction"] == "收入")
        return expense, income

    def category_totals(self, start: str = "", end: str = "") -> list[tuple[str, int]]:
        rows = self.connection.execute(
            "SELECT category, SUM(amount_cents) amount FROM entries "
            "WHERE direction = '支出' AND (? = '' OR happened_on >= ?) AND (? = '' OR happened_on <= ?) "
            "GROUP BY category ORDER BY amount DESC", (start, start, end, end)).fetchall()
        return [(row["category"], row["amount"]) for row in rows]

    def close(self) -> None:
        self.connection.close()


def _pick(row: dict[str, str], *names: str) -> str:
    for name in names:
        if name in row and row[name].strip():
            return row[name].strip()
    return ""


def _date(value: str) -> str:
    value = value.strip().replace("/", "-").replace("年", "-").replace("月", "-").replace("日", "")
    parts = value.split(" ")[0].split("-")
    if len(parts) == 3:
        return f"{int(parts[0]):04d}-{int(parts[1]):02d}-{int(parts[2]):02d}"
    raise ValueError(f"无法识别日期：{value}")


def _cents(value: str) -> int:
    value = value.replace(",", "").replace("¥", "").replace("￥", "").strip()
    return round(abs(float(value)) * 100)


def import_csv(path: Path, source: str) -> list[Entry]:
    """导入微信/支付宝常见导出格式；无法识别的行会抛出明确错误。"""
    last_error = None
    for encoding in ("utf-8-sig", "gb18030"):
        try:
            with path.open("r", encoding=encoding, newline="") as file:
                sample = file.read(4096); file.seek(0)
                try:
                    dialect = csv.Sniffer().sniff(sample, delimiters=",\t")
                except csv.Error:
                    dialect = csv.excel_tab if "\t" in sample and sample.count("\t") > sample.count(",") else csv.excel
                raw_rows = list(csv.reader(file, dialect=dialect))
                header_index = next((index for index, row in enumerate(raw_rows)
                                     if any(name in row for name in ("交易时间", "日期"))
                                     and any(name in row for name in ("金额(元)", "金额（元）", "金额", "交易金额"))), None)
                if header_index is None:
                    raise ValueError("没有找到账单表头（需要日期和金额列）。")
                headers = raw_rows[header_index]
                rows = (dict(zip(headers, values)) for values in raw_rows[header_index + 1:] if values)
                result = []
                for row in rows:
                    happened = _pick(row, "交易时间", "日期", "时间")
                    amount = _pick(row, "金额(元)", "金额（元）", "金额", "交易金额")
                    if not happened or not amount or "¥" in amount and amount.strip() == "¥":
                        continue
                    direction = _pick(row, "收/支", "收支", "类型") or "支出"
                    direction = "收入" if any(word in direction for word in ("收入", "转入", "退款")) else "支出"
                    result.append(Entry(
                        happened_on=_date(happened), amount_cents=_cents(amount), direction=direction,
                        category=_pick(row, "交易分类", "交易类型", "分类") or "未分类",
                        note=_pick(row, "商品说明", "商品", "交易对方", "备注"),
                        payment_method=_pick(row, "支付方式", "付款方式", "资金渠道"), source=source,
                    ))
                if not result:
                    raise ValueError("没有找到可导入的账单记录，请确认这是 CSV 格式的账单文件。")
                return result
        except UnicodeDecodeError as error:
            last_error = error
    raise ValueError(f"无法读取账单文件：{last_error}")

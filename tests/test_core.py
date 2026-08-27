import csv
import shutil
import unittest
from pathlib import Path

from record_main.db import Entry, Ledger, import_csv


class CoreTests(unittest.TestCase):
    def test_ledger_totals_and_filter(self):
        folder = Path(__file__).with_name(".tmp")
        folder.mkdir(exist_ok=True)
        try:
            ledger = Ledger(folder / "test.db")
            ledger.add_many([
                Entry("2026-08-01", 1250, "餐饮"),
                Entry("2026-08-02", 3000, "交通"),
                Entry("2026-08-02", 10000, "工资", direction="收入"),
            ])
            self.assertEqual(ledger.totals(), (4250, 10000))
            self.assertEqual(len(ledger.list(category="交通")), 1)
            ledger.close()
        finally:
            shutil.rmtree(folder, ignore_errors=True)

    def test_common_csv_import(self):
        folder = Path(__file__).with_name(".tmp")
        folder.mkdir(exist_ok=True)
        try:
            path = folder / "wechat.csv"
            with path.open("w", encoding="utf-8-sig", newline="") as file:
                file.write("支付宝交易明细\n导出时间：2026-08-27\n")
                writer = csv.DictWriter(file, fieldnames=["交易时间", "交易分类", "商品说明", "金额（元）", "收/支", "支付方式"])
                writer.writeheader()
                writer.writerow({"交易时间":"2026-08-27 12:00:00", "交易分类":"餐饮", "商品说明":"午餐", "金额（元）":"12.50", "收/支":"支出", "支付方式":"零钱"})
            entries = import_csv(path, "支付宝")
            self.assertEqual(entries[0].amount_cents, 1250)
            self.assertEqual(entries[0].happened_on, "2026-08-27")
        finally:
            shutil.rmtree(folder, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()

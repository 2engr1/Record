from __future__ import annotations

import tkinter as tk
from datetime import date
from decimal import Decimal, InvalidOperation
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from .db import Entry, Ledger, import_csv


BG = "#fff8ee"
PANEL = "#fffdf8"
INK = "#493b35"
MUTED = "#8b766b"
CORAL = "#e88770"
GOLD = "#efb552"
GREEN = "#75a989"


class RecordApp:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("Record · 日常记录簿")
        self.root.geometry("1100x720")
        self.root.minsize(900, 620)
        self.root.configure(bg=BG)
        self.ledger = Ledger()
        self._style()
        self._build()
        self.refresh()
        self.root.protocol("WM_DELETE_WINDOW", self._close)

    def _style(self):
        style = ttk.Style(self.root); style.theme_use("clam")
        style.configure("TFrame", background=BG); style.configure("Card.TFrame", background=PANEL)
        style.configure("TLabel", background=BG, foreground=INK, font=("Microsoft YaHei UI", 10))
        style.configure("Title.TLabel", font=("Microsoft YaHei UI", 24, "bold"), foreground=INK)
        style.configure("Sub.TLabel", foreground=MUTED)
        style.configure("Card.TLabel", background=PANEL, foreground=INK)
        style.configure("Treeview", rowheight=32, background=PANEL, fieldbackground=PANEL, foreground=INK, borderwidth=0)
        style.configure("Treeview.Heading", background="#f4e4cd", foreground=INK, font=("Microsoft YaHei UI", 10, "bold"))
        style.configure("Accent.TButton", background=CORAL, foreground="white", padding=(14, 8), borderwidth=0)
        style.map("Accent.TButton", background=[("active", "#d9715d")])
        style.configure("Soft.TButton", background="#f6eadb", foreground=INK, padding=(10, 7), borderwidth=0)
        style.configure("TNotebook", background=BG, borderwidth=0); style.configure("TNotebook.Tab", padding=(18, 9))

    def _build(self):
        header = ttk.Frame(self.root, padding=(28, 22, 28, 10)); header.pack(fill="x")
        ttk.Label(header, text="✦  Record", style="Title.TLabel").pack(side="left")
        ttk.Label(header, text="把日子里的小事，好好记下来", style="Sub.TLabel").pack(side="left", padx=18, pady=(10, 0))
        ttk.Button(header, text="＋ 记一笔", style="Accent.TButton", command=self.add_dialog).pack(side="right")

        self.tabs = ttk.Notebook(self.root); self.tabs.pack(fill="both", expand=True, padx=24, pady=(0, 24))
        self.ledger_tab = ttk.Frame(self.tabs, padding=18); self.stats_tab = ttk.Frame(self.tabs, padding=18)
        self.tabs.add(self.ledger_tab, text=" 账目簿 "); self.tabs.add(self.stats_tab, text=" 统计 ")
        self._build_ledger_tab(); self._build_stats_tab()

    def _build_ledger_tab(self):
        filters = ttk.Frame(self.ledger_tab, style="Card.TFrame", padding=14); filters.pack(fill="x", pady=(0, 14))
        self.start = tk.StringVar(); self.end = tk.StringVar(); self.category = tk.StringVar(value="全部"); self.keyword = tk.StringVar()
        self.category_box = None
        for label, variable, width in (("从", self.start, 12), ("至", self.end, 12), ("类别", self.category, 15), ("搜索", self.keyword, 18)):
            ttk.Label(filters, text=label, style="Card.TLabel").pack(side="left", padx=(0, 5))
            widget = ttk.Combobox(filters, textvariable=variable, width=width, state="readonly") if label == "类别" else ttk.Entry(filters, textvariable=variable, width=width)
            if label == "类别": self.category_box = widget
            widget.pack(side="left", padx=(0, 12))
        ttk.Button(filters, text="筛选", style="Soft.TButton", command=self.refresh).pack(side="left")
        ttk.Button(filters, text="导入账单 CSV", style="Soft.TButton", command=self.import_file).pack(side="right")
        ttk.Button(filters, text="删除选中", style="Soft.TButton", command=self.delete_selected).pack(side="right", padx=8)

        summary = ttk.Frame(self.ledger_tab); summary.pack(fill="x", pady=(0, 14))
        self.expense_text = tk.StringVar(); self.income_text = tk.StringVar(); self.balance_text = tk.StringVar()
        for title, variable, color in (("本期支出", self.expense_text, CORAL), ("本期收入", self.income_text, GREEN), ("结余", self.balance_text, GOLD)):
            card = ttk.Frame(summary, style="Card.TFrame", padding=(18, 12)); card.pack(side="left", fill="x", expand=True, padx=(0, 10))
            tk.Label(card, text=title, bg=PANEL, fg=MUTED, font=("Microsoft YaHei UI", 10)).pack(anchor="w")
            tk.Label(card, textvariable=variable, bg=PANEL, fg=color, font=("Microsoft YaHei UI", 18, "bold")).pack(anchor="w", pady=(3, 0))
        self.table = ttk.Treeview(self.ledger_tab, columns=("date", "direction", "amount", "category", "note", "method", "source"), show="headings")
        headings = {"date":"日期", "direction":"收支", "amount":"金额", "category":"类别", "note":"说明", "method":"支付方式", "source":"来源"}
        widths = {"date":105, "direction":65, "amount":105, "category":120, "note":260, "method":130, "source":90}
        for key, text in headings.items(): self.table.heading(key, text=text); self.table.column(key, width=widths[key], anchor="center" if key != "note" else "w")
        self.table.tag_configure("income", foreground=GREEN); self.table.tag_configure("expense", foreground=INK)
        self.table.pack(fill="both", expand=True)

    def _build_stats_tab(self):
        self.stats_title = tk.StringVar(value="支出分布")
        ttk.Label(self.stats_tab, textvariable=self.stats_title, style="Title.TLabel").pack(anchor="w", pady=(0, 14))
        self.stats_text = tk.Text(self.stats_tab, bg=PANEL, fg=INK, relief="flat", font=("Microsoft YaHei UI", 11), padx=20, pady=18)
        self.stats_text.pack(fill="both", expand=True); self.stats_text.configure(state="disabled")

    @staticmethod
    def _money(cents: int) -> str:
        return f"¥ {cents / 100:,.2f}"

    def refresh(self):
        categories = ["全部"] + self.ledger.categories()
        if hasattr(self, "category_box"): self.category_box["values"] = categories
        rows = self.ledger.list(self.start.get().strip(), self.end.get().strip(), self.category.get(), self.keyword.get().strip())
        for item in self.table.get_children(): self.table.delete(item)
        for row in rows:
            self.table.insert("", "end", iid=str(row["id"]), values=(row["happened_on"], row["direction"], self._money(row["amount_cents"]), row["category"], row["note"], row["payment_method"] or "—", row["source"]), tags=("income" if row["direction"] == "收入" else "expense",))
        expense, income = self.ledger.totals(self.start.get().strip(), self.end.get().strip())
        self.expense_text.set(self._money(expense)); self.income_text.set(self._money(income)); self.balance_text.set(self._money(income - expense))
        self._refresh_stats()

    def _refresh_stats(self):
        totals = self.ledger.category_totals(self.start.get().strip(), self.end.get().strip())
        total = sum(value for _, value in totals)
        lines = [f"筛选范围：{self.start.get() or '最早'}  至  {self.end.get() or '今天'}", "", f"支出合计：{self._money(total)}", ""]
        for category, value in totals:
            percent = value / total * 100 if total else 0
            bars = "▮" * max(1, round(percent / 5))
            lines.append(f"{category:<12} {self._money(value):>12}  {percent:5.1f}%  {bars}")
        self.stats_text.configure(state="normal"); self.stats_text.delete("1.0", "end"); self.stats_text.insert("1.0", "\n".join(lines) or "暂无数据"); self.stats_text.configure(state="disabled")

    def add_dialog(self):
        win = tk.Toplevel(self.root); win.title("记一笔"); win.geometry("420x410"); win.configure(bg=BG); win.transient(self.root); win.grab_set()
        vars = {key: tk.StringVar(value=value) for key, value in {"date": date.today().isoformat(), "amount":"", "category":"餐饮", "note":"", "method":"", "direction":"支出"}.items()}
        box = ttk.Frame(win, padding=26); box.pack(fill="both", expand=True)
        ttk.Label(box, text="记一笔日常开销", style="Title.TLabel").pack(anchor="w", pady=(0, 18))
        fields = [("日期（YYYY-MM-DD）", "date"), ("金额（元）", "amount"), ("类别", "category"), ("说明", "note"), ("支付方式", "method")]
        for label, key in fields:
            ttk.Label(box, text=label).pack(anchor="w", pady=(5, 2)); ttk.Entry(box, textvariable=vars[key]).pack(fill="x")
        ttk.Label(box, text="类型").pack(anchor="w", pady=(8, 2)); ttk.Combobox(box, textvariable=vars["direction"], values=("支出", "收入"), state="readonly").pack(fill="x")
        def save():
            try:
                amount = Decimal(vars["amount"].get().strip()); cents = int(amount * 100)
                if cents <= 0: raise ValueError
                entry_date = date.fromisoformat(vars["date"].get().strip()).isoformat()
            except (InvalidOperation, ValueError):
                messagebox.showerror("无法保存", "请填写正确的日期和大于 0 的金额。", parent=win); return
            self.ledger.add(Entry(entry_date, cents, vars["category"].get().strip() or "未分类", vars["note"].get().strip(), vars["method"].get().strip(), "手动", vars["direction"].get()))
            win.destroy(); self.refresh()
        ttk.Button(box, text="保存", style="Accent.TButton", command=save).pack(fill="x", pady=(20, 0))

    def import_file(self):
        filename = filedialog.askopenfilename(title="选择账单 CSV", filetypes=(("CSV 文件", "*.csv"), ("所有文件", "*.*")))
        if not filename: return
        source = "支付宝" if "支付宝" in Path(filename).name else "微信/CSV"
        try:
            entries = import_csv(Path(filename), source); self.ledger.add_many(entries); self.refresh()
            messagebox.showinfo("导入完成", f"已导入 {len(entries)} 条记录。")
        except (OSError, ValueError) as error: messagebox.showerror("导入失败", str(error))

    def delete_selected(self):
        selected = self.table.selection()
        if not selected: return
        if messagebox.askyesno("确认删除", f"确定删除选中的 {len(selected)} 条记录吗？"):
            for item in selected: self.ledger.delete(int(item))
            self.refresh()

    def _close(self):
        self.ledger.close(); self.root.destroy()

    def run(self): self.root.mainloop()

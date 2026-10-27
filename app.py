# -*- coding: utf-8 -*-
"""
app.py — «Планировщик служб Windows».

Один исполняемый файл (после сборки PyInstaller), запуск от администратора.
Современный интерфейс на русском языке (CustomTkinter). Управление службами
через задания стандартного Планировщика заданий Windows.

Режимы запуска:
  без аргументов                  — GUI
  --exec <служба> <операция>      — режим исполнителя (вызывается заданием
                                    планировщика): выполняет операцию и пишет
                                    результат в журнал
"""

import base64
import ctypes
import datetime
import io
import os
import re
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import messagebox, ttk

import customtkinter as ctk
from PIL import Image

import service_core as svc
import scheduler_core as sch
from help_text import HELP_TEXT, ABOUT_TEXT
from icons_data import ICON_B64

APP_TITLE = "Планировщик служб Windows"
APP_VERSION = "1.2"

ctk.set_appearance_mode("light")
ctk.set_default_color_theme("blue")

ACCENT = "#3B8ED0"
ACCENT_HOVER = "#3679B5"
GREEN = "#2FA572"
GREEN_HOVER = "#27875E"
RED = "#D93B3B"
RED_HOVER = "#B83030"

STATE_RU = {
    "Ready": "Готово",
    "Running": "Выполняется",
    "Disabled": "Отключено",
    "Queued": "В очередь",
    "Unknown": "Неизвестно",
    "": "",
}

SCHEDULE_ORDER = ("daily", "weekly", "monthly", "once", "startup", "interval")


def load_icons(size=20):
    """Иконки из base64 -> CTkImage."""
    icons = {}
    for name, b64 in ICON_B64.items():
        img = Image.open(io.BytesIO(base64.b64decode(b64)))
        icons[name] = ctk.CTkImage(light_image=img, dark_image=img,
                                   size=(size, size))
    return icons


# ---------------------------------------------------------------------------
# Журнал событий Windows
# ---------------------------------------------------------------------------
# События пишутся в отдельный журнал Windows "ServiceScheduler"
# (Просмотр событий -> Журналы приложений и служб -> ServiceScheduler).
# Файловый журнал рядом с программой не создаётся.

EVENT_LOG_NAME = "ServiceScheduler"
EVENT_SOURCE = "ServiceScheduler"

EVENT_IDS = {"INFO": 1000, "SUCCESS": 1001, "ERROR": 1002}
EVENT_TYPES = {"INFO": "Information", "SUCCESS": "Information",
               "ERROR": "Error"}


def _ps_encoded(script, timeout=60):
    """Запуск PowerShell-скрипта через -EncodedCommand (без проблем кодировок)."""
    enc = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    try:
        proc = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
             "-EncodedCommand", enc],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        out = proc.stdout.decode("utf-8", errors="replace") if proc.stdout else ""
        return proc.returncode, out
    except Exception:  # noqa: BLE001 — журналирование не должно ронять программу
        return -1, ""


def log_line(message, level="INFO"):
    """Записать событие в журнал Windows 'ServiceScheduler'."""
    msg_b64 = base64.b64encode(message.encode("utf-8")).decode("ascii")
    eid = EVENT_IDS.get(level, 1000)
    etype = EVENT_TYPES.get(level, "Information")
    script = (
        "$ProgressPreference = 'SilentlyContinue'\n"
        "$msg = [Text.Encoding]::UTF8.GetString("
        "[Convert]::FromBase64String('%s'))\n"
        "try {\n"
        "    if (-not [System.Diagnostics.EventLog]::SourceExists('%s')) {\n"
        "        New-EventLog -LogName '%s' -Source '%s'\n"
        "    }\n"
        "    Write-EventLog -LogName '%s' -Source '%s' -EventId %d"
        " -EntryType %s -Message $msg\n"
        "} catch {}\n" % (msg_b64, EVENT_SOURCE, EVENT_LOG_NAME, EVENT_SOURCE,
                          EVENT_LOG_NAME, EVENT_SOURCE, eid, etype))
    _ps_encoded(script)


def read_event_log(max_events=300):
    """Читает последние события журнала Windows 'ServiceScheduler'."""
    script = (
        "$ProgressPreference = 'SilentlyContinue'\n"
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8\n"
        "$events = Get-WinEvent -LogName '%s' -MaxEvents %d"
        " -ErrorAction SilentlyContinue\n"
        "foreach ($e in $events) {\n"
        "    $msg = ($e.Message -replace \"`r?`n\", ' | ')\n"
        "    '[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}' -f"
        " $e.TimeCreated, $e.LevelDisplayName, $msg\n"
        "}\n" % (EVENT_LOG_NAME, max_events))
    code, out = _ps_encoded(script, timeout=120)
    if code != 0 or not out.strip():
        return ""
    # отфильтровываем служебный CLIXML-мусор перенаправленных потоков
    lines = [ln for ln in out.splitlines()
             if ln.strip() and not ln.startswith(("#<", "<Objs", "</Objs"))]
    return "\n".join(lines).strip()


# ---------------------------------------------------------------------------
# Права администратора
# ---------------------------------------------------------------------------

def is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def relaunch_as_admin():
    if getattr(sys, "frozen", False):
        exe = sys.executable
        params = ""
    else:
        exe = sys.executable
        params = '"%s"' % os.path.abspath(__file__)
    ctypes.windll.shell32.ShellExecuteW(None, "runas", exe, params, None, 1)


# ---------------------------------------------------------------------------
# Режим исполнителя (вызывается заданием планировщика)
# ---------------------------------------------------------------------------

def executor_mode(service_name, operation):
    log_line("Задание запущено: служба=%s, операция=%s" % (service_name, operation))
    try:
        ok, msg = svc.run_operation(service_name, operation)
    except Exception as exc:  # noqa: BLE001 — в режиме исполнителя логируем всё
        ok, msg = False, "Внутренняя ошибка: %s" % exc
    result = "УСПЕХ" if ok else "ОШИБКА"
    op_ru = svc.OPERATION_NAMES_RU.get(operation, operation)
    log_line("%s: служба=%s, операция=%s — %s" % (result, service_name, op_ru, msg),
             level="SUCCESS" if ok else "ERROR")
    return 0 if ok else 1


# ---------------------------------------------------------------------------
# Всплывающая подсказка для кнопок-иконок
# ---------------------------------------------------------------------------

class Tooltip:
    def __init__(self, widget, text):
        self.widget = widget
        self.text = text
        self.tip = None
        self._after_id = None
        widget.bind("<Enter>", self._schedule)
        widget.bind("<Leave>", self._hide)
        widget.bind("<ButtonPress>", self._hide)

    def _schedule(self, _e=None):
        self._cancel()
        self._after_id = self.widget.after(500, self._show)

    def _cancel(self):
        if self._after_id:
            self.widget.after_cancel(self._after_id)
            self._after_id = None

    def _show(self):
        if self.tip:
            return
        x = self.widget.winfo_rootx() + self.widget.winfo_width() // 2
        y = self.widget.winfo_rooty() + self.widget.winfo_height() + 6
        tip = tk.Toplevel(self.widget)
        tip.overrideredirect(True)
        tip.attributes("-topmost", True)
        label = tk.Label(tip, text=self.text, font=("Segoe UI", 9),
                         bg="#333333", fg="white", padx=8, pady=4)
        label.pack()
        tip.geometry("+%d+%d" % (x - tip.winfo_reqwidth() // 2, y))
        self.tip = tip

    def _hide(self, _e=None):
        self._cancel()
        if self.tip:
            self.tip.destroy()
            self.tip = None


# ---------------------------------------------------------------------------
# Диалог добавления / изменения задания
# ---------------------------------------------------------------------------

class TaskDialog(ctk.CTkToplevel):
    def __init__(self, master, services, on_saved, preset=None):
        super().__init__(master)
        self.title("Добавить задание для службы" if not preset else "Изменить задание")
        self.resizable(False, False)
        self.transient(master)
        self.on_saved = on_saved
        self.services = services

        # карта "текст в списке" -> имя службы
        self.svc_map = {}
        self.all_svc_texts = []
        for s in services:
            running = s["status"].lower() == "running"
            mark = "🟢 " if running else "⚪ "
            text = "%s%s  (%s)" % (mark, s["display_name"] or s["name"], s["name"])
            self.svc_map[text] = s["name"]
            self.all_svc_texts.append(text)

        body = ctk.CTkFrame(self, fg_color="transparent")
        body.pack(fill="both", expand=True, padx=16, pady=14)

        # --- Операция ---
        op_row = ctk.CTkFrame(body, fg_color="transparent")
        op_row.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(op_row, text="Операция:", width=80, anchor="w",
                     font=("Segoe UI", 13)).pack(side="left")
        self.op_combo = ctk.CTkComboBox(
            op_row, width=280, state="readonly",
            values=[svc.OPERATION_NAMES_RU[o] for o in svc.OPERATIONS],
            font=("Segoe UI", 13), dropdown_font=("Segoe UI", 12))
        self.op_combo.pack(side="left")
        self.op_combo.set(svc.OPERATION_NAMES_RU["restart"])

        # --- Служба: выпадающий список со стрелкой и поиском ---
        svc_row = ctk.CTkFrame(body, fg_color="transparent")
        svc_row.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(svc_row, text="Служба:", width=80, anchor="w",
                     font=("Segoe UI", 13)).pack(side="left")
        self.svc_combo = ctk.CTkComboBox(
            svc_row, width=420, values=self.all_svc_texts,
            font=("Segoe UI", 13), dropdown_font=("Segoe UI", 12))
        self.svc_combo.pack(side="left")
        self.svc_combo.set("")
        # ввод фильтрует список; выбор по стрелке вписывает значение в поле
        self.svc_combo.bind("<KeyRelease>", self._filter_services)
        ctk.CTkLabel(body,
                     text="Начните вводить имя службы для поиска или "
                          "выберите её по стрелке справа",
                     font=("Segoe UI", 11), text_color="gray").pack(anchor="w",
                                                                    pady=(0, 10))

        # --- Расписание ---
        sched_box = ctk.CTkFrame(body)
        sched_box.pack(fill="x", pady=(0, 10))
        ctk.CTkLabel(sched_box, text="Расписание",
                     font=("Segoe UI", 13, "bold")).pack(anchor="w",
                                                         padx=10, pady=(8, 0))
        type_row = ctk.CTkFrame(sched_box, fg_color="transparent")
        type_row.pack(fill="x", padx=10, pady=6)
        ctk.CTkLabel(type_row, text="Тип:", width=50, anchor="w",
                     font=("Segoe UI", 13)).pack(side="left")
        self.sched_combo = ctk.CTkComboBox(
            type_row, width=220, state="readonly",
            values=[sch.SCHEDULE_NAMES_RU[k] for k in SCHEDULE_ORDER],
            font=("Segoe UI", 13), dropdown_font=("Segoe UI", 12),
            command=lambda _v: self._show_schedule_fields())
        self.sched_combo.pack(side="left")
        self.sched_combo.set(sch.SCHEDULE_NAMES_RU["daily"])

        self.sched_frame = ctk.CTkFrame(sched_box, fg_color="transparent")
        self.sched_frame.pack(fill="x", padx=10, pady=(0, 8))

        self.time_var = tk.StringVar(value="09:00")
        self.date_var = tk.StringVar(
            value=datetime.date.today().strftime("%Y-%m-%d"))
        self.dom_var = tk.StringVar(value="1")
        self.interval_var = tk.StringVar(value="60")
        self.day_vars = {d: tk.BooleanVar(value=(d == "MON"))
                         for d in sch.WEEKDAYS_RU}
        self._show_schedule_fields()

        # --- Отключить задание ---
        self.disabled_var = tk.BooleanVar(value=False)
        ctk.CTkCheckBox(
            body, text="Отключить это задание (создать, но не выполнять)",
            variable=self.disabled_var,
            font=("Segoe UI", 12)).pack(anchor="w", pady=(0, 12))

        # --- Кнопки ---
        btns = ctk.CTkFrame(body, fg_color="transparent")
        btns.pack(fill="x")
        ctk.CTkButton(btns, text="Отмена", width=110, fg_color="gray60",
                      hover_color="gray50",
                      command=self.destroy).pack(side="right", padx=(6, 0))
        ctk.CTkButton(btns, text="Сохранить  >>", width=130,
                      fg_color=GREEN, hover_color=GREEN_HOVER,
                      font=("Segoe UI", 13, "bold"),
                      command=self._save).pack(side="right")

        # предустановка при редактировании
        if preset:
            op = preset.get("operation")
            if op in svc.OPERATION_NAMES_RU:
                self.op_combo.set(svc.OPERATION_NAMES_RU[op])
            service = preset.get("service")
            if service:
                for text, name in self.svc_map.items():
                    if name == service:
                        self.svc_combo.set(text)
                        break

        self.bind("<Escape>", lambda _e: self.destroy())
        self.after(100, self._center_and_grab)

    def _center_and_grab(self):
        self.update_idletasks()
        try:
            self.grab_set()
            self.focus_set()
        except tk.TclError:
            pass

    # -- фильтр списка служб при вводе --
    def _filter_services(self, _event=None):
        typed = self.svc_combo.get()
        needle = re.sub(r"^[🟢⚪ ]+", "", typed).strip().lower()
        if not needle:
            self.svc_combo.configure(values=self.all_svc_texts)
            return
        filtered = [t for t in self.all_svc_texts
                    if needle in re.sub(r"^[🟢⚪ ]+", "", t).lower()]
        self.svc_combo.configure(values=filtered or ["(ничего не найдено)"])

    def _selected_service(self):
        text = self.svc_combo.get().strip()
        if text in self.svc_map:
            return self.svc_map[text]
        # вариант «вписали вручную без маркера»
        m = re.search(r"\(([^()]+)\)\s*$", text)
        if m and m.group(1) in {s["name"] for s in self.services}:
            return m.group(1)
        return None

    # -- поля расписания --
    def _show_schedule_fields(self):
        for w in self.sched_frame.winfo_children():
            w.destroy()
        label = self.sched_combo.get()
        stype = next(k for k in SCHEDULE_ORDER
                     if sch.SCHEDULE_NAMES_RU[k] == label)

        def add_time(row=0):
            ctk.CTkLabel(self.sched_frame, text="Время (ЧЧ:ММ):",
                         font=("Segoe UI", 12)).grid(
                row=row, column=0, sticky="w", padx=(0, 6), pady=3)
            ctk.CTkEntry(self.sched_frame, textvariable=self.time_var,
                         width=80, font=("Segoe UI", 12)).grid(
                row=row, column=1, sticky="w", pady=3)

        if stype in ("daily", "interval"):
            add_time()
        if stype == "weekly":
            add_time()
            days_frame = ctk.CTkFrame(self.sched_frame, fg_color="transparent")
            days_frame.grid(row=1, column=0, columnspan=8, sticky="w", pady=3)
            for i, (code, name_ru) in enumerate(sch.WEEKDAYS_RU.items()):
                ctk.CTkCheckBox(days_frame, text=name_ru,
                                variable=self.day_vars[code],
                                font=("Segoe UI", 11), width=110).grid(
                    row=0, column=i, sticky="w", padx=(0, 6))
        if stype == "monthly":
            add_time()
            ctk.CTkLabel(self.sched_frame, text="Число месяца:",
                         font=("Segoe UI", 12)).grid(
                row=1, column=0, sticky="w", padx=(0, 6), pady=3)
            ctk.CTkEntry(self.sched_frame, textvariable=self.dom_var,
                         width=60, font=("Segoe UI", 12)).grid(
                row=1, column=1, sticky="w", pady=3)
        if stype == "once":
            add_time()
            ctk.CTkLabel(self.sched_frame, text="Дата (ГГГГ-ММ-ДД):",
                         font=("Segoe UI", 12)).grid(
                row=1, column=0, sticky="w", padx=(0, 6), pady=3)
            ctk.CTkEntry(self.sched_frame, textvariable=self.date_var,
                         width=110, font=("Segoe UI", 12)).grid(
                row=1, column=1, sticky="w", pady=3)
        if stype == "interval":
            ctk.CTkLabel(self.sched_frame, text="Интервал (мин):",
                         font=("Segoe UI", 12)).grid(
                row=1, column=0, sticky="w", padx=(0, 6), pady=3)
            ctk.CTkEntry(self.sched_frame, textvariable=self.interval_var,
                         width=70, font=("Segoe UI", 12)).grid(
                row=1, column=1, sticky="w", pady=3)
        if stype == "startup":
            ctk.CTkLabel(self.sched_frame,
                         text="Задание будет выполняться при каждой загрузке Windows.",
                         font=("Segoe UI", 12), text_color="gray").grid(
                row=0, column=0, sticky="w", pady=3)

    def _build_schedule(self):
        label = self.sched_combo.get()
        stype = next(k for k in SCHEDULE_ORDER
                     if sch.SCHEDULE_NAMES_RU[k] == label)
        time_str = self.time_var.get().strip()
        schedule = {"type": stype}
        if stype != "startup":
            if not re.match(r"^\d{1,2}:\d{2}$", time_str):
                raise ValueError("Укажите время в формате ЧЧ:ММ (например, 09:00)")
            hh, mm = time_str.split(":")
            schedule["time"] = "%02d:%02d" % (int(hh), int(mm))
        if stype == "weekly":
            days = [c for c, v in self.day_vars.items() if v.get()]
            if not days:
                raise ValueError("Выберите хотя бы один день недели")
            schedule["days"] = days
        elif stype == "monthly":
            dom = int(self.dom_var.get())
            if not 1 <= dom <= 31:
                raise ValueError("Число месяца должно быть от 1 до 31")
            schedule["day_of_month"] = dom
        elif stype == "once":
            date_str = self.date_var.get().strip()
            m = re.match(r"^(\d{2})\.(\d{2})\.(\d{4})$", date_str)
            if m:  # ДД.ММ.ГГГГ -> ГГГГ-ММ-ДД
                date_str = "%s-%s-%s" % (m.group(3), m.group(2), m.group(1))
            if not re.match(r"^\d{4}-\d{2}-\d{2}$", date_str):
                raise ValueError("Укажите дату в формате ГГГГ-ММ-ДД")
            schedule["date"] = date_str
        elif stype == "interval":
            minutes = int(self.interval_var.get())
            if not 1 <= minutes <= 1439:
                raise ValueError("Интервал должен быть от 1 до 1439 минут")
            schedule["interval_minutes"] = minutes
        return schedule

    def _save(self):
        service = self._selected_service()
        if not service:
            messagebox.showwarning("Задание", "Выберите службу из списка.",
                                   parent=self)
            return
        op_ru = self.op_combo.get()
        operation = next((k for k, v in svc.OPERATION_NAMES_RU.items()
                          if v == op_ru), None)
        if not operation:
            messagebox.showwarning("Задание", "Выберите операцию.", parent=self)
            return
        try:
            schedule = self._build_schedule()
        except (ValueError, tk.TclError) as exc:
            messagebox.showwarning("Задание", str(exc), parent=self)
            return
        try:
            full_name = sch.create_task(service, operation, schedule,
                                        disabled=self.disabled_var.get())
        except sch.SchedulerError as exc:
            messagebox.showerror("Ошибка планировщика", str(exc), parent=self)
            return
        log_line("Создано/изменено задание %s (%s, %s, %s)" % (
            full_name, service, operation, sch.describe_schedule(schedule)))
        messagebox.showinfo("Задание",
                            "Задание сохранено:\n%s" % full_name, parent=self)
        self.destroy()
        self.on_saved()


# ---------------------------------------------------------------------------
# Окно справки / журнала
# ---------------------------------------------------------------------------

class TextWindow(ctk.CTkToplevel):
    def __init__(self, master, title, text):
        super().__init__(master)
        self.title(title)
        self.geometry("780x580")
        box = ctk.CTkTextbox(self, wrap="word", font=("Segoe UI", 12))
        box.pack(fill="both", expand=True, padx=12, pady=(12, 6))
        box.insert("1.0", text)
        box.configure(state="disabled")
        ctk.CTkButton(self, text="Закрыть", width=110,
                      command=self.destroy).pack(pady=(0, 10))


# ---------------------------------------------------------------------------
# Главное окно
# ---------------------------------------------------------------------------

class MainApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("%s v%s" % (APP_TITLE, APP_VERSION))
        self.geometry("1150x620")
        self.minsize(960, 500)

        self.icons = load_icons()
        self.show_all_var = tk.BooleanVar(value=False)
        self.tasks = []
        self.services = []

        self._style_tree()
        self._build_menu()
        self._build_toolbar()
        self._build_table()
        self._build_statusbar()

        self.refresh_services()
        self.refresh_tasks()

    # ----- стиль таблицы -----
    def _style_tree(self):
        style = ttk.Style(self)
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass
        style.configure("Treeview",
                        background="white", fieldbackground="white",
                        foreground="#1f2937", rowheight=30,
                        font=("Segoe UI", 10), borderwidth=0)
        style.configure("Treeview.Heading",
                        background="#eef2f7", foreground="#1f2937",
                        font=("Segoe UI", 10, "bold"), relief="flat",
                        padding=(6, 6))
        style.map("Treeview",
                  background=[("selected", ACCENT)],
                  foreground=[("selected", "white")])
        style.map("Treeview.Heading", background=[("active", "#e0e8f2")])

    # ----- построение интерфейса -----
    def _build_menu(self):
        menubar = tk.Menu(self)

        m_file = tk.Menu(menubar, tearoff=0)
        m_file.add_command(label="Выход", command=self.destroy)
        menubar.add_cascade(label="Файл", menu=m_file)

        m_view = tk.Menu(menubar, tearoff=0)
        m_view.add_command(label="Обновить", command=self.refresh_tasks,
                           accelerator="F5")
        m_view.add_checkbutton(
            label="Показать все задачи планировщика Windows",
            variable=self.show_all_var, command=self.refresh_tasks)
        menubar.add_cascade(label="Вид", menu=m_view)

        m_tasks = tk.Menu(menubar, tearoff=0)
        m_tasks.add_command(label="Добавить...", command=self.add_task)
        m_tasks.add_command(label="Изменить...", command=self.edit_task)
        m_tasks.add_command(label="Удалить", command=self.delete_task)
        m_tasks.add_separator()
        m_tasks.add_command(label="Выполнить сейчас", command=self.run_now)
        m_tasks.add_command(label="Включить / отключить",
                            command=self.toggle_task)
        menubar.add_cascade(label="Задания", menu=m_tasks)

        m_tools = tk.Menu(menubar, tearoff=0)
        m_tools.add_command(label="Журнал выполнения", command=self.show_log)
        menubar.add_cascade(label="Инструменты", menu=m_tools)

        m_help = tk.Menu(menubar, tearoff=0)
        m_help.add_command(label="Руководство пользователя",
                           command=self.show_help)
        m_help.add_command(label="О программе", command=self.show_about)
        menubar.add_cascade(label="Справка", menu=m_help)

        self.config(menu=menubar)
        self.bind("<F5>", lambda _e: self.refresh_tasks())

    def _build_toolbar(self):
        bar = ctk.CTkFrame(self, fg_color="transparent")
        bar.pack(fill="x", padx=10, pady=(8, 2))

        buttons = [
            ("add", "Добавить задание", self.add_task, GREEN, GREEN_HOVER),
            ("edit", "Изменить задание", self.edit_task, ACCENT, ACCENT_HOVER),
            ("delete", "Удалить задание", self.delete_task, RED, RED_HOVER),
            ("run", "Выполнить сейчас", self.run_now, GREEN, GREEN_HOVER),
            ("power", "Включить / отключить задание", self.toggle_task,
             "#E8A13D", "#C8871F"),
            ("refresh", "Обновить список (F5)", self.refresh_tasks,
             ACCENT, ACCENT_HOVER),
            ("journal", "Журнал выполнения", self.show_log,
             "#6B7280", "#525964"),
            ("help", "Справка", self.show_help, ACCENT, ACCENT_HOVER),
        ]
        for name, hint, cmd, color, hover in buttons:
            btn = ctk.CTkButton(
                bar, text="", image=self.icons[name], width=42, height=36,
                corner_radius=8, fg_color=color, hover_color=hover,
                command=cmd)
            btn.pack(side="left", padx=3)
            Tooltip(btn, hint)

        ctk.CTkCheckBox(
            bar, text="Показать все задачи планировщика Windows",
            variable=self.show_all_var, command=self.refresh_tasks,
            font=("Segoe UI", 12)).pack(side="right", padx=6)

    def _build_table(self):
        frame = ctk.CTkFrame(self, corner_radius=10)
        frame.pack(fill="both", expand=True, padx=10, pady=8)
        columns = ("task", "service", "operation", "schedule",
                   "last", "next", "state", "kind")
        self.tree = ttk.Treeview(frame, columns=columns, show="headings",
                                 selectmode="browse")
        headings = {
            "task": ("Задание", 250),
            "service": ("Служба", 170),
            "operation": ("Операция", 170),
            "schedule": ("Расписание", 160),
            "last": ("Последний запуск", 140),
            "next": ("Следующий запуск", 140),
            "state": ("Состояние", 100),
            "kind": ("Тип", 90),
        }
        for key, (text, width) in headings.items():
            self.tree.heading(key, text=text)
            self.tree.column(key, width=width, anchor="w")
        scroll = ttk.Scrollbar(frame, orient="vertical",
                               command=self.tree.yview)
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.pack(side="left", fill="both", expand=True,
                       padx=(6, 0), pady=6)
        scroll.pack(side="right", fill="y", padx=(0, 6), pady=6)
        self.tree.tag_configure("foreign", foreground="#9ca3af")
        self.tree.tag_configure("disabled", foreground="#b00000")
        self.tree.bind("<Double-1>", lambda _e: self.show_log())

    def _build_statusbar(self):
        self.status_var = tk.StringVar(value="Готово")
        ctk.CTkLabel(self, textvariable=self.status_var, anchor="w",
                     font=("Segoe UI", 11), height=24,
                     fg_color="#eef2f7", corner_radius=0).pack(
            fill="x", side="bottom")

    # ----- данные -----
    def refresh_services(self):
        def worker():
            try:
                services = svc.list_services()
            except Exception as exc:  # noqa: BLE001
                services = []
                self.after(0, lambda: messagebox.showerror(
                    "Ошибка", "Не удалось получить список служб:\n%s" % exc))
            self.services = services
            self.after(0, lambda: self.status_var.set(
                "Служб в системе: %d" % len(services)))
        threading.Thread(target=worker, daemon=True).start()

    def refresh_tasks(self):
        own_only = not self.show_all_var.get()
        self.status_var.set("Чтение списка заданий...")
        self.tree.delete(*self.tree.get_children())

        def worker():
            try:
                tasks = sch.list_tasks(own_only=own_only)
            except Exception as exc:  # noqa: BLE001
                self.after(0, lambda: (
                    self.status_var.set("Ошибка чтения заданий"),
                    messagebox.showerror(
                        "Ошибка",
                        "Не удалось получить список заданий:\n%s" % exc)))
                return
            self.tasks = tasks
            self.after(0, self._fill_table)

        threading.Thread(target=worker, daemon=True).start()

    def _fill_table(self):
        self.tree.delete(*self.tree.get_children())
        for t in self.tasks:
            service, operation = sch.parse_task_name(t["task_name"])
            if t["own"] and operation:
                service_text = service
                op_text = svc.OPERATION_NAMES_RU.get(operation, operation)
                name_text = t["task_name"]
                kind = "Своя"
            elif t["own"]:
                service_text = ""
                op_text = ""
                name_text = t["task_name"]
                kind = "Своя"
            else:
                service_text = ""
                op_text = ""
                name_text = t["full_name"]
                kind = "Системная"
            state_ru = STATE_RU.get(t["state"], t["state"])
            tags = []
            if not t["own"]:
                tags.append("foreign")
            if t["state"] == "Disabled":
                tags.append("disabled")
            self.tree.insert(
                "", "end", iid=t["full_name"],
                values=(name_text, service_text, op_text,
                        sch.describe_trigger(t.get("trigger", "")),
                        t["last_run"] or "—", t["next_run"] or "—",
                        state_ru, kind),
                tags=tuple(tags))
        own_count = sum(1 for t in self.tasks if t["own"])
        self.status_var.set(
            "Заданий: %d (своих: %d)" % (len(self.tasks), own_count))

    def _selected_task(self):
        sel = self.tree.selection()
        if not sel:
            return None
        full_name = sel[0]
        for t in self.tasks:
            if t["full_name"] == full_name:
                return t
        return None

    def _require_own_task(self):
        t = self._selected_task()
        if t is None:
            messagebox.showwarning("Задания", "Выберите задание в таблице.")
            return None
        if not t["own"]:
            messagebox.showinfo(
                "Задания",
                "Это системное задание — оно доступно только для просмотра.\n"
                "Снимите флажок «Показать все задачи…», "
                "чтобы вернуться к своим заданиям.")
            return None
        return t

    # ----- действия -----
    def add_task(self):
        if not self.services:
            messagebox.showwarning("Службы", "Список служб ещё загружается.")
            return
        TaskDialog(self, self.services, on_saved=self.refresh_tasks)

    def edit_task(self):
        t = self._require_own_task()
        if not t:
            return
        service, operation = sch.parse_task_name(t["task_name"])
        messagebox.showinfo(
            "Изменение задания",
            "Задание будет пересохранено: выберите операцию, службу и "
            "задайте расписание заново.")
        TaskDialog(self, self.services, on_saved=self.refresh_tasks,
                   preset={"service": service, "operation": operation})

    def delete_task(self):
        t = self._require_own_task()
        if not t:
            return
        if not messagebox.askyesno(
                "Удаление задания",
                "Удалить задание?\n%s" % t["full_name"]):
            return
        try:
            sch.delete_task(t["full_name"])
        except sch.SchedulerError as exc:
            messagebox.showerror("Ошибка", str(exc))
            return
        log_line("Удалено задание %s" % t["full_name"])
        self.refresh_tasks()

    def run_now(self):
        t = self._require_own_task()
        if not t:
            return
        try:
            sch.run_task_now(t["full_name"])
        except sch.SchedulerError as exc:
            messagebox.showerror("Ошибка", str(exc))
            return
        log_line("Задание %s запущено вручную" % t["full_name"])
        self.status_var.set("Задание запущено: %s" % t["task_name"])
        self.after(3000, self.refresh_tasks)

    def toggle_task(self):
        t = self._require_own_task()
        if not t:
            return
        enable = t["state"] == "Disabled"
        try:
            sch.set_task_enabled(t["full_name"], enable)
        except sch.SchedulerError as exc:
            messagebox.showerror("Ошибка", str(exc))
            return
        log_line("Задание %s %s" % (t["full_name"],
                                    "включено" if enable else "отключено"))
        self.refresh_tasks()

    # ----- справка / журнал -----
    def show_help(self):
        TextWindow(self, "Справка — Руководство пользователя", HELP_TEXT)

    def show_about(self):
        messagebox.showinfo("О программе", ABOUT_TEXT)

    def show_log(self):
        text = read_event_log()
        if not text.strip():
            text = ("Журнал пока пуст.\n\nСобытия хранятся в журнале Windows:\n"
                    "Просмотр событий (eventvwr.msc) -> Журналы приложений "
                    "и служб -> ServiceScheduler")
        TextWindow(
            self,
            "Журнал выполнения — Журналы Windows / ServiceScheduler",
            "События также доступны в Просмотре событий Windows "
            "(eventvwr.msc):\nЖурналы приложений и служб -> ServiceScheduler\n"
            + "=" * 70 + "\n\n" + text)


# ---------------------------------------------------------------------------
# Точка входа
# ---------------------------------------------------------------------------

def main():
    args = sys.argv[1:]
    if len(args) >= 3 and args[0] == "--exec":
        sys.exit(executor_mode(args[1], args[2]))

    if not is_admin():
        relaunch_as_admin()
        return

    app = MainApp()
    app.mainloop()


if __name__ == "__main__":
    main()

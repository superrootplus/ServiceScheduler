# -*- coding: utf-8 -*-
"""
scheduler_core.py — слой работы с Планировщиком заданий Windows.

Свои задания создаются в папке \\ServiceManager\\ с именами
SvcMgr_<Служба>_<Операция>. Задания выполняются от SYSTEM с наивысшими
правами и вызывают эту же программу в режиме исполнителя:
    ServiceScheduler.exe --exec <служба> <операция>

Создание заданий — через schtasks.exe (работает на любых ревизиях Windows,
ключи не зависят от локали). Чтение/управление — через модуль PowerShell
ScheduledTasks (стабильные имена свойств, не зависят от локали).
"""

import csv
import io
import re
import subprocess
import sys
import os

from service_core import _powershell, OPERATIONS

TASK_FOLDER = "\\ServiceManager\\"
TASK_PREFIX = "SvcMgr_"

# Русские названия типов расписаний для GUI
SCHEDULE_NAMES_RU = {
    "daily": "Ежедневно",
    "weekly": "Еженедельно",
    "monthly": "Ежемесячно",
    "once": "Однократно",
    "startup": "При запуске системы",
    "interval": "Каждые N минут",
}

WEEKDAYS_RU = {
    "MON": "Понедельник", "TUE": "Вторник", "WED": "Среда",
    "THU": "Четверг", "FRI": "Пятница", "SAT": "Суббота", "SUN": "Воскресенье",
}


class SchedulerError(Exception):
    """Ошибка работы с планировщиком."""
    pass


def _run(cmd, timeout=120):
    try:
        proc = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        out = proc.stdout.decode("cp866", errors="replace") if proc.stdout else ""
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return -1, "Превышено время ожидания команды"
    except FileNotFoundError as exc:
        return -1, str(exc)


# ---------------------------------------------------------------------------
# Команда-исполнитель
# ---------------------------------------------------------------------------

def get_executor_path():
    """Путь, который будет записан в задание планировщика."""
    if getattr(sys, "frozen", False):
        # Собранный exe
        return '"%s"' % os.path.abspath(sys.executable)
    # Режим разработки: pythonw.exe <абсолютный путь app.py>
    pythonw = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")
    if not os.path.exists(pythonw):
        pythonw = sys.executable
    app_py = os.path.join(os.path.dirname(os.path.abspath(__file__)), "app.py")
    return '"%s" "%s"' % (pythonw, app_py)


def make_task_name(service_name, operation):
    return TASK_PREFIX + service_name + "_" + operation


def parse_task_name(task_name):
    """Из имени задания извлекает (служба, операция) или (None, None)."""
    if not task_name.startswith(TASK_PREFIX):
        return None, None
    rest = task_name[len(TASK_PREFIX):]
    for op in OPERATIONS:
        suffix = "_" + op
        if rest.endswith(suffix):
            return rest[:-len(suffix)], op
    return rest, None


# ---------------------------------------------------------------------------
# Создание задания
# ---------------------------------------------------------------------------

def _short_date_pattern():
    """Шаблон короткой даты системной локали (для schtasks /sd)."""
    code, out = _powershell("(Get-Culture).DateTimeFormat.ShortDatePattern")
    if code == 0 and out.strip():
        return out.strip()
    return "dd.MM.yyyy"


def _format_date_for_schtasks(iso_date):
    """'2026-10-25' -> строка даты в формате локали для /sd."""
    y, m, d = iso_date.split("-")
    pattern = _short_date_pattern()
    result = pattern
    result = re.sub(r"y+", "Y", result)
    result = re.sub(r"d+", "D", result)
    result = re.sub(r"M+", "M", result)
    result = result.replace("Y", y).replace("M", m).replace("D", d)
    return result


def _schedule_args(schedule):
    """Словарь расписания -> аргументы schtasks."""
    stype = schedule.get("type")
    time_str = schedule.get("time", "09:00")
    args = []
    if stype == "daily":
        args += ["/sc", "DAILY", "/st", time_str]
    elif stype == "weekly":
        days = ",".join(schedule.get("days", ["MON"]))
        args += ["/sc", "WEEKLY", "/d", days, "/st", time_str]
    elif stype == "monthly":
        dom = str(schedule.get("day_of_month", 1))
        args += ["/sc", "MONTHLY", "/d", dom, "/st", time_str]
    elif stype == "once":
        date_str = _format_date_for_schtasks(schedule.get("date", ""))
        args += ["/sc", "ONCE", "/st", time_str, "/sd", date_str]
    elif stype == "startup":
        args += ["/sc", "ONSTART"]
    elif stype == "interval":
        minutes = int(schedule.get("interval_minutes", 60))
        if minutes >= 60 and minutes % 60 == 0:
            args += ["/sc", "HOURLY", "/mo", str(minutes // 60)]
        else:
            args += ["/sc", "MINUTE", "/mo", str(minutes)]
        args += ["/st", time_str]
    else:
        raise SchedulerError("Неизвестный тип расписания: %r" % stype)
    return args


def describe_schedule(schedule):
    """Человекочитаемое описание расписания на русском."""
    stype = schedule.get("type")
    time_str = schedule.get("time", "09:00")
    if stype == "daily":
        return "Ежедневно в %s" % time_str
    if stype == "weekly":
        days = ", ".join(WEEKDAYS_RU.get(d, d) for d in schedule.get("days", []))
        return "Еженедельно (%s) в %s" % (days, time_str)
    if stype == "monthly":
        return "Ежемесячно, %s-е число в %s" % (schedule.get("day_of_month", 1), time_str)
    if stype == "once":
        return "Однократно %s в %s" % (schedule.get("date", ""), time_str)
    if stype == "startup":
        return "При запуске системы"
    if stype == "interval":
        return "Каждые %s мин, начиная с %s" % (schedule.get("interval_minutes", 60), time_str)
    return str(schedule)


def create_task(service_name, operation, schedule, disabled=False):
    """Создаёт (или перезаписывает) задание в планировщике."""
    if operation not in OPERATIONS:
        raise SchedulerError("Неизвестная операция: %r" % operation)
    task_name = make_task_name(service_name, operation)
    full_name = TASK_FOLDER + task_name
    run_cmd = "%s --exec \"%s\" %s" % (get_executor_path(), service_name, operation)
    # Кавычки передаём как есть: subprocess вызывает schtasks напрямую,
    # без cmd.exe, и Task Scheduler корректно сохраняет их в XML задания.
    tr_arg = run_cmd
    cmd = [
        "schtasks.exe", "/create", "/f",
        "/tn", full_name,
        "/tr", tr_arg,
        "/ru", "SYSTEM",
        "/rl", "HIGHEST",
    ]
    cmd += _schedule_args(schedule)
    code, out = _run(cmd)
    if code != 0:
        raise SchedulerError("schtasks /create завершился с ошибкой:\n" + out.strip())
    # Ключ /disable для schtasks /create есть не во всех ревизиях Windows,
    # поэтому отключаем задание отдельной командой после создания.
    if disabled:
        set_task_enabled(full_name, False)
    return full_name


def delete_task(full_name):
    code, out = _run(["schtasks.exe", "/delete", "/f", "/tn", full_name])
    if code != 0:
        raise SchedulerError("schtasks /delete завершился с ошибкой:\n" + out.strip())


def run_task_now(full_name):
    code, out = _run(["schtasks.exe", "/run", "/tn", full_name])
    if code != 0:
        raise SchedulerError("schtasks /run завершился с ошибкой:\n" + out.strip())


# ---------------------------------------------------------------------------
# Чтение списка заданий
# ---------------------------------------------------------------------------

_PS_LIST_TMPL = r"""
param([string]$TaskPath)
$tasks = if ($TaskPath) {
    Get-ScheduledTask -TaskPath $TaskPath -ErrorAction SilentlyContinue
} else {
    Get-ScheduledTask -ErrorAction SilentlyContinue
}
$result = foreach ($t in $tasks) {
    $info = $t | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue
    $action = ""
    if ($t.Actions -and $t.Actions.Count -gt 0) {
        $action = ($t.Actions[0].Execute + " " + $t.Actions[0].Arguments).Trim()
    }
    $last = ""; $next = ""
    if ($info -and $info.LastRunTime -and $info.LastRunTime.Year -gt 2000) {
        $last = $info.LastRunTime.ToString("yyyy-MM-dd HH:mm:ss")
    }
    if ($info -and $info.NextRunTime -and $info.NextRunTime.Year -gt 2000) {
        $next = $info.NextRunTime.ToString("yyyy-MM-dd HH:mm:ss")
    }
    $lastResult = ""
    if ($info) { $lastResult = "0x{0:X}" -f $info.LastTaskResult }
    $trig = ""
    if ($t.Triggers -and $t.Triggers.Count -gt 0) {
        try {
            $trig = [string]$t.Triggers[0].CimClass.CimClassName + "|" + [string]$t.Triggers[0].StartBoundary
        } catch { $trig = "" }
    }
    [PSCustomObject]@{
        TaskName   = $t.TaskName
        TaskPath   = $t.TaskPath
        State      = [string]$t.State
        LastRun    = $last
        NextRun    = $next
        LastResult = $lastResult
        Action     = $action
        Trigger    = $trig
    }
}
$result | ConvertTo-Csv -NoTypeInformation
"""


def list_tasks(own_only=True, timeout=180):
    """Список заданий планировщика.

    own_only=True  — только задания программы (папка \\ServiceManager\\)
    own_only=False — все задания планировщика Windows

    Возвращает список словарей: task_name, task_path, state, last_run,
    next_run, last_result, action, own (bool).
    """
    task_path = TASK_FOLDER if own_only else ""
    injected = _PS_LIST_TMPL.replace('param([string]$TaskPath)',
                                     '$TaskPath = "%s"' % task_path.replace('"', '`"'))
    code, out = _powershell(injected, timeout=timeout)
    if code != 0:
        raise SchedulerError("Не удалось получить список заданий:\n" + out.strip())
    result = []
    text = out.strip()
    if not text:
        return result
    reader = csv.DictReader(io.StringIO(text))
    for row in reader:
        name = (row.get("TaskName") or "").strip()
        path = (row.get("TaskPath") or "").strip()
        own = path.rstrip("\\") + "\\" == TASK_FOLDER or name.startswith(TASK_PREFIX)
        result.append({
            "task_name": name,
            "task_path": path,
            "full_name": (path.rstrip("\\") + "\\" + name) if path else name,
            "state": (row.get("State") or "").strip(),
            "last_run": (row.get("LastRun") or "").strip(),
            "next_run": (row.get("NextRun") or "").strip(),
            "last_result": (row.get("LastResult") or "").strip(),
            "action": (row.get("Action") or "").strip(),
            "trigger": (row.get("Trigger") or "").strip(),
            "own": own,
        })
    return result


def describe_trigger(trigger):
    """'MSFT_TaskDailyTrigger|2026-10-23T09:00:00' -> 'Ежедневно в 09:00'."""
    if not trigger:
        return ""
    class_name, _, start = trigger.partition("|")
    time_str = ""
    m = re.search(r"T(\d{2}:\d{2})", start)
    if m:
        time_str = m.group(1)
    date_str = ""
    m = re.match(r"(\d{4})-(\d{2})-(\d{2})T", start)
    if m:
        date_str = "%s.%s.%s" % (m.group(3), m.group(2), m.group(1))
    mapping = {
        "MSFT_TaskDailyTrigger": "Ежедневно",
        "MSFT_TaskWeeklyTrigger": "Еженедельно",
        "MSFT_TaskMonthlyTrigger": "Ежемесячно",
        "MSFT_TaskMonthlyDOWTrigger": "Ежемесячно",
        "MSFT_TaskTimeTrigger": "Однократно",
        "MSFT_TaskBootTrigger": "При запуске системы",
        "MSFT_TaskLogonTrigger": "При входе в систему",
        "MSFT_TaskIdleTrigger": "При простое",
        "MSFT_TaskRegistrationTrigger": "При создании задания",
        "MSFT_TaskSessionStateChangeTrigger": "При смене состояния сеанса",
        "MSFT_TaskEventTrigger": "По событию",
    }
    label = mapping.get(class_name, class_name or "")
    if class_name == "MSFT_TaskTimeTrigger" and date_str:
        return "%s %s в %s" % (label, date_str, time_str)
    if time_str and class_name not in ("MSFT_TaskBootTrigger", "MSFT_TaskLogonTrigger"):
        return "%s в %s" % (label, time_str)
    return label


def set_task_enabled(full_name, enabled):
    """Включить/выключить задание."""
    # Разделяем путь и имя
    m = re.match(r"^(.*\\)([^\\]+)$", full_name)
    if not m:
        raise SchedulerError("Некорректное имя задания: " + full_name)
    path, name = m.group(1), m.group(2)
    verb = "Enable-ScheduledTask" if enabled else "Disable-ScheduledTask"
    ps = '%s -TaskName "%s" -TaskPath "%s"' % (
        verb, name.replace('"', '`"'), path.replace('"', '`"'))
    code, out = _powershell(ps)
    if code != 0:
        raise SchedulerError("Не удалось изменить состояние задания:\n" + out.strip())

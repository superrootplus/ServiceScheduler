# -*- coding: utf-8 -*-
"""
service_core.py — ядро операций со службами Windows.

Операции:
  start        — запуск службы (net start)
  stop         — остановка службы (net stop) с ожиданием
  restart      — мягкий перезапуск (stop -> ожидание Stopped -> start)
  hard_restart — жёсткий перезапуск: мягкий stop с таймаутом, при зависании
                 принудительный kill процесса (taskkill /F /PID), затем start

Используются только штатные команды Windows (sc.exe, net.exe, taskkill.exe,
powershell.exe), поэтому работает на любых ревизиях Windows.
"""

import csv
import io
import re
import subprocess
import time

# Таймаут ожидания остановки службы перед принудительным kill (секунды)
STOP_TIMEOUT = 30
# Интервал опроса состояния службы (секунды)
POLL_INTERVAL = 1.0

OPERATIONS = ("start", "stop", "restart", "hard_restart")

# Русские названия операций для GUI и справки
OPERATION_NAMES_RU = {
    "start": "Запуск",
    "stop": "Остановка",
    "restart": "Перезапуск",
    "hard_restart": "Жёсткий перезапуск (kill)",
}


class ServiceError(Exception):
    """Ошибка операции со службой."""
    pass


def _run(cmd, timeout=120):
    """Запуск команды, возвращает (returncode, stdout+stderr)."""
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


def _powershell(ps_command, timeout=120):
    """Запуск команды PowerShell, вывод в UTF-8."""
    wrapped = (
        "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; "
        + ps_command
    )
    try:
        proc = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
             "-Command", wrapped],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        out = proc.stdout.decode("utf-8", errors="replace") if proc.stdout else ""
        return proc.returncode, out
    except subprocess.TimeoutExpired:
        return -1, "Превышено время ожидания PowerShell"
    except FileNotFoundError:
        return -1, "powershell.exe не найден"


# ---------------------------------------------------------------------------
# Список служб
# ---------------------------------------------------------------------------

def list_services():
    """Возвращает список служб: [{name, display_name, status}, ...].

    status: 'Running', 'Stopped' и т.д. (как в Get-Service).
    Основной путь — PowerShell Get-Service (CSV, работает даже на PS 2.0).
    Запасной путь — парсинг `sc.exe query`.
    """
    services = _list_services_ps()
    if services is None:
        services = _list_services_sc()
    return services


def _list_services_ps():
    ps = (
        "Get-Service | Select-Object Name,DisplayName,Status "
        "| ConvertTo-Csv -NoTypeInformation"
    )
    code, out = _powershell(ps)
    if code != 0 or not out.strip():
        return None
    result = []
    try:
        reader = csv.DictReader(io.StringIO(out.strip()))
        for row in reader:
            result.append({
                "name": (row.get("Name") or "").strip(),
                "display_name": (row.get("DisplayName") or "").strip(),
                "status": (row.get("Status") or "").strip(),
            })
    except Exception:
        return None
    return result or None


def _list_services_sc():
    code, out = _run(["sc.exe", "query", "state=", "all"])
    if code != 0:
        return []
    result = []
    current = None
    for line in out.splitlines():
        m = re.search(r"(?:SERVICE_NAME|Имя_службы):\s*(\S+)", line, re.IGNORECASE)
        if m:
            current = {"name": m.group(1), "display_name": m.group(1),
                       "status": ""}
            result.append(current)
            continue
        m = re.search(
            r":\s*[1-7]\s+"
            r"(STOPPED|START_PENDING|STOP_PENDING|RUNNING|"
            r"CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b", line)
        if m and current is not None:
            current["status"] = m.group(1).capitalize()
    return result


# ---------------------------------------------------------------------------
# Состояние службы
# ---------------------------------------------------------------------------

def get_service_state(service_name):
    """Возвращает состояние службы: RUNNING / STOPPED / START_PENDING / ... или None.

    Метка строки локализована (STATE / СОСТОЯНИЕ), поэтому ищем по коду
    состояния 1-7 и английскому слову-значению, которое не локализуется.
    """
    code, out = _run(["sc.exe", "query", service_name])
    if code != 0:
        return None
    m = re.search(
        r":\s*[1-7]\s+"
        r"(STOPPED|START_PENDING|STOP_PENDING|RUNNING|"
        r"CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b", out)
    return m.group(1) if m else None


def get_service_pid(service_name):
    """Возвращает PID процесса службы (int) или None.

    Метка локализована (PID / ID_процесса); строка PID — единственная,
    где после двоеточия стоит только десятичное число.
    """
    code, out = _run(["sc.exe", "queryex", service_name])
    if code != 0:
        return None
    m = re.search(r":\s*(\d+)\s*$", out, re.MULTILINE)
    if not m:
        return None
    pid = int(m.group(1))
    return pid if pid > 0 else None


def _wait_state(service_name, wanted, timeout):
    """Ждёт перехода службы в состояние wanted (секунд). True если дождались."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        state = get_service_state(service_name)
        if state == wanted:
            return True
        time.sleep(POLL_INTERVAL)
    return get_service_state(service_name) == wanted


# ---------------------------------------------------------------------------
# Операции
# ---------------------------------------------------------------------------

def start_service(service_name):
    """Запуск службы."""
    state = get_service_state(service_name)
    if state == "RUNNING":
        return True, "Служба уже запущена"
    code, out = _run(["net.exe", "start", service_name])
    if code == 0 and _wait_state(service_name, "RUNNING", 30):
        return True, "Служба запущена"
    # net start может вернуть 0 до фактического старта — даём ещё шанс
    if _wait_state(service_name, "RUNNING", 15):
        return True, "Служба запущена"
    return False, "Не удалось запустить службу: " + out.strip()


def stop_service(service_name, timeout=STOP_TIMEOUT):
    """Остановка службы с ожиданием."""
    state = get_service_state(service_name)
    if state == "STOPPED":
        return True, "Служба уже остановлена"
    code, out = _run(["net.exe", "stop", service_name])
    if code != 0:
        return False, "Команда остановки завершилась с ошибкой: " + out.strip()
    if _wait_state(service_name, "STOPPED", timeout):
        return True, "Служба остановлена"
    return False, "Служба не остановилась за отведённое время"


def restart_service(service_name):
    """Мягкий перезапуск: stop -> ожидание -> start."""
    state = get_service_state(service_name)
    if state == "STOPPED":
        return start_service(service_name)
    ok, msg = stop_service(service_name)
    if not ok:
        return False, "Ошибка при остановке: " + msg
    return start_service(service_name)


def kill_service_process(service_name):
    """Принудительное завершение процесса службы (taskkill /F)."""
    pid = get_service_pid(service_name)
    if not pid:
        return False, "Не удалось получить PID службы"
    code, out = _run(["taskkill.exe", "/F", "/PID", str(pid)])
    if code == 0:
        return True, "Процесс службы (PID %d) принудительно завершён" % pid
    return False, "taskkill завершился с ошибкой: " + out.strip()


def hard_restart_service(service_name):
    """Жёсткий перезапуск: мягкий stop с таймаутом, при зависании — kill, затем start."""
    state = get_service_state(service_name)
    log = []
    if state != "STOPPED":
        code, _out = _run(["net.exe", "stop", service_name])
        if code == 0 and _wait_state(service_name, "STOPPED", STOP_TIMEOUT):
            log.append("Служба остановлена штатно")
        else:
            log.append("Служба не остановилась за %d сек — принудительный kill"
                       % STOP_TIMEOUT)
            ok, msg = kill_service_process(service_name)
            log.append(msg)
            if not ok:
                return False, "; ".join(log)
            _wait_state(service_name, "STOPPED", 10)
    ok, msg = start_service(service_name)
    log.append(msg)
    return ok, "; ".join(log)


def run_operation(service_name, operation):
    """Единая точка входа: выполнить операцию над службой.

    Возвращает (успех: bool, сообщение: str).
    """
    if operation == "start":
        return start_service(service_name)
    if operation == "stop":
        return stop_service(service_name)
    if operation == "restart":
        return restart_service(service_name)
    if operation == "hard_restart":
        return hard_restart_service(service_name)
    raise ServiceError("Неизвестная операция: %r" % operation)

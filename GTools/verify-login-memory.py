"""Windows login-memory integration test: isolated server + two separate Unity processes.

python GTools/verify-login-memory.py --unity <Unity.exe>
Credentials use only the temporary server's port and a random .invalid test target.
"""
import argparse
import ctypes
from ctypes import wintypes
from pathlib import Path
import shutil
import socket
import subprocess
import time
import uuid

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--unity', default=r'C:\Program Files\Unity\Hub\Editor\2022.3.47f1c1\Editor\Unity.exe')
parser.add_argument('--stage', default='.build-check/login-memory')
parser.add_argument('--prepared', action='store_true', help='Use an already synchronized isolated project.')
args = parser.parse_args()
stage = (root / args.stage).resolve()
if not stage.is_relative_to(root / '.build-check'):
    raise RuntimeError('Validation project must be inside .build-check, never the working project.')
stage.mkdir(parents=True, exist_ok=True)
if not args.prepared:
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        result = subprocess.run(['robocopy', str(root/folder), str(stage/folder), '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP'],
                                stdout=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode >= 8:
            raise RuntimeError('Could not copy validation project: ' + folder)
editor_sources = stage/'Assets/Editor'
editor_sources.mkdir(parents=True, exist_ok=True)
shutil.copy2(root/'GTools/LoginValidation/VerifyLoginMemory.cs', editor_sources/'VerifyLoginMemory.cs')
server_dll = root/'GServer/OperationBlacktide.Server/bin/Debug/net8.0/OperationBlacktide.Server.dll'
if not server_dll.exists():
    raise RuntimeError('Build the server with GTools/build-server.bat first.')
with socket.socket() as probe:
    probe.bind(('127.0.0.1', 0))
    port = probe.getsockname()[1]
(stage/'login-memory-test-port.txt').write_text(str(port))
server = editor = None
with (stage/'login-memory-server.log').open('w', encoding='utf-8') as log:
    try:
        server = subprocess.Popen(['dotnet', str(server_dll), '--port', str(port), '--accounts',
                                   str(stage/('login-memory-accounts-'+uuid.uuid4().hex+'.json'))],
                                  stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT,
                                  text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        deadline = time.monotonic()+20
        while True:
            try:
                with socket.create_connection(('127.0.0.1', port), timeout=1):
                    break
            except OSError:
                if time.monotonic() > deadline or server.poll() is not None:
                    raise RuntimeError('Temporary login server failed to start.')
                time.sleep(.2)
        print('Testing login memory against an isolated local server.', flush=True)
        for method in ('Run', 'Restart'):
            path = stage/('login-memory-'+method.lower()+'.log')
            editor = subprocess.Popen([args.unity, '-batchmode', '-projectPath', str(stage),
                                       '-executeMethod', 'VerifyLoginMemory.'+method, '-logFile', str(path)],
                                      creationflags=subprocess.CREATE_NO_WINDOW)
            editor.wait(timeout=300)
            content = path.read_text(encoding='utf-8', errors='replace')
            for line in content.splitlines():
                if line.startswith(('LOGIN_MEMORY_PASS:', 'LOGIN_MEMORY_STORE_PASS:', 'LOGIN_MEMORY_FAIL:')):
                    print(line, flush=True)
            if editor.returncode or 'LOGIN_MEMORY_PASS:' not in content:
                raise RuntimeError('Login-memory validation failed; see ' + str(path))
    finally:
        if editor and editor.poll() is None:
            editor.terminate(); editor.wait(timeout=15)
        if server and server.poll() is None:
            server.stdin.write('stop\n'); server.stdin.flush()
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.terminate(); server.wait(timeout=10)
        # Only our synthetic test endpoint; never enumerate or read the user's credential store.
        delete = ctypes.WinDLL('advapi32', use_last_error=True).CredDeleteW
        delete.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD]
        delete.restype = wintypes.BOOL
        if not delete('OperationBlacktide/Login/v1/127.0.0.1:'+str(port), 1, 0) and ctypes.get_last_error() != 1168:
            raise RuntimeError('Could not clean the isolated login-memory test credential.')

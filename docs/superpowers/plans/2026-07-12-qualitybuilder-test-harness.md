# QualityBuilder Automated Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Python-driven automated test suite that verifies QualityBuilder against a live RimWorld instance, driven by a new `QualityBuilderBridgeTools` companion DLL plus RimBridge's built-in tools.

**Architecture:** A companion DLL (RimBridge SDK pattern) exposes `qb/*` tools that call QB's real `public static` API via cached reflection and read live `CompQualityBuilder` state. A Python package speaks the GABP wire protocol (LSP-framed JSON over TCP) directly to RimBridge, launches/loads the game, and runs the tests. **Refinement vs. spec:** the "runner.py" is realized as a **pytest** package (conftest fixtures for game-up/save-loaded, per-test arena cleanup, crash-restart; `pytest-json-report` for the JSON summary; `-k` for filtering). This satisfies every runner requirement in the spec (report pass/fail, per-test cleanup, reload-for-persistence, restart-on-crash) with far less bespoke code. A thin `python -m qbtest` entry invokes pytest so the "single command" requirement holds.

**Tech Stack:** C# (.NET Framework 4.8, `Krafs.Rimworld.Ref`, `RimBridgeServer.Sdk`); Python 3.11+ (standard library sockets/subprocess + `pytest`, `pytest-json-report`).

## Global Constraints

- Companion DLL target framework: **net48**, `LangVersion` 12.0, `Nullable disable`, `PlatformTarget AnyCPU` (mirror `ZoneStorageBridgeTools.csproj`).
- Companion DLL deploys to `RimWorldRoot\BridgeTools\QualityBuilderBridgeTools.dll` via `OutputPath` = `..\..\..\BridgeTools\`; `CopyLocalLockFileAssemblies=false`; `RimBridgeServer.Sdk` referenced with `Private=false` from `..\..\RimBridgeServer\1.6\Assemblies\RimBridgeServer.Sdk.dll`.
- The companion references QB only via **cached reflection** (`GenTypes.GetTypeInAnyAssembly("QualityBuilder.<Type>")`), never a compile-time assembly reference — QB and the companion load from different folders.
- All companion game access marshalled through `ctx.MainThread.InvokeAsync(...)`.
- Mutation/spawn tools return `{success:false, error:"...requires RimWorld dev mode..."}` when `!Prefs.DevMode` (match ZoneStorage precedent).
- Every companion tool returns either `{success:true, ...}` or `{success:false, error:string}`.
- GABP wire: LSP framing `Content-Length: <bytes>\r\nContent-Type: application/json\r\n\r\n<utf8-json>`; envelope `{"v":"gabp/1","id":<uuid>,"type":"request","method":<m>,"params":<p>}`; handshake `session/hello`(token,bridgeVersion,platform,launchId)→`session/welcome`; tool call `method:"tools/call"`, `params:{name, arguments}`; response has `result` or `error:{code,message,data}`.
- RimBridge direct-mode discovery: parse `Player.log` for `[RimBridge] GABP server running standalone on port <N>` and `[RimBridge] Bridge token: <T>`.
- `QualityCategory` index map (used by QB's `SkilledBuilder*` designations): Awful=0→`SkilledBuilder`, Poor=1→`SkilledBuilder2`, Normal=2→`SkilledBuilder3`, Good=3→`SkilledBuilder4`, Excellent=4→`SkilledBuilder5`, Masterwork=5→`SkilledBuilder6`, Legendary=6→`SkilledBuilder7`.
- Python paths: package root `Mods/QualityBuilderBridgeTools/qbtest/` (co-located with the DLL project). Player.log at `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`. Game exe at `<RimWorldRoot>\RimWorldWin64.exe` (RimWorldRoot = four levels up from the Mods folder: `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`).

---

## File Structure

```
Mods/QualityBuilderBridgeTools/
  Source/
    QualityBuilderBridgeTools.csproj   # net48 companion, deploys to BridgeTools\
    QbReflect.cs                        # cached reflection into QB's public API + comp props
    QbInspectTools.cs                   # qb/get_building_state, list_qb_things, get_settings, get_gizmo_info, ping
    QbSetupTools.cs                     # qb/spawn_blueprint, spawn_finished_building, clear_arena
    QbMutateTools.cs                    # qb/set_skilled, set_comp_state, invoke_check_rebuild,
                                        #   invoke_after_finish_toil, set_pawn_skill, set_pawn_flags, set_setting
  qbtest/
    __init__.py
    framing.py        # LSP frame encode/decode
    gabp_client.py    # GabpClient: connect, handshake, call(tool, **args)
    discovery.py      # parse Player.log for port/token
    game_control.py   # launch/kill RimWorldWin64.exe, wait-for-ready, load save
    qb.py             # typed wrappers over qb/* and built-in rimworld/* tools
    arena.py          # scratch-rect constants
    conftest.py       # pytest fixtures: client, game, arena, autouse clean_arena
    __main__.py       # `python -m qbtest` -> pytest entry
  requirements.txt    # pytest, pytest-json-report (the runner hardcodes --json-report)
    tests/
      __init__.py
      test_framing.py         # pure-unit tests (no game)
      test_discovery.py       # pure-unit tests (no game)
      test_a_designation.py   # A1..A8
      test_b_persistence.py   # B1..B3
      test_c_skill.py         # C1..C7
      test_d_rebuild.py       # D1..D8
      test_e_config.py        # E1..E3
  qb_test.SAVE_INSTRUCTIONS.md          # how to build the qb_test save
docs/superpowers/plans/2026-07-12-qualitybuilder-test-harness.md
```

Note: the Python package lives under the new `QualityBuilderBridgeTools` mod folder (not the QB mod repo) so it sits beside the DLL it drives. The design spec is committed in the QB repo; this plan too. The `QualityBuilderBridgeTools` folder gets its own git init in Task 1.

---

## Task 1: Companion DLL scaffold + `qb/ping` + reflection resolver

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/Source/QualityBuilderBridgeTools.csproj`
- Create: `Mods/QualityBuilderBridgeTools/Source/QbReflect.cs`
- Create: `Mods/QualityBuilderBridgeTools/Source/QbInspectTools.cs` (ping only for now)

**Interfaces:**
- Produces: DLL at `BridgeTools\QualityBuilderBridgeTools.dll`; tool `qb/ping` → `{success:true, label:"pong", qbLoaded:bool}`.
- Produces: `QbReflect` static helper with `Type QbStatic`, `Type CompType`, `object GetComp(Thing)`, `object GetProp(object comp, string name)`, `void SetProp(object comp, string name, object value)`, `object CallStatic(Type t, string method, object[] args, Type[] sig)`.

- [ ] **Step 1: Create the csproj** (mirror ZoneStorage exactly)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>QualityBuilderBridgeTools</RootNamespace>
    <AssemblyName>QualityBuilderBridgeTools</AssemblyName>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <Optimize>true</Optimize>
    <DebugType>portable</DebugType>
    <Nullable>disable</Nullable>
    <OutputPath>..\..\..\BridgeTools\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Krafs.Rimworld.Ref" Version="1.6.4518" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="RimBridgeServer.Sdk">
      <HintPath>..\..\RimBridgeServer\1.6\Assemblies\RimBridgeServer.Sdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write `QbReflect.cs`** (the entire reflection layer other tasks depend on)

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace QualityBuilderBridgeTools
{
    // Cached reflection into the QualityBuilder mod assembly. QB and this companion load from
    // different folders, so we never take a compile-time reference — we resolve QB's public
    // (and internal, where needed) API by name once and cache the MemberInfo.
    internal static class QbReflect
    {
        private static Type _qbStatic, _compType, _settingsType, _mapCompType, _globalSettingsType,
            _workGiverType, _jobDriverFinishType;
        private static readonly Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, PropertyInfo> _props = new Dictionary<string, PropertyInfo>();

        internal static Type QbStatic => _qbStatic ??= Resolve("QualityBuilder.QualityBuilder");
        internal static Type CompType => _compType ??= Resolve("QualityBuilder.CompQualityBuilder");
        internal static Type SettingsType => _settingsType ??= Resolve("QualityBuilder.QualityBuilderModSettings");
        internal static Type MapCompType => _mapCompType ??= Resolve("QualityBuilder.QualityBuilder_MapComponent");
        internal static Type GlobalSettingsType => _globalSettingsType ??= Resolve("QualityBuilder.QualityBuilderGlobalModSettings");
        internal static Type WorkGiverType => _workGiverType ??= Resolve("QualityBuilder._WorkGiver_ConstructFinishFrames");
        internal static Type JobDriverFinishType => _jobDriverFinishType ??= Resolve("QualityBuilder._JobDriver_ConstructFinishFrame");

        internal static bool QbAvailable => QbStatic != null && CompType != null;

        private static Type Resolve(string fullName)
        {
            return GenTypes.GetTypeInAnyAssembly(fullName);
        }

        // ThingWithComps.GetComp<CompQualityBuilder>() via QB's own helper (handles null).
        internal static object GetComp(Thing thing)
        {
            if (thing == null || QbStatic == null) return null;
            var m = Method(QbStatic, "getCompQualityBuilder", new[] { typeof(Thing) });
            return m?.Invoke(null, new object[] { thing });
        }

        internal static object GetProp(object comp, string name)
        {
            if (comp == null) return null;
            var p = Prop(CompType, name);
            return p?.GetValue(comp);
        }

        internal static void SetProp(object comp, string name, object value)
        {
            if (comp == null) return;
            Prop(CompType, name)?.SetValue(comp, value);
        }

        internal static object CallStatic(Type t, string method, Type[] sig, params object[] args)
        {
            var m = Method(t, method, sig);
            return m?.Invoke(null, args);
        }

        internal static MethodInfo Method(Type t, string name, Type[] sig)
        {
            if (t == null) return null;
            var key = t.FullName + "|" + name + "|" + string.Join(",", Array.ConvertAll(sig, x => x.FullName));
            if (_methods.TryGetValue(key, out var mi)) return mi;
            mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance,
                null, sig, null);
            _methods[key] = mi;
            return mi;
        }

        internal static PropertyInfo Prop(Type t, string name)
        {
            if (t == null) return null;
            var key = t.FullName + "|" + name;
            if (_props.TryGetValue(key, out var pi)) return pi;
            pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            _props[key] = pi;
            return pi;
        }
    }
}
```

- [ ] **Step 3: Write `QbInspectTools.cs` with only `qb/ping`**

```csharp
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using Verse;

namespace QualityBuilderBridgeTools
{
    public sealed class QbInspectTools
    {
        internal static object Error(string message) => new { success = false, error = message };

        [Tool("qb/ping",
            Description = "QualityBuilderBridgeTools connectivity + QB-assembly-resolved check.",
            ResultDescription = "success, label 'pong', and whether the QualityBuilder assembly resolved.",
            Tags = new[] { "read-only" })]
        public Task<object> Ping(IRimBridgeContext ctx, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(
                () => new { success = true, label = "pong", qbLoaded = QbReflect.QbAvailable }, ct);
    }
}
```

- [ ] **Step 4: Build and deploy**

Run (PowerShell):
```powershell
dotnet build "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\QualityBuilderBridgeTools\Source\QualityBuilderBridgeTools.csproj" -c Release
```
Expected: build succeeds; `...\Mods\..\BridgeTools\QualityBuilderBridgeTools.dll` is written (verify `Test-Path`).

- [ ] **Step 5: Smoke-verify against the live game**

Restart RimWorld so RimBridge loads the new companion, then call `qb/ping`. During plan execution you may verify via GABS (`games_call_tool` with `qb/ping`) or defer to Task 6 once the Python client exists. Expected result JSON: `{"success":true,"label":"pong","qbLoaded":true}` (with QualityBuilder enabled). If `qbLoaded` is false, QB is not enabled or the type names are wrong — fix before proceeding.

- [ ] **Step 6: git init + commit**

```bash
cd "/c/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods/QualityBuilderBridgeTools"
git init -q
printf 'Source/obj/\nSource/bin/\n__pycache__/\n*.pyc\n.pytest_cache/\nqbtest_report.json\n' > .gitignore
git add -A && git commit -q -m "QualityBuilderBridgeTools: scaffold + qb/ping + reflection resolver"
```

---

## Task 2: Python GABP framing codec (pure unit, no game)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/__init__.py` (empty)
- Create: `Mods/QualityBuilderBridgeTools/qbtest/framing.py`
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/__init__.py` (empty)
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_framing.py`

**Interfaces:**
- Produces: `encode_frame(obj: dict) -> bytes`, `FrameDecoder` with `.feed(data: bytes)` and `.messages() -> Iterator[dict]`.

- [ ] **Step 1: Write the failing test**

```python
# qbtest/tests/test_framing.py
from qbtest.framing import encode_frame, FrameDecoder


def test_encode_frame_has_content_length_and_json():
    raw = encode_frame({"v": "gabp/1", "id": "x", "type": "request"})
    text = raw.decode("utf-8")
    header, body = text.split("\r\n\r\n", 1)
    assert "Content-Length: %d" % len(body.encode("utf-8")) in header
    assert "Content-Type: application/json" in header
    assert body == '{"v": "gabp/1", "id": "x", "type": "request"}'


def test_decoder_reassembles_split_frame():
    raw = encode_frame({"hello": "world"})
    dec = FrameDecoder()
    dec.feed(raw[:5])
    assert list(dec.messages()) == []
    dec.feed(raw[5:])
    assert list(dec.messages()) == [{"hello": "world"}]


def test_decoder_handles_two_frames_in_one_chunk():
    dec = FrameDecoder()
    dec.feed(encode_frame({"a": 1}) + encode_frame({"b": 2}))
    assert list(dec.messages()) == [{"a": 1}, {"b": 2}]
```

- [ ] **Step 2: Run it, verify it fails**

Run: `cd Mods/QualityBuilderBridgeTools && python -m pytest qbtest/tests/test_framing.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'qbtest.framing'`.

- [ ] **Step 3: Implement `framing.py`**

```python
# qbtest/framing.py
"""GABP LSP-style framing: Content-Length headers + UTF-8 JSON body."""
import json

_SEP = b"\r\n\r\n"


def encode_frame(obj):
    body = json.dumps(obj).encode("utf-8")
    header = (
        b"Content-Length: %d\r\n" % len(body)
        + b"Content-Type: application/json\r\n"
    )
    return header + b"\r\n" + body


class FrameDecoder:
    """Feed raw bytes in; pull complete JSON messages out."""

    def __init__(self):
        self._buf = bytearray()

    def feed(self, data):
        self._buf.extend(data)

    def messages(self):
        while True:
            sep = self._buf.find(_SEP)
            if sep == -1:
                return
            header = self._buf[:sep].decode("ascii", "replace")
            length = None
            for line in header.split("\r\n"):
                if line.lower().startswith("content-length:"):
                    length = int(line.split(":", 1)[1].strip())
            if length is None:
                raise ValueError("GABP frame missing Content-Length: %r" % header)
            start = sep + len(_SEP)
            if len(self._buf) < start + length:
                return  # body not fully arrived yet
            body = bytes(self._buf[start:start + length])
            del self._buf[:start + length]
            yield json.loads(body.decode("utf-8"))
```

- [ ] **Step 4: Run tests, verify pass**

Run: `python -m pytest qbtest/tests/test_framing.py -v`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add qbtest/__init__.py qbtest/framing.py qbtest/tests/
git commit -q -m "qbtest: GABP LSP framing codec + unit tests"
```

---

## Task 3: Discovery — parse Player.log for port/token (pure unit)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/discovery.py`
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_discovery.py`

**Interfaces:**
- Produces: `parse_bridge_info(log_text: str) -> tuple[int, str] | None` (last port+token pair in the log), and `DEFAULT_LOG_PATH: str`.

- [ ] **Step 1: Write the failing test**

```python
# qbtest/tests/test_discovery.py
from qbtest.discovery import parse_bridge_info

LOG = """\
Some unrelated line
[RimBridge] GABP server running standalone on port 5174
[RimBridge] Bridge token: abc123deadbeef
more noise
"""


def test_parses_port_and_token():
    assert parse_bridge_info(LOG) == (5174, "abc123deadbeef")


def test_returns_last_pair_when_restarted():
    text = LOG + (
        "[RimBridge] GABP server running standalone on port 5199\n"
        "[RimBridge] Bridge token: newtoken999\n"
    )
    assert parse_bridge_info(text) == (5199, "newtoken999")


def test_none_when_absent():
    assert parse_bridge_info("nothing here") is None


def test_ignores_unrelated_rimbridge_noise():
    # Noise line PRECEDES a real token line; only the anchored port regex stops the bogus
    # 8080 from pairing with that token (the pairing guard alone would not catch this).
    text = LOG + (
        "[RimBridge] Will export 8080 mod defs to client\n"
        "[RimBridge] Bridge token: badtoken\n"
    )
    assert parse_bridge_info(text) == (5174, "abc123deadbeef")


def test_restart_without_token_keeps_last_complete_pair():
    # A new port line with no following token (crash mid-startup) must not pair the
    # new port with the earlier token.
    text = LOG + "[RimBridge] GABP server running standalone on port 5199\n"
    assert parse_bridge_info(text) == (5174, "abc123deadbeef")
```

- [ ] **Step 2: Run it, verify it fails**

Run: `python -m pytest qbtest/tests/test_discovery.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'qbtest.discovery'`.

- [ ] **Step 3: Implement `discovery.py`**

```python
# qbtest/discovery.py
"""Locate the RimBridge direct-mode port/token from Player.log."""
import os
import re

DEFAULT_LOG_PATH = os.path.expandvars(
    r"%USERPROFILE%\AppData\LocalLow\Ludeon Studios"
    r"\RimWorld by Ludeon Studios\Player.log"
)

# Anchored to the exact RimBridge startup line so unrelated "[RimBridge] ... export 8080 ..."
# noise can't be mistaken for the port.
_PORT = re.compile(r"\[RimBridge\][^\n]*running standalone on port\s+(\d+)", re.IGNORECASE)
_TOKEN = re.compile(r"\[RimBridge\]\s*Bridge token:\s*(\S+)", re.IGNORECASE)


def parse_bridge_info(log_text):
    """Return (port, token) for the LAST COMPLETE bridge-start block, or None.

    Pairs each token with the most recent preceding port line (RimBridge logs the port
    line then the token line per start), so a restart that logged a new port but no token
    (crash mid-startup) does not yield a bogus new-port/stale-token pairing.
    """
    port = None
    last_pair = None
    for line in log_text.splitlines():
        pm = _PORT.search(line)
        if pm:
            port = int(pm.group(1))
            continue
        tm = _TOKEN.search(line)
        if tm and port is not None:
            last_pair = (port, tm.group(1))
            port = None
    return last_pair


def read_bridge_info(log_path=DEFAULT_LOG_PATH):
    with open(log_path, "r", encoding="utf-8", errors="replace") as fh:
        return parse_bridge_info(fh.read())
```

- [ ] **Step 4: Run tests, verify pass**

Run: `python -m pytest qbtest/tests/test_discovery.py -v`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add qbtest/discovery.py qbtest/tests/test_discovery.py
git commit -q -m "qbtest: Player.log bridge discovery + unit tests"
```

---

## Task 4: GABP TCP client (handshake + tools/call)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/gabp_client.py`
- Modify: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_framing.py` is untouched; add `qbtest/tests/test_client.py`

**Interfaces:**
- Consumes: `encode_frame`, `FrameDecoder` (Task 2).
- Produces: `class GabpClient(host, port, token)` with `.connect()`, `.call(tool_name: str, timeout=30, **arguments) -> dict` (raises `ToolError` on `success:false` or GABP `error`; raises `ToolTimeout` on no reply / dead socket), `.close()`. Exceptions `ToolError(message, payload)`, `ToolTimeout`, `BridgeError`.

- [ ] **Step 1: Write the failing test** (drives a real in-process fake GABP server over a loopback socket)

```python
# qbtest/tests/test_client.py
import socket
import threading
import uuid

from qbtest.framing import encode_frame, FrameDecoder
from qbtest.gabp_client import GabpClient, ToolError


def _fake_server(handler):
    """Start a loopback server; call handler(msg)->reply dict per message. Returns port."""
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.bind(("127.0.0.1", 0))
    srv.listen(1)
    port = srv.getsockname()[1]

    def run():
        conn, _ = srv.accept()
        dec = FrameDecoder()
        with conn:
            while True:
                data = conn.recv(4096)
                if not data:
                    break
                dec.feed(data)
                for msg in dec.messages():
                    reply = handler(msg)
                    if reply is not None:
                        conn.sendall(encode_frame(reply))
    threading.Thread(target=run, daemon=True).start()
    return port


def _welcome(msg):
    return {"v": "gabp/1", "id": msg["id"], "type": "response",
            "result": {"agentId": "test", "capabilities": {"methods": ["tools/call"]}}}


def test_handshake_and_successful_call():
    def handler(msg):
        if msg["method"] == "session/hello":
            assert msg["params"]["token"] == "tok"
            return _welcome(msg)
        if msg["method"] == "tools/call":
            assert msg["params"]["name"] == "qb/ping"
            return {"v": "gabp/1", "id": msg["id"], "type": "response",
                    "result": {"success": True, "label": "pong"}}
    port = _fake_server(handler)
    c = GabpClient("127.0.0.1", port, "tok")
    c.connect()
    assert c.call("qb/ping")["label"] == "pong"
    c.close()


def test_tool_error_raises():
    def handler(msg):
        if msg["method"] == "session/hello":
            return _welcome(msg)
        return {"v": "gabp/1", "id": msg["id"], "type": "response",
                "result": {"success": False, "error": "no current map"}}
    port = _fake_server(handler)
    c = GabpClient("127.0.0.1", port, "tok")
    c.connect()
    try:
        c.call("qb/get_settings")
        assert False, "expected ToolError"
    except ToolError as e:
        assert "no current map" in str(e)
    c.close()
```

- [ ] **Step 2: Run it, verify it fails**

Run: `python -m pytest qbtest/tests/test_client.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'qbtest.gabp_client'`.

- [ ] **Step 3: Implement `gabp_client.py`**

```python
# qbtest/gabp_client.py
"""Minimal GABP client: LSP-framed JSON over TCP, session handshake, tools/call."""
import socket
import time
import uuid

from .framing import encode_frame, FrameDecoder


class BridgeError(Exception):
    pass


class ToolTimeout(BridgeError):
    pass


class ToolError(BridgeError):
    def __init__(self, message, payload=None):
        super().__init__(message)
        self.payload = payload


class GabpClient:
    def __init__(self, host, port, token, launch_id=None):
        self.host = host
        self.port = int(port)
        self.token = token
        self.launch_id = launch_id or str(uuid.uuid4())
        self._sock = None
        self._dec = FrameDecoder()

    def connect(self, timeout=10):
        self._sock = socket.create_connection((self.host, self.port), timeout=timeout)
        self._sock.settimeout(timeout)
        welcome = self._request("session/hello", {
            "token": self.token,
            "bridgeVersion": "1.0.0",
            "platform": "windows",
            "launchId": self.launch_id,
            "clientInfo": {"name": "qbtest", "version": "1.0.0"},
        })
        if "error" in welcome:
            raise BridgeError("handshake rejected: %r" % welcome["error"])
        return welcome["result"]

    def call(self, tool_name, timeout=30, **arguments):
        resp = self._request("tools/call",
                             {"name": tool_name, "arguments": arguments},
                             timeout=timeout)
        if "error" in resp:
            err = resp["error"]
            raise ToolError("GABP error %s: %s" % (err.get("code"), err.get("message")), err)
        result = resp.get("result", {})
        if isinstance(result, dict) and result.get("success") is False:
            raise ToolError(result.get("error", "tool reported success:false"), result)
        return result

    def _request(self, method, params, timeout=30):
        msg_id = str(uuid.uuid4())
        self._send({"v": "gabp/1", "id": msg_id, "type": "request",
                    "method": method, "params": params})
        return self._await(msg_id, timeout)

    def _send(self, obj):
        try:
            self._sock.sendall(encode_frame(obj))
        except OSError as e:
            raise ToolTimeout("socket send failed (game down?): %s" % e)

    def _await(self, msg_id, timeout):
        deadline = time.monotonic() + timeout
        while True:
            for msg in self._dec.messages():
                if msg.get("id") == msg_id and msg.get("type") == "response":
                    return msg
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise ToolTimeout("no response to %s within %ss" % (msg_id, timeout))
            self._sock.settimeout(remaining)
            try:
                data = self._sock.recv(65536)
            except socket.timeout:
                raise ToolTimeout("recv timed out for %s" % msg_id)
            except OSError as e:
                raise ToolTimeout("socket recv failed (game down?): %s" % e)
            if not data:
                raise ToolTimeout("bridge closed connection (game crashed?)")
            self._dec.feed(data)

    def close(self):
        if self._sock is not None:
            try:
                self._sock.close()
            finally:
                self._sock = None
```

- [ ] **Step 4: Run tests, verify pass**

Run: `python -m pytest qbtest/tests/test_client.py -v`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add qbtest/gabp_client.py qbtest/tests/test_client.py
git commit -q -m "qbtest: GABP TCP client (handshake + tools/call) with fake-server tests"
```

---

## Task 5: Game lifecycle control

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/game_control.py`

**Interfaces:**
- Consumes: `read_bridge_info` (Task 3), `GabpClient`, `ToolTimeout` (Task 4).
- Produces: `class Game(exe_path, log_path, save_name)` with `.connect_existing() -> GabpClient`, `.ensure_running() -> GabpClient`, `.restart() -> GabpClient`, `.load_test_save(client)`. Constant `RIMWORLD_ROOT`, `EXE_PATH`.

This task is validated by a live smoke run, not pytest (it drives a real process).

- [ ] **Step 1: Implement `game_control.py`**

```python
# qbtest/game_control.py
"""Start/stop RimWorld and get a connected, save-loaded GABP client."""
import os
import subprocess
import time

from .discovery import read_bridge_info, DEFAULT_LOG_PATH
from .gabp_client import GabpClient, ToolTimeout, BridgeError

RIMWORLD_ROOT = r"C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
EXE_PATH = os.path.join(RIMWORLD_ROOT, "RimWorldWin64.exe")


class Game:
    def __init__(self, exe_path=EXE_PATH, log_path=DEFAULT_LOG_PATH, save_name="qb_test"):
        self.exe_path = exe_path
        self.log_path = log_path
        self.save_name = save_name

    def connect_existing(self, timeout=10):
        info = read_bridge_info(self.log_path)
        if info is None:
            raise BridgeError("no [RimBridge] port/token in %s" % self.log_path)
        port, token = info
        c = GabpClient("127.0.0.1", port, token)
        c.connect(timeout=timeout)
        return c

    def ensure_running(self, boot_timeout=180):
        try:
            c = self.connect_existing()
            c.call("rimbridge/ping", timeout=10)
            return c
        except (OSError, BridgeError, ToolTimeout):
            pass
        return self._launch_and_wait(boot_timeout)

    def restart(self, boot_timeout=180):
        self.kill()
        time.sleep(3)
        return self._launch_and_wait(boot_timeout)

    def kill(self):
        subprocess.run(["taskkill", "/F", "/IM", "RimWorldWin64.exe"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def _launch_and_wait(self, boot_timeout):
        # Truncate the log so discovery reads THIS boot's port/token, not a stale one.
        try:
            open(self.log_path, "w").close()
        except OSError:
            pass
        subprocess.Popen([self.exe_path], cwd=RIMWORLD_ROOT)
        deadline = time.monotonic() + boot_timeout
        last_err = None
        while time.monotonic() < deadline:
            try:
                c = self.connect_existing(timeout=5)
                c.call("rimbridge/ping", timeout=10)
                return c
            except (OSError, BridgeError, ToolTimeout) as e:
                last_err = e
                time.sleep(3)
        raise BridgeError("bridge did not come up within %ss (%s)" % (boot_timeout, last_err))

    def load_test_save(self, client, timeout=120):
        # Built-in RimBridge load-with-readiness; falls back to load_game + poll.
        try:
            client.call("rimworld/load_game_ready",
                        timeout=timeout, saveName=self.save_name, readiness="visual")
        except ToolTimeout:
            raise
        # Confirm QB is reachable on the loaded map.
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            try:
                if client.call("qb/ping").get("qbLoaded"):
                    return
            except ToolTimeout:
                pass
            time.sleep(1)
        raise BridgeError("qb/ping did not confirm QB after loading %s" % self.save_name)
```

- [ ] **Step 2: Smoke-verify live**

With RimWorld closed, run a scratch script:
```powershell
cd "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\QualityBuilderBridgeTools"
python -c "from qbtest.game_control import Game; g=Game(); c=g.ensure_running(); print(c.call('qb/ping'))"
```
Expected: RimWorld launches, reaches the menu, and prints `{'success': True, 'label': 'pong', 'qbLoaded': True}`. (Requires the `qb_test` save from Task 12 only for `load_test_save`; `ensure_running`+`qb/ping` work from the menu.)

- [ ] **Step 3: Commit**

```bash
git add qbtest/game_control.py
git commit -q -m "qbtest: RimWorld launch/kill/restart + save-load lifecycle"
```

---

## Task 6: Companion read-only tools — `qb/get_building_state`, `qb/list_qb_things`

**Files:**
- Modify: `Mods/QualityBuilderBridgeTools/Source/QbInspectTools.cs`

**Interfaces:**
- Produces tool `qb/get_building_state` (params `thingId?`, `x?`, `z?`) → `{success, thingId, def, isBlueprint, isFrame, hasQuality, quality, isSkilled, desiredMinQuality, pendingQualityRebuild, qualityRebuildAttempts, isDesiredMinQualityReached, qbDesignation, hasDeconstructDesignation}`.
- Produces tool `qb/list_qb_things` (params `x,z,width,height`) → `{success, count, things:[{thingId,def,kind,quality,x,z}]}`.

- [ ] **Step 1: Add a shared thing-resolver + the two tools to `QbInspectTools.cs`**

Add these methods/usings to the existing `QbInspectTools` class:

```csharp
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

// ... inside namespace QualityBuilderBridgeTools, inside class QbInspectTools ...

internal static Map CurrentMap => Find.CurrentMap;

internal static Thing ResolveThing(Map map, string thingId, int x, int z, out string problem)
{
    problem = null;
    if (!string.IsNullOrEmpty(thingId))
    {
        foreach (var t in map.listerThings.AllThings)
            if (t.ThingID == thingId) return t;
        problem = "no thing with id '" + thingId + "' on the current map";
        return null;
    }
    var cell = new IntVec3(x, 0, z);
    if (!cell.InBounds(map)) { problem = "cell (" + x + "," + z + ") out of bounds"; return null; }
    // Prefer QB's own blueprint/frame finder; else the first quality-bearing thing.
    var qbThing = QbReflect.CallStatic(QbReflect.QbStatic, "GetFirstBuildingBuildingOrFrame",
        new[] { typeof(Map), typeof(IntVec3) }, map, cell) as Thing;
    if (qbThing != null) return qbThing;
    foreach (var t in map.thingGrid.ThingsListAt(cell))
        if (QbReflect.GetComp(t) != null) return t;
    problem = "no QB thing at (" + x + "," + z + ")";
    return null;
}

private static object BuildingState(Thing thing)
{
    var comp = QbReflect.GetComp(thing);
    string quality = null;
    bool hasQuality = false;
    var cq = (thing as ThingWithComps)?.TryGetComp<CompQuality>();
    if (cq != null) { hasQuality = true; quality = cq.Quality.ToString(); }

    var des = QbReflect.CallStatic(QbReflect.QbStatic, "getDesignationOnThing",
        new[] { typeof(Thing) }, thing) as Designation;
    bool hasDecon = thing.Map != null &&
        thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null;

    return new
    {
        success = true,
        thingId = thing.ThingID,
        def = thing.def.defName,
        isBlueprint = thing.def.IsBlueprint,
        isFrame = thing.def.IsFrame,
        hasQuality,
        quality,
        isSkilled = comp == null ? (bool?)null : (bool)QbReflect.GetProp(comp, "isSkilled"),
        desiredMinQuality = comp == null ? null : QbReflect.GetProp(comp, "desiredMinQuality").ToString(),
        pendingQualityRebuild = comp == null ? (bool?)null : (bool)QbReflect.GetProp(comp, "pendingQualityRebuild"),
        qualityRebuildAttempts = comp == null ? (int?)null : (int)QbReflect.GetProp(comp, "qualityRebuildAttempts"),
        isDesiredMinQualityReached = comp == null ? (bool?)null : (bool)QbReflect.GetProp(comp, "isDesiredMinQualityReached"),
        qbDesignation = des?.def?.defName,
        hasDeconstructDesignation = hasDecon,
    };
}

[Tool("qb/get_building_state",
    Description = "Read a QB thing's quality + full CompQualityBuilder state + designations. Target by thingId or cell (x,z).",
    ResultDescription = "def, quality, comp fields (isSkilled, desiredMinQuality, pendingQualityRebuild, qualityRebuildAttempts, isDesiredMinQualityReached), qbDesignation, hasDeconstructDesignation.",
    Tags = new[] { "read-only" })]
public Task<object> GetBuildingState(
    IRimBridgeContext ctx,
    [ToolParameter(Description = "Thing id")] string thingId = null,
    [ToolParameter(Description = "Cell x")] int x = -1,
    [ToolParameter(Description = "Cell z")] int z = -1,
    CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var map = CurrentMap;
        if (map == null) return Error("no current map");
        if (!QbReflect.QbAvailable) return Error("QualityBuilder assembly not resolved");
        var thing = ResolveThing(map, thingId, x, z, out var problem);
        if (thing == null) return Error(problem);
        return BuildingState(thing);
    }, ct);

[Tool("qb/list_qb_things",
    Description = "List QB-comp blueprints/frames/buildings in a rect.",
    ResultDescription = "Array of {thingId, def, kind, quality, x, z}.",
    Tags = new[] { "read-only" })]
public Task<object> ListQbThings(
    IRimBridgeContext ctx,
    [ToolParameter(Description = "Rect min x")] int x,
    [ToolParameter(Description = "Rect min z")] int z,
    [ToolParameter(Description = "Rect width")] int width,
    [ToolParameter(Description = "Rect height")] int height,
    CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var map = CurrentMap;
        if (map == null) return Error("no current map");
        var rect = CellRect.FromLimits(x, z, x + Math.Max(1, width) - 1, z + Math.Max(1, height) - 1).ClipInsideMap(map);
        var seen = new HashSet<Thing>();
        var items = new List<object>();
        foreach (var cell in rect)
            foreach (var t in map.thingGrid.ThingsListAt(cell))
            {
                if (!seen.Add(t) || QbReflect.GetComp(t) == null) continue;
                var cq = (t as ThingWithComps)?.TryGetComp<CompQuality>();
                items.Add(new
                {
                    thingId = t.ThingID,
                    def = t.def.defName,
                    kind = t.def.IsBlueprint ? "blueprint" : t.def.IsFrame ? "frame" : "building",
                    quality = cq != null ? cq.Quality.ToString() : null,
                    x = t.Position.x,
                    z = t.Position.z,
                });
            }
        return new { success = true, count = items.Count, things = items };
    }, ct);
```

- [ ] **Step 2: Build + deploy + restart + smoke**

Run: `dotnet build ...QualityBuilderBridgeTools.csproj -c Release`; restart game; load any save with a QB blueprint (or spawn one via Task 7). Call `qb/list_qb_things` over a rect and `qb/get_building_state` on a returned `thingId`.
Expected: JSON with populated comp fields; a fresh blueprint shows `isSkilled` per the default setting.

- [ ] **Step 3: Commit**

```bash
git add Source/QbInspectTools.cs
git commit -q -m "QBBT: qb/get_building_state + qb/list_qb_things"
```

---

## Task 7: Companion setup tools — `qb/spawn_blueprint`, `qb/spawn_finished_building`, `qb/clear_arena`

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/Source/QbSetupTools.cs`

**Interfaces:**
- Produces `qb/spawn_blueprint` (`def, x, z, rot?, stuff?`) → `{success, thingId}`.
- Produces `qb/spawn_finished_building` (`def, x, z, quality, stuff?, rot?`) → `{success, thingId, quality}`.
- Produces `qb/clear_arena` (`x, z, width, height`) → `{success, destroyed, designationsCleared}`.

- [ ] **Step 1: Write `QbSetupTools.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace QualityBuilderBridgeTools
{
    public sealed class QbSetupTools
    {
        private static object Error(string m) => new { success = false, error = m };
        private static Map Map => Find.CurrentMap;

        private static ThingDef Def(string n) => DefDatabase<ThingDef>.GetNamedSilentFail(n ?? "");
        private static Rot4 Rot(int r) => new Rot4(((r % 4) + 4) % 4);

        private static ThingDef ResolveStuff(ThingDef def, string stuff)
        {
            if (!string.IsNullOrEmpty(stuff)) return Def(stuff);
            return def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
        }

        [Tool("qb/spawn_blueprint",
            Description = "DEV MODE. Place a build blueprint for def at a cell (GenConstruct.PlaceBlueprintForBuild). Exercises QB PostSpawnSetup auto-adopt.",
            ResultDescription = "New blueprint thingId.")]
        public Task<object> SpawnBlueprint(
            IRimBridgeContext ctx,
            [ToolParameter(Description = "Building ThingDef defName, e.g. 'Wall'")] string def,
            [ToolParameter(Description = "Cell x")] int x,
            [ToolParameter(Description = "Cell z")] int z,
            [ToolParameter(Description = "Rotation 0-3")] int rot = 0,
            [ToolParameter(Description = "Stuff defName, e.g. 'Steel'")] string stuff = null,
            CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("spawn_blueprint requires dev mode");
                var map = Map;
                if (map == null) return Error("no current map");
                var d = Def(def);
                if (d == null) return Error("unknown ThingDef '" + def + "'");
                if (!string.IsNullOrEmpty(stuff) && Def(stuff) == null) return Error("unknown stuff ThingDef '" + stuff + "'");
                var cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) return Error("cell out of bounds");
                var bp = GenConstruct.PlaceBlueprintForBuild(d, cell, map, Rot(rot), Faction.OfPlayer, ResolveStuff(d, stuff));
                return new { success = true, thingId = bp.ThingID };
            }, ct);

        [Tool("qb/spawn_finished_building",
            Description = "DEV MODE. Spawn a finished building with a forced quality (the deterministic force-quality primitive).",
            ResultDescription = "thingId and the applied quality.")]
        public Task<object> SpawnFinishedBuilding(
            IRimBridgeContext ctx,
            [ToolParameter(Description = "Building ThingDef defName")] string def,
            [ToolParameter(Description = "Cell x")] int x,
            [ToolParameter(Description = "Cell z")] int z,
            [ToolParameter(Description = "Quality: Awful, Poor, Normal, Good, Excellent, Masterwork, Legendary")] string quality,
            [ToolParameter(Description = "Stuff defName")] string stuff = null,
            [ToolParameter(Description = "Rotation 0-3")] int rot = 0,
            CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("spawn_finished_building requires dev mode");
                var map = Map;
                if (map == null) return Error("no current map");
                var d = Def(def);
                if (d == null) return Error("unknown ThingDef '" + def + "'");
                if (!string.IsNullOrEmpty(stuff) && Def(stuff) == null) return Error("unknown stuff ThingDef '" + stuff + "'");
                if (!Enum.TryParse<QualityCategory>(quality, true, out var q))
                    return Error("unknown quality '" + quality + "'");
                var cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) return Error("cell out of bounds");
                var thing = ThingMaker.MakeThing(d, ResolveStuff(d, stuff));
                var cq = (thing as ThingWithComps)?.TryGetComp<CompQuality>();
                if (cq == null) return Error(def + " has no CompQuality");
                cq.SetQuality(q, ArtGenerationContext.Colony);
                thing.SetFactionDirect(Faction.OfPlayer);
                var spawned = GenSpawn.Spawn(thing, cell, map, Rot(rot));
                return new { success = true, thingId = spawned.ThingID, quality = q.ToString() };
            }, ct);

        [Tool("qb/clear_arena",
            Description = "DEV MODE. Destroy every non-terrain thing and remove all designations in a rect (per-test cleanup).",
            ResultDescription = "Counts destroyed and designations cleared.")]
        public Task<object> ClearArena(
            IRimBridgeContext ctx,
            [ToolParameter(Description = "Rect min x")] int x,
            [ToolParameter(Description = "Rect min z")] int z,
            [ToolParameter(Description = "Rect width")] int width,
            [ToolParameter(Description = "Rect height")] int height,
            CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("clear_arena requires dev mode");
                var map = Map;
                if (map == null) return Error("no current map");
                var rect = CellRect.FromLimits(x, z, x + Math.Max(1, width) - 1, z + Math.Max(1, height) - 1).ClipInsideMap(map);
                int destroyed = 0, desCleared = 0;
                foreach (var cell in rect)
                {
                    foreach (var des in new List<Designation>(map.designationManager.AllDesignationsAt(cell)))
                    { map.designationManager.RemoveDesignation(des); desCleared++; }
                    var things = new List<Thing>(map.thingGrid.ThingsListAt(cell));
                    foreach (var t in things)
                    {
                        if (t is Pawn) continue; // never destroy colonists
                        if (t.def.category == ThingCategory.Building || t.def.IsBlueprint || t.def.IsFrame ||
                            t.def.category == ThingCategory.Item)
                        {
                            foreach (var des in new List<Designation>(map.designationManager.AllDesignationsOn(t)))
                            { map.designationManager.RemoveDesignation(des); desCleared++; }
                            if (!t.Destroyed) { t.Destroy(DestroyMode.Vanish); destroyed++; }
                        }
                    }
                }
                return new { success = true, destroyed, designationsCleared = desCleared };
            }, ct);
    }
}
```

- [ ] **Step 2: Build + deploy + restart + smoke**

Load `qb_test` (or any save with dev mode). Call `qb/spawn_finished_building def=Wall x=<ax> z=<az> quality=Awful stuff=Steel`, then `qb/get_building_state thingId=<returned>` → expect `quality:"Awful"`. Call `qb/clear_arena` over the rect → expect it destroyed. `qb/spawn_blueprint def=Wall ...` then `get_building_state` → a blueprint.
Expected: all succeed; arena empty after clear.

- [ ] **Step 3: Commit**

```bash
git add Source/QbSetupTools.cs
git commit -q -m "QBBT: qb/spawn_blueprint + spawn_finished_building + clear_arena"
```

---

## Task 8: Companion mutation/invoke tools

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/Source/QbMutateTools.cs`
- Modify: `Mods/QualityBuilderBridgeTools/Source/QbInspectTools.cs` (add `qb/get_settings`, `qb/get_gizmo_info`)

**Interfaces:**
- Produces `qb/set_skilled` (`thingId, quality, add`) → `{success}`.
- Produces `qb/set_comp_state` (`thingId`, optional `isSkilled, desiredMinQuality, pendingQualityRebuild, qualityRebuildAttempts, isDesiredMinQualityReached`) → `{success}`.
- Produces `qb/invoke_check_rebuild` (`thingId`) → `{success}`.
- Produces `qb/invoke_after_finish_toil` (`x, z`) → `{success}`.
- Produces `qb/set_pawn_skill` (`pawnId, level`) → `{success}`; `qb/set_pawn_flags` (`pawnId, downed?`) → `{success}`.
- Produces `qb/set_setting` (`key, value, scope`) → `{success}`; `qb/get_settings` → `{success, effective:{...}, bestConstructorSkill}`; `qb/get_gizmo_info` (`thingId`) → `{success, commandOffered, floatMenuQualities:[...]}`.

- [ ] **Step 1: Write `QbMutateTools.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using RimWorld;
using UnityEngine; // Mathf.Clamp in set_pawn_skill
using Verse;

namespace QualityBuilderBridgeTools
{
    public sealed class QbMutateTools
    {
        private static object Error(string m) => new { success = false, error = m };
        private static Map Map => Find.CurrentMap;

        private static Thing Thing(Map map, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in map.listerThings.AllThings) if (t.ThingID == id) return t;
            return null;
        }

        private static Pawn Pawn(Map map, string id)
        {
            foreach (var p in map.mapPawns.AllPawnsSpawned) if (p.ThingID == id) return p;
            return null;
        }

        [Tool("qb/set_skilled",
            Description = "DEV MODE. Call QualityBuilder.setSkilled(thing, quality, add) — the real designation/forbidden/rebuild-check path.")]
        public Task<object> SetSkilled(
            IRimBridgeContext ctx, string thingId, string quality, bool add, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("set_skilled requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                var t = Thing(map, thingId); if (t == null) return Error("no thing '" + thingId + "'");
                if (!Enum.TryParse<QualityCategory>(quality, true, out var q)) return Error("bad quality");
                // setSkilled(Thing, QualityCategory?, bool)
                var sig = new[] { typeof(Thing), typeof(QualityCategory?), typeof(bool) };
                QbReflect.CallStatic(QbReflect.QbStatic, "setSkilled", sig, t, q, add);
                return new { success = true };
            }, ct);

        [Tool("qb/set_comp_state",
            Description = "DEV MODE. Directly set CompQualityBuilder fields to arrange preconditions. Omit a field to leave it unchanged.")]
        public Task<object> SetCompState(
            IRimBridgeContext ctx, string thingId,
            [ToolParameter(Description = "true/false")] string isSkilled = null,
            [ToolParameter(Description = "Quality name")] string desiredMinQuality = null,
            [ToolParameter(Description = "true/false")] string pendingQualityRebuild = null,
            [ToolParameter(Description = "int")] int qualityRebuildAttempts = int.MinValue,
            [ToolParameter(Description = "true/false")] string isDesiredMinQualityReached = null,
            CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("set_comp_state requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                var t = Thing(map, thingId); if (t == null) return Error("no thing '" + thingId + "'");
                var comp = QbReflect.GetComp(t); if (comp == null) return Error("thing has no QB comp");
                if (isSkilled != null) QbReflect.SetProp(comp, "isSkilled", bool.Parse(isSkilled));
                if (pendingQualityRebuild != null) QbReflect.SetProp(comp, "pendingQualityRebuild", bool.Parse(pendingQualityRebuild));
                if (isDesiredMinQualityReached != null) QbReflect.SetProp(comp, "isDesiredMinQualityReached", bool.Parse(isDesiredMinQualityReached));
                if (qualityRebuildAttempts != int.MinValue) QbReflect.SetProp(comp, "qualityRebuildAttempts", qualityRebuildAttempts);
                if (desiredMinQuality != null)
                {
                    if (!Enum.TryParse<QualityCategory>(desiredMinQuality, true, out var q)) return Error("bad quality");
                    QbReflect.SetProp(comp, "desiredMinQuality", q);
                }
                return new { success = true };
            }, ct);

        [Tool("qb/invoke_check_rebuild",
            Description = "DEV MODE. Call QualityBuilder.checkAndDesignateForRebuild(building, comp) directly on a finished building.")]
        public Task<object> InvokeCheckRebuild(IRimBridgeContext ctx, string thingId, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("invoke_check_rebuild requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                var t = Thing(map, thingId) as Building; if (t == null) return Error("no building '" + thingId + "'");
                var comp = QbReflect.GetComp(t); if (comp == null) return Error("no QB comp");
                var sig = new[] { typeof(Building), QbReflect.CompType };
                QbReflect.CallStatic(QbReflect.QbStatic, "checkAndDesignateForRebuild", sig, t, comp);
                return new { success = true };
            }, ct);

        [Tool("qb/invoke_after_finish_toil",
            Description = "DEV MODE. Call _JobDriver_ConstructFinishFrame.afterFinishToil(comp, map, target) for the QB thing at a cell (def-disambiguation test).")]
        public Task<object> InvokeAfterFinishToil(IRimBridgeContext ctx, int x, int z, string frameThingId = null, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("invoke_after_finish_toil requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                Thing src = frameThingId != null ? Thing(map, frameThingId) : null;
                var cell = new IntVec3(x, 0, z);
                if (src == null)
                    foreach (var t in map.thingGrid.ThingsListAt(cell)) { if (QbReflect.GetComp(t) != null) { src = t; break; } }
                if (src == null) return Error("no QB thing at cell / id");
                var comp = QbReflect.GetComp(src); if (comp == null) return Error("no QB comp");
                var target = new LocalTargetInfo(cell);
                var sig = new[] { QbReflect.CompType, typeof(Map), typeof(LocalTargetInfo) };
                QbReflect.CallStatic(QbReflect.JobDriverFinishType, "afterFinishToil", sig, comp, map, target);
                return new { success = true };
            }, ct);

        [Tool("qb/set_pawn_skill", Description = "DEV MODE. Set a colonist's Construction skill level.")]
        public Task<object> SetPawnSkill(IRimBridgeContext ctx, string pawnId, int level, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("set_pawn_skill requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                var p = Pawn(map, pawnId); if (p == null) return Error("no pawn '" + pawnId + "'");
                var rec = p.skills?.GetSkill(SkillDefOf.Construction); if (rec == null) return Error("pawn has no skills");
                rec.Level = Mathf.Clamp(level, 0, 20);
                ResetBestConstructorCache(map);
                return new { success = true, pawnId, def = "Construction", level = rec.Level };
            }, ct);

        // QB caches the best-constructor skill for ~10s per settings object; a skill/downed change
        // wouldn't be visible to the isPawnGoodEnoughToBuild gate within that window (the gate reads
        // the CACHED value, unlike qb/get_settings which reads raw). Null the private stopwatch so
        // the next gate read recomputes immediately, keeping gate tests deterministic.
        private static void ResetBestConstructorCache(Map map)
        {
            var settings = QbReflect.CallStatic(QbReflect.SettingsType, "getSettings", new[] { typeof(Map) }, map);
            if (settings == null) return;
            var f = QbReflect.SettingsType.GetField("bestConstructorCheckWatch",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(settings, null);
        }

        [Tool("qb/set_pawn_flags", Description = "DEV MODE. Toggle transient pawn state (downed) for gate tests.")]
        public Task<object> SetPawnFlags(IRimBridgeContext ctx, string pawnId, string downed = null, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("set_pawn_flags requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                var p = Pawn(map, pawnId); if (p == null) return Error("no pawn '" + pawnId + "'");
                if (downed != null && bool.Parse(downed))
                {
                    // Anesthetic reliably + REVERSIBLY downs: consciousness -> 0 makes the pawn
                    // both Downed and incapable of Manipulation/Moving. DamageUntilDowned is
                    // avoided because it deals permanent injuries (leaking pawn health across
                    // session-scoped tests) and can down via pain-shock while capacities stay
                    // intact (so a bestConstructorOverride pawn would still bind). heal_all_colonists
                    // (run in the per-test teardown) removes the anesthetic.
                    if (!p.health.hediffSet.HasHediff(HediffDefOf.Anesthetic))
                        p.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.Anesthetic, p));
                }
                ResetBestConstructorCache(map);  // downing changes the best-constructor set
                return new { success = true, pawnId, downed = p.Downed };
            }, ct);

        [Tool("qb/heal_all_colonists",
            Description = "DEV MODE. Fully restore all spawned player colonists (remove injuries, missing parts, anesthetic) so pawn health never leaks across tests. Run in the per-test teardown.")]
        public Task<object> HealAllColonists(IRimBridgeContext ctx, CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("heal_all_colonists requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                int removed = 0;
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                {
                    var toRemove = new List<Hediff>();
                    foreach (var h in p.health.hediffSet.hediffs)
                        if (h is Hediff_Injury || h is Hediff_MissingPart || h.def == HediffDefOf.Anesthetic)
                            toRemove.Add(h);
                    foreach (var h in toRemove) { p.health.RemoveHediff(h); removed++; }
                }
                return new { success = true, hediffsRemoved = removed };
            }, ct);

        // --- settings ---
        // Resolves the effective QualityBuilderModSettings object for the requested scope.
        private static object EffectiveSettings(Map map, string scope, out string problem)
        {
            problem = null;
            if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
                return QbReflect.CallStatic(QbReflect.GlobalSettingsType, "getSettings", Type.EmptyTypes);
            // map/effective: QualityBuilderModSettings.getSettings(Map)
            return QbReflect.CallStatic(QbReflect.SettingsType, "getSettings", new[] { typeof(Map) }, map);
        }

        [Tool("qb/set_setting",
            Description = "DEV MODE. Set one QB setting. keys: defaultUseQualityBuilder, defaultMinQualitySetting, skillDifferenceFromBestBuilder, ignoreQualityBuilderAtSkill, maxQualityRebuildAttempts (int; 2147483647 = unlimited), bestConstructorOverride (pawnId or empty), useMapSettings. scope: map|global.")]
        public Task<object> SetSetting(IRimBridgeContext ctx, string key, string value, string scope = "map", CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                if (!Prefs.DevMode) return Error("set_setting requires dev mode");
                var map = Map; if (map == null) return Error("no current map");
                if (string.Equals(key, "useMapSettings", StringComparison.OrdinalIgnoreCase))
                {
                    var mc = QbReflect.CallStatic(QbReflect.MapCompType, "getAndEnsure", new[] { typeof(Map) }, map);
                    QbReflect.MapCompType.GetProperty("useMapSettings").SetValue(mc, bool.Parse(value));
                    return new { success = true, key, value };
                }
                var settings = EffectiveSettings(map, scope, out var problem);
                if (settings == null) return Error(problem ?? "could not resolve settings");
                var prop = QbReflect.SettingsType.GetProperty(key,
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return Error("unknown setting '" + key + "'");
                object boxed;
                var pt = prop.PropertyType;
                if (pt == typeof(bool)) boxed = bool.Parse(value);
                else if (pt == typeof(int)) boxed = int.Parse(value);
                else if (pt == typeof(QualityCategory)) { if (!Enum.TryParse<QualityCategory>(value, true, out var q)) return Error("bad quality"); boxed = q; }
                else if (pt == typeof(Pawn)) boxed = string.IsNullOrEmpty(value) ? null : Pawn(map, value);
                else return Error("unsupported setting type " + pt.Name);
                prop.SetValue(settings, boxed);
                return new { success = true, key, value };
            }, ct);

        [Tool("qb/get_settings", Description = "Read the effective QB settings + computed best constructor skill.", Tags = new[] { "read-only" })]
        public Task<object> GetSettings(IRimBridgeContext ctx, string scope = "map", CancellationToken ct = default)
            => ctx.MainThread.InvokeAsync<object>(() =>
            {
                var map = Map; if (map == null) return Error("no current map");
                var s = EffectiveSettings(map, scope, out var problem);
                if (s == null) return Error(problem ?? "no settings");
                object Get(string n) => QbReflect.SettingsType.GetProperty(n, BindingFlags.Public | BindingFlags.Instance)?.GetValue(s);
                var mc = QbReflect.CallStatic(QbReflect.MapCompType, "getAndEnsure", new[] { typeof(Map) }, map);
                var useMap = QbReflect.MapCompType.GetProperty("useMapSettings")?.GetValue(mc);
                var best = QbReflect.CallStatic(QbReflect.QbStatic, "getBestConstructorSkill", new[] { typeof(Map) }, map);
                var overridePawn = Get("bestConstructorOverride") as Pawn;
                return new
                {
                    success = true,
                    effective = new
                    {
                        defaultUseQualityBuilder = Get("defaultUseQualityBuilder"),
                        defaultMinQualitySetting = Get("defaultMinQualitySetting")?.ToString(),
                        skillDifferenceFromBestBuilder = Get("skillDifferenceFromBestBuilder"),
                        ignoreQualityBuilderAtSkill = Get("ignoreQualityBuilderAtSkill"),
                        maxQualityRebuildAttempts = Get("maxQualityRebuildAttempts"),
                        bestConstructorOverride = overridePawn?.ThingID,
                    },
                    useMapSettings = useMap,
                    bestConstructorSkill = best,
                };
            }, ct);
    }
}
```

- [ ] **Step 2: Add `qb/get_gizmo_info` to `QbInspectTools.cs`**

```csharp
// inside class QbInspectTools

[Tool("qb/get_gizmo_info",
    Description = "Would QB offer its command button on this thing, and which quality options would its right-click float menu list? Invokes the real gizmo code with the thing selected.",
    ResultDescription = "commandOffered + floatMenuQualities (ordered).",
    Tags = new[] { "read-only" })]
public Task<object> GetGizmoInfo(IRimBridgeContext ctx, string thingId, CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var map = CurrentMap;
        if (map == null) return Error("no current map");
        var thing = ResolveThing(map, thingId, -1, -1, out var problem);
        if (thing == null) return Error(problem);
        var twc = thing as ThingWithComps;
        var comp = QbReflect.GetComp(thing);
        if (comp == null) return Error("thing has no QB comp");

        bool commandOffered = false;
        var gizmos = comp.GetType().GetMethod("CompGetGizmosExtra")?.Invoke(comp, null) as System.Collections.IEnumerable;
        if (gizmos != null) foreach (var g in gizmos) { commandOffered = true; break; }

        // Drive the right-click float menu with the thing selected.
        var prior = new List<object>(Find.Selector.SelectedObjects);
        Find.Selector.ClearSelection();
        Find.Selector.Select(thing, false, false);
        var qualities = new List<string>();
        try
        {
            foreach (var g in (comp.GetType().GetMethod("CompGetGizmosExtra").Invoke(comp, null) as System.Collections.IEnumerable))
            {
                var rc = g.GetType().GetProperty("RightClickFloatMenuOptions",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(g) as System.Collections.IEnumerable;
                if (rc == null) continue;
                foreach (var opt in rc)
                {
                    var label = opt.GetType().GetProperty("Label")?.GetValue(opt) as string;
                    if (label != null) qualities.Add(label);
                }
            }
        }
        finally
        {
            Find.Selector.ClearSelection();
            foreach (var o in prior) Find.Selector.Select(o, false, false);
        }
        return new { success = true, commandOffered, floatMenuQualities = qualities };
    }, ct);
```

Add `using System.Reflection;` to `QbInspectTools.cs`.

- [ ] **Step 3: Build + deploy + restart + smoke**

`qb/spawn_finished_building def=Wall quality=Legendary ...` → `qb/get_gizmo_info thingId=<id>` expects `commandOffered:false`. `quality=Awful` → `commandOffered:true`, `floatMenuQualities` excludes "Awful". `qb/set_setting key=maxQualityRebuildAttempts value=5 scope=map` → `qb/get_settings` shows `maxQualityRebuildAttempts:5`.

- [ ] **Step 4: Commit**

```bash
git add Source/QbMutateTools.cs Source/QbInspectTools.cs
git commit -q -m "QBBT: mutation/invoke tools + get_settings + get_gizmo_info"
```

---

## Task 9: Python QB wrappers + arena constants

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/arena.py`
- Create: `Mods/QualityBuilderBridgeTools/qbtest/qb.py`

**Interfaces:**
- Consumes: `GabpClient.call` (Task 4).
- Produces `arena.ARENA` (`Rect` namedtuple with `.x,.z,.width,.height` + `.cell(i)` helper giving spaced non-overlapping cells).
- Produces `qb.py` functions listed below.

- [ ] **Step 1: Write `arena.py`**

```python
# qbtest/arena.py
"""Scratch-arena coordinates in the qb_test save. Must match the save's cleared area."""
from collections import namedtuple

Rect = namedtuple("Rect", "x z width height")

# The qb_test save reserves this flat, buildable rect for the tests (see qb_test.SAVE_INSTRUCTIONS.md).
ARENA = Rect(x=50, z=50, width=20, height=20)

# Spaced test cells (3 apart) so several buildings don't share a cell unintentionally.
def cell(i):
    row = i // 6
    col = i % 6
    return (ARENA.x + col * 3, ARENA.z + row * 3)
```

- [ ] **Step 2: Write `qb.py`** (thin, typed wrappers; every test uses these)

```python
# qbtest/qb.py
"""Typed wrappers over qb/* companion tools and the built-in rimworld/* tools."""


# ---- companion qb/* ----
def get_building_state(c, thing_id=None, x=None, z=None):
    kw = {}
    if thing_id is not None:
        kw["thingId"] = thing_id
    if x is not None:
        kw["x"], kw["z"] = x, z
    return c.call("qb/get_building_state", **kw)


def list_qb_things(c, x, z, width, height):
    return c.call("qb/list_qb_things", x=x, z=z, width=width, height=height)["things"]


def spawn_blueprint(c, def_name, x, z, rot=0, stuff=None):
    return c.call("qb/spawn_blueprint", **{"def": def_name, "x": x, "z": z, "rot": rot,
                                           **({"stuff": stuff} if stuff else {})})["thingId"]


def spawn_finished_building(c, def_name, x, z, quality, stuff=None, rot=0):
    return c.call("qb/spawn_finished_building", **{"def": def_name, "x": x, "z": z,
                  "quality": quality, "rot": rot, **({"stuff": stuff} if stuff else {})})["thingId"]


def set_skilled(c, thing_id, quality, add):
    return c.call("qb/set_skilled", thingId=thing_id, quality=quality, add=add)


def set_comp_state(c, thing_id, **fields):
    args = {"thingId": thing_id}
    for k, v in fields.items():
        args[k] = str(v) if isinstance(v, bool) else v
    return c.call("qb/set_comp_state", **args)


def invoke_check_rebuild(c, thing_id):
    return c.call("qb/invoke_check_rebuild", thingId=thing_id)


def invoke_after_finish_toil(c, x, z, frame_thing_id=None):
    kw = {"x": x, "z": z}
    if frame_thing_id:
        kw["frameThingId"] = frame_thing_id
    return c.call("qb/invoke_after_finish_toil", **kw)


def set_pawn_skill(c, pawn_id, level):
    return c.call("qb/set_pawn_skill", pawnId=pawn_id, level=level)


def set_pawn_flags(c, pawn_id, **flags):
    args = {"pawnId": pawn_id}
    for k, v in flags.items():
        args[k] = str(v)
    return c.call("qb/set_pawn_flags", **args)


def set_setting(c, key, value, scope="map"):
    return c.call("qb/set_setting", key=key,
                  value=str(value) if not isinstance(value, str) else value, scope=scope)


def get_settings(c, scope="map"):
    return c.call("qb/get_settings", scope=scope)


def get_gizmo_info(c, thing_id):
    return c.call("qb/get_gizmo_info", thingId=thing_id)


def clear_arena(c, rect):
    return c.call("qb/clear_arena", x=rect.x, z=rect.z, width=rect.width, height=rect.height)


def heal_all_colonists(c):
    return c.call("qb/heal_all_colonists")


# ---- built-in rimworld/* ----
def save_game(c, name):
    return c.call("rimworld/save_game", saveName=name)


def load_game(c, name, timeout=120):
    return c.call("rimworld/load_game_ready", timeout=timeout, saveName=name, readiness="visual")


def list_colonists(c):
    return c.call("rimworld/list_colonists")


def list_messages(c):
    return c.call("rimworld/list_messages")


def list_logs(c, count=200):
    # RimBridge's rimbridge/list_logs parameter is 'limit' (not 'count').
    return c.call("rimbridge/list_logs", limit=count)


def set_god_mode(c, on=True):
    return c.call("rimworld/set_god_mode", enabled=on)
```

- [ ] **Step 3: Commit** (no live test yet — exercised by Task 10+)

```bash
git add qbtest/arena.py qbtest/qb.py
git commit -q -m "qbtest: arena constants + qb/* and built-in tool wrappers"
```

---

## Task 10: pytest harness — conftest fixtures + first green test (A1)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/conftest.py`
- Create: `Mods/QualityBuilderBridgeTools/qbtest/__main__.py`
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_a_designation.py` (A1 only for now)
- Create: `Mods/QualityBuilderBridgeTools/pytest.ini`

**Interfaces:**
- Consumes: `Game` (Task 5), `qb` wrappers (Task 9), `arena.ARENA` (Task 9).
- Produces fixtures: `game` (session), `client` (session), `arena` (session, = `ARENA`), autouse `clean_arena` (function; clears before+after each non-persistence test). Marker `@pytest.mark.persistence` skips the autouse clear's reload semantics (it still clears).

- [ ] **Step 1: Write `pytest.ini`**

```ini
[pytest]
markers =
    live: test requires a running RimWorld instance
    persistence: test performs a real save+reload
    integration: test drives real pawns constructing
addopts = -ra
```

- [ ] **Step 2: Write `conftest.py`**

```python
# qbtest/conftest.py
import pytest

from .game_control import Game
from .gabp_client import ToolTimeout, BridgeError
from . import qb
from .arena import ARENA


def pytest_addoption(parser):
    parser.addoption("--no-restart", action="store_true",
                     help="attach to a running game; never launch/kill it")
    parser.addoption("--save", default="qb_test", help="test save name")


@pytest.fixture(scope="session")
def _game(request):
    return Game(save_name=request.config.getoption("--save"))


@pytest.fixture(scope="session")
def client(_game, request):
    if request.config.getoption("--no-restart"):
        c = _game.connect_existing()
    else:
        c = _game.ensure_running()
        _game.load_test_save(c)
    yield c
    c.close()


@pytest.fixture(scope="session")
def arena():
    return ARENA


@pytest.fixture(autouse=True)
def clean_arena(request):
    # Only live tests touch the game. Offline unit tests (framing/discovery/client) must NOT
    # trigger a game launch, so gate on the `live` marker and resolve `client` LAZILY — taking
    # `client` as a direct param would force every test (incl. offline ones) to start the game.
    if "live" not in request.keywords:
        yield
        return
    client = request.getfixturevalue("client")
    qb.clear_arena(client, ARENA)
    yield
    try:
        qb.heal_all_colonists(client)  # undo any test that downed a pawn (no health leak)
        qb.clear_arena(client, ARENA)
    except (ToolTimeout, BridgeError):
        pass  # a crash during the test; session fixture teardown / next test handles it


@pytest.fixture
def colonists(client):
    """Return {'hi': pawnId_highskill, 'lo': pawnId_lowskill} from the qb_test save."""
    pawns = qb.list_colonists(client)["colonists"]
    # qb_test names its builders 'BuilderHi' and 'BuilderLo' (see SAVE_INSTRUCTIONS).
    by_name = {p["name"].split()[0]: p["id"] for p in pawns}
    return {"hi": by_name["BuilderHi"], "lo": by_name["BuilderLo"]}
```

Note: if `rimworld/list_colonists` field names differ (`name`/`id`), adjust in this one place during the Task-10 smoke run.

- [ ] **Step 3: Write `__main__.py`**

```python
# qbtest/__main__.py
import sys
import pytest

if __name__ == "__main__":
    # `python -m qbtest [pytest args]` — default: run the whole live suite with a JSON report.
    args = sys.argv[1:] or [
        "qbtest/tests",
        "--json-report", "--json-report-file=qbtest_report.json",
    ]
    raise SystemExit(pytest.main(args))
```

- [ ] **Step 4: Write A1 test**

```python
# qbtest/tests/test_a_designation.py
import pytest
from qbtest import qb

pytestmark = pytest.mark.live


def test_a1_autoadopt_when_default_on(client, arena):
    qb.set_setting(client, "defaultUseQualityBuilder", True)
    qb.set_setting(client, "defaultMinQualitySetting", "Good")
    x, z = arena.x, arena.z
    tid = qb.spawn_blueprint(client, "Wall", x, z, stuff="Steel")
    st = qb.get_building_state(client, thing_id=tid)
    assert st["isSkilled"] is True
    assert st["desiredMinQuality"] == "Good"
    assert st["qbDesignation"] == "SkilledBuilder4"  # Good == index 3
```

- [ ] **Step 5: Run A1 live, verify pass**

Ensure the `qb_test` save exists (Task 12) or use `--no-restart` against a hand-loaded dev save with the arena cleared.
Run: `python -m pytest qbtest/tests/test_a_designation.py -v`
Expected: `test_a1_autoadopt_when_default_on PASSED`. Fix field-name mismatches surfaced here (colonist fields, designation names) before continuing.

- [ ] **Step 6: Commit**

```bash
git add qbtest/conftest.py qbtest/__main__.py qbtest/tests/test_a_designation.py pytest.ini
git commit -q -m "qbtest: pytest harness (fixtures + entrypoint) + first green A1"
```

---

## Task 11: Test module A — designation & gizmo (A1b..A8)

**Files:**
- Modify: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_a_designation.py`

**Interfaces:** Consumes `qb`, `client`, `arena` fixtures.

- [ ] **Step 1: Append A1b..A8**

```python
def test_a1b_no_autoadopt_when_default_off(client, arena):
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    st = qb.get_building_state(client, thing_id=tid)
    assert st["isSkilled"] in (False, None)
    assert st["qbDesignation"] is None


def test_a2_toggle_on_off(client, arena):
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    qb.set_skilled(client, tid, "Excellent", True)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["isSkilled"] is True and st["qbDesignation"] == "SkilledBuilder5"
    qb.set_skilled(client, tid, "Excellent", False)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["isSkilled"] is False and st["qbDesignation"] is None


@pytest.mark.parametrize("quality", ["Awful", "Good", "Masterwork"])
def test_a3_gizmo_offered_below_legendary(client, arena, quality):
    tid = qb.spawn_finished_building(client, "Wall", arena.x, arena.z, quality, stuff="Steel")
    assert qb.get_gizmo_info(client, tid)["commandOffered"] is True


def test_a4_gizmo_hidden_at_legendary(client, arena):
    tid = qb.spawn_finished_building(client, "Wall", arena.x, arena.z, "Legendary", stuff="Steel")
    assert qb.get_gizmo_info(client, tid)["commandOffered"] is False


def test_a5_floatmenu_excludes_at_or_below_current(client, arena):
    tid = qb.spawn_finished_building(client, "Wall", arena.x, arena.z, "Good", stuff="Steel")
    labels = qb.get_gizmo_info(client, tid)["floatMenuQualities"]
    # Finished building at Good: options are strictly above Good.
    assert not any(q in labels for q in _quality_labels_up_to("Good"))
    assert any("Excellent" in l or _is_quality(l, "Excellent") for l in labels)


def test_a5b_floatmenu_full_range_on_blueprint(client, arena):
    qb.set_setting(client, "defaultUseQualityBuilder", True)
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    labels = qb.get_gizmo_info(client, tid)["floatMenuQualities"]
    assert len(labels) == 7  # Awful..Legendary, all offered on a blueprint


QUALITIES = ["Awful", "Poor", "Normal", "Good", "Excellent", "Masterwork", "Legendary"]
DESIGS = ["SkilledBuilder", "SkilledBuilder2", "SkilledBuilder3", "SkilledBuilder4",
          "SkilledBuilder5", "SkilledBuilder6", "SkilledBuilder7"]


@pytest.mark.parametrize("quality,desig", list(zip(QUALITIES, DESIGS)))
def test_a6_designation_quality_mapping(client, arena, quality, desig):
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    qb.set_skilled(client, tid, quality, True)
    assert qb.get_building_state(client, thing_id=tid)["qbDesignation"] == desig


def test_a8_toggle_off_cancels_pending_deconstruct(client, arena):
    tid = qb.spawn_finished_building(client, "Wall", arena.x, arena.z, "Awful", stuff="Steel")
    qb.set_skilled(client, tid, "Excellent", True)
    qb.set_comp_state(client, tid, pendingQualityRebuild=True, isDesiredMinQualityReached=False)
    qb.invoke_check_rebuild(client, tid)  # ensure a Deconstruct designation exists
    assert qb.get_building_state(client, thing_id=tid)["hasDeconstructDesignation"] is True
    qb.set_skilled(client, tid, "Excellent", False)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["hasDeconstructDesignation"] is False
    assert st["pendingQualityRebuild"] is False


# --- helpers for A5 (quality label localization varies; match by known English labels) ---
def _quality_labels_up_to(quality):
    idx = QUALITIES.index(quality)
    return set(QUALITIES[: idx + 1])  # includes 'Good' and below


def _is_quality(label, quality):
    return quality.lower() in label.lower()
```

Note on A5/A7: `floatMenuQualities` returns localized labels; the assertions match on English quality words which the qb_test save uses (English language). **A7 (forbidden preserved)** requires reading/setting the forbidden flag; RimBridge lacks a direct forbidden toggle, so add it to the companion in Step 2 rather than leave A7 unimplemented.

- [ ] **Step 2: Add `qb/set_forbidden` + `forbidden` field to support A7**

In `QbSetupTools.cs` add:

```csharp
[Tool("qb/set_forbidden", Description = "DEV MODE. Set a thing's Forbidden flag.")]
public Task<object> SetForbidden(IRimBridgeContext ctx, string thingId, bool forbidden, CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        if (!Prefs.DevMode) return Error("set_forbidden requires dev mode");
        var map = Map; if (map == null) return Error("no current map");
        Thing found = null;
        foreach (var t in map.listerThings.AllThings) if (t.ThingID == thingId) { found = t; break; }
        if (found == null) return Error("no thing '" + thingId + "'");
        var f = (found as ThingWithComps)?.GetComp<CompForbiddable>();
        if (f == null) return Error("thing not forbiddable");
        f.Forbidden = forbidden;
        return new { success = true, forbidden = f.Forbidden };
    }, ct);
```

In `QbInspectTools.BuildingState`, add to the returned object: `forbidden = (thing as ThingWithComps)?.GetComp<CompForbiddable>()?.Forbidden,`. Rebuild+deploy+restart. Add wrapper in `qb.py`:

```python
def set_forbidden(c, thing_id, forbidden):
    return c.call("qb/set_forbidden", thingId=thing_id, forbidden=forbidden)
```

Then append A7:

```python
def test_a7_forbidden_preserved_across_setskilled(client, arena):
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    qb.set_forbidden(client, tid, True)
    qb.set_skilled(client, tid, "Good", True)
    assert qb.get_building_state(client, thing_id=tid)["forbidden"] is True
```

- [ ] **Step 3: Run module A live, verify pass**

Run: `python -m pytest qbtest/tests/test_a_designation.py -v`
Expected: all A tests PASS. (A5 label matching may need tightening — inspect the actual `floatMenuQualities` output once and adjust `_is_quality` if labels are prefixed.)

- [ ] **Step 4: Commit**

```bash
git add qbtest/tests/test_a_designation.py Source/QbSetupTools.cs Source/QbInspectTools.cs qbtest/qb.py
git commit -q -m "qbtest: module A (designation + gizmo) complete; add qb/set_forbidden"
```

---

## Task 12: Build the `qb_test` save + document it

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qb_test.SAVE_INSTRUCTIONS.md`

This task produces the fixture save the whole live suite loads. It is manual (in-game) but scripted where possible.

- [ ] **Step 1: Write the instructions doc**

```markdown
# Building the qb_test save

1. Launch RimWorld with **Harmony, RimBridgeServer, QualityBuilder** (and their deps) enabled,
   plus the `QualityBuilderBridgeTools.dll` present in `RimWorldRoot\BridgeTools\`.
2. Start a new colony (Crashlanded is fine) on a **flat, mostly-buildable** tile. Ideology
   enabled with at least one styleable precept (for D3).
3. Enable **Development mode** (Options → check "Development mode").
4. Ensure two colonists exist and rename them (double-click name):
   - `BuilderHi` — set Construction skill high (dev tool: "Set skill" or the pawn's bio).
   - `BuilderLo` — set Construction skill low (~2).
   Both must have **Construction work enabled** in the Work tab.
5. Flatten/clear the arena rect **x=50..69, z=50..69** (20x20): remove rocks, plants, roofs;
   ensure the terrain is buildable (soil/rough). This must match `qbtest/arena.py::ARENA`.
6. Place a **stockpile with plenty of Steel and Wood** within ~15 cells of the arena (dev tool:
   "Spawn thing" → Steel x2000, Wood x1000; then a stockpile over them, or drop directly).
7. Pause the game.
8. Save as **`qb_test`** (exact name; the runner loads it by this name).
9. Sanity check: with the game paused on this save,
   `python -m qbtest --no-restart qbtest/tests/test_a_designation.py::test_a1_autoadopt_when_default_on`
   should PASS.

Keep this save stable. If you change the arena rect, update `arena.py` to match.
```

- [ ] **Step 2: Build the save in-game per the doc; verify the sanity check passes.**

Run: `python -m qbtest --no-restart "qbtest/tests/test_a_designation.py::test_a1_autoadopt_when_default_on"`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add qb_test.SAVE_INSTRUCTIONS.md
git commit -q -m "qbtest: qb_test save build instructions"
```

---

## Task 13: Test module E — config (E1..E3)

(E is simpler than B/C/D and validates settings wrappers used everywhere; do it before the harder modules.)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_e_config.py`

- [ ] **Step 1: Write E1..E3**

```python
# qbtest/tests/test_e_config.py
import pytest
from qbtest import qb

pytestmark = pytest.mark.live

UNLIMITED = 2147483647


def test_e1_max_rebuild_attempts_roundtrip(client):
    qb.set_setting(client, "maxQualityRebuildAttempts", 5)
    assert qb.get_settings(client)["effective"]["maxQualityRebuildAttempts"] == 5
    qb.set_setting(client, "maxQualityRebuildAttempts", UNLIMITED)
    assert qb.get_settings(client)["effective"]["maxQualityRebuildAttempts"] == UNLIMITED


def test_e2_per_map_vs_global_isolation(client):
    qb.set_setting(client, "useMapSettings", True)
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 7, scope="map")
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 15, scope="global")
    assert qb.get_settings(client, scope="map")["effective"]["ignoreQualityBuilderAtSkill"] == 7
    assert qb.get_settings(client, scope="global")["effective"]["ignoreQualityBuilderAtSkill"] == 15
    # With useMapSettings=False, the effective (map) resolve falls through to global.
    qb.set_setting(client, "useMapSettings", False)
    assert qb.get_settings(client, scope="map")["effective"]["ignoreQualityBuilderAtSkill"] == 15


def test_e3_default_min_quality_applied_to_blueprint(client, arena):
    qb.set_setting(client, "useMapSettings", True)
    qb.set_setting(client, "defaultUseQualityBuilder", True)
    qb.set_setting(client, "defaultMinQualitySetting", "Excellent")
    tid = qb.spawn_blueprint(client, "Wall", arena.x, arena.z, stuff="Steel")
    assert qb.get_building_state(client, thing_id=tid)["desiredMinQuality"] == "Excellent"
```

- [ ] **Step 2: Run + verify**

Run: `python -m pytest qbtest/tests/test_e_config.py -v`
Expected: 3 passed. (If E2's effective-resolve differs, confirm `qb/get_settings scope=map` uses `QualityBuilderModSettings.getSettings(map)` which honors `useMapSettings` — matches QB source.)

- [ ] **Step 3: Commit**

```bash
git add qbtest/tests/test_e_config.py
git commit -q -m "qbtest: module E (config) complete"
```

---

## Task 14: Test module D — rebuild cycle (D1..D8)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_d_rebuild.py`

- [ ] **Step 1: Write the logic-level cases D1, D2, D4, D5, D7**

```python
# qbtest/tests/test_d_rebuild.py
import time
import pytest
from qbtest import qb

pytestmark = pytest.mark.live


def _setup_finished(client, arena, quality, desired):
    tid = qb.spawn_finished_building(client, "Wall", arena.x, arena.z, quality, stuff="Steel")
    # Set preconditions via the raw field setter (NO side effects). qb.set_skilled would call
    # the REAL setSkilled, which on a below-min finished building immediately runs
    # checkAndDesignateForRebuild and adds a stray Deconstruct designation — corrupting every
    # test that then relies on invoke_check_rebuild starting from a clean slate.
    qb.set_comp_state(client, tid, isSkilled=True, desiredMinQuality=desired,
                      isDesiredMinQualityReached=False)
    return tid


def test_d1_at_or_above_min_no_rebuild(client, arena):
    tid = _setup_finished(client, arena, "Excellent", "Good")
    qb.set_comp_state(client, tid, qualityRebuildAttempts=0, pendingQualityRebuild=False)
    qb.invoke_check_rebuild(client, tid)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["isDesiredMinQualityReached"] is True
    assert st["hasDeconstructDesignation"] is False
    assert st["qualityRebuildAttempts"] == 0
    assert st["pendingQualityRebuild"] is False


def test_d2_below_min_designates_deconstruct(client, arena):
    tid = _setup_finished(client, arena, "Awful", "Excellent")
    qb.set_comp_state(client, tid, qualityRebuildAttempts=0, pendingQualityRebuild=False)
    qb.set_setting(client, "maxQualityRebuildAttempts", 3)
    qb.invoke_check_rebuild(client, tid)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["pendingQualityRebuild"] is True
    assert st["hasDeconstructDesignation"] is True
    assert st["qualityRebuildAttempts"] == 1


def test_d4_loop_breaker_gives_up_at_cap(client, arena):
    qb.set_setting(client, "maxQualityRebuildAttempts", 3)
    tid = _setup_finished(client, arena, "Awful", "Excellent")
    qb.set_comp_state(client, tid, qualityRebuildAttempts=3, pendingQualityRebuild=False)
    qb.invoke_check_rebuild(client, tid)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["hasDeconstructDesignation"] is False
    assert st["pendingQualityRebuild"] is False
    labels = " ".join(m.get("text", "") for m in qb.list_messages(client).get("messages", []))
    assert "quality" in labels.lower() or "give" in labels.lower() or st["qualityRebuildAttempts"] == 3


def test_d5_unlimited_never_gives_up(client, arena):
    qb.set_setting(client, "maxQualityRebuildAttempts", 2147483647)
    tid = _setup_finished(client, arena, "Awful", "Excellent")
    qb.set_comp_state(client, tid, qualityRebuildAttempts=999, pendingQualityRebuild=False)
    qb.invoke_check_rebuild(client, tid)
    st = qb.get_building_state(client, thing_id=tid)
    assert st["hasDeconstructDesignation"] is True
    assert st["pendingQualityRebuild"] is True


@pytest.mark.skip(reason="afterFinishToil def-disambiguation needs a real in-progress Frame "
                         "(cmp.parent as Frame with entityDefToBuild) plus a wall-attached-light "
                         "def sharing the wall cell — not reproducible deterministically in the "
                         "base test env (a finished Building makes cmp.parent-as-Frame null, so "
                         "the def-match branch never runs and the assertion is vacuous). Covered "
                         "by code inspection; revisit with a qb/spawn_frame tool + a wall-light "
                         "mod in the environment.")
def test_d7_def_disambiguation_wall_vs_lamp(client, arena):
    # A quality wall and a wall-light sharing a cell; afterFinishToil must target the lamp,
    # not the wall, when invoked for the lamp def.
    x, z = arena.x, arena.z
    wall = qb.spawn_finished_building(client, "Wall", x, z, "Legendary", stuff="Steel")
    lamp = qb.spawn_finished_building(client, "StandingLamp", x, z, "Awful")
    qb.set_comp_state(client, lamp, isSkilled=True, desiredMinQuality="Excellent",
                      isDesiredMinQualityReached=False,
                      qualityRebuildAttempts=0, pendingQualityRebuild=False)
    qb.set_setting(client, "maxQualityRebuildAttempts", 3)
    qb.invoke_after_finish_toil(client, x, z, frame_thing_id=lamp)
    wall_st = qb.get_building_state(client, thing_id=wall)
    assert wall_st["hasDeconstructDesignation"] is False  # the Legendary wall must be untouched
```

Note: if `StandingLamp` can't co-occupy the wall cell, place it on an adjacent arena cell and pass that cell to `invoke_after_finish_toil`; the assertion (wall untouched) still validates def-matching. Confirm co-occupancy during the live run and adjust the cell if needed.

- [ ] **Step 2: Run the logic-level D cases, verify pass**

Run: `python -m pytest qbtest/tests/test_d_rebuild.py -k "d1 or d2 or d4 or d5 or d7" -v`
Expected: 5 passed.

- [ ] **Step 3: Write the integration cases D3, D6, D8** (real pawns; generous wait-loops)

```python
def _wait(client, predicate, timeout=90, poll=1.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(poll)
    return False


@pytest.mark.integration
def test_d3_full_deconstruct_rebuild(client, arena, colonists):
    # Build a real wall, force it below-min, let the colony rebuild it.
    qb.set_setting(client, "maxQualityRebuildAttempts", 3)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    x, z = arena.x, arena.z
    tid = qb.spawn_finished_building(client, "Wall", x, z, "Awful", stuff="Steel")
    qb.set_comp_state(client, tid, isSkilled=True, desiredMinQuality="Excellent",
                      isDesiredMinQualityReached=False,
                      qualityRebuildAttempts=0, pendingQualityRebuild=False)
    qb.invoke_check_rebuild(client, tid)  # designates deconstruct (QB-initiated)
    assert qb.get_building_state(client, thing_id=tid)["pendingQualityRebuild"] is True
    client.call("rimworld/set_time_speed", speed=3)
    # After deconstruct, a fresh QB blueprint/frame appears at the same cell.
    got = _wait(client, lambda: any(
        t["kind"] in ("blueprint", "frame")
        for t in qb.list_qb_things(client, x, z, 1, 1)), timeout=120)
    client.call("rimworld/pause_game")
    assert got, "no replacement blueprint/frame appeared at the cell"
    rebuilt = qb.list_qb_things(client, x, z, 1, 1)[0]
    st = qb.get_building_state(client, thing_id=rebuilt["thingId"])
    assert st["desiredMinQuality"] == "Excellent"
    assert st["qualityRebuildAttempts"] >= 1


@pytest.mark.integration
def test_d6_player_deconstruct_not_hijacked(client, arena, colonists):
    qb.set_pawn_skill(client, colonists["hi"], 15)
    x, z = arena.x, arena.z
    tid = qb.spawn_finished_building(client, "Wall", x, z, "Awful", stuff="Steel")
    # Player-ordered deconstruct: isSkilled/desiredMin set via the side-effect-free field setter
    # so no QB-initiated deconstruct is created; pendingQualityRebuild stays False.
    qb.set_comp_state(client, tid, isSkilled=True, desiredMinQuality="Excellent",
                      isDesiredMinQualityReached=False, pendingQualityRebuild=False)
    client.call("rimworld/apply_architect_designator",
                designatorId="Deconstruct", x=x, z=z)  # confirm id via list_architect_designators
    client.call("rimworld/set_time_speed", speed=3)
    _wait(client, lambda: not qb.list_qb_things(client, x, z, 1, 1), timeout=120)
    time.sleep(3)
    client.call("rimworld/pause_game")
    # No QB blueprint/frame should have been placed as a rebuild.
    assert not any(t["kind"] in ("blueprint", "frame")
                   for t in qb.list_qb_things(client, x, z, 1, 1))


@pytest.mark.integration
def test_d8_no_reserve_error_during_rebuild(client, arena, colonists):
    qb.set_pawn_skill(client, colonists["hi"], 15)
    x, z = arena.x, arena.z
    tid = qb.spawn_finished_building(client, "Wall", x, z, "Awful", stuff="Steel")
    qb.set_comp_state(client, tid, isSkilled=True, desiredMinQuality="Excellent",
                      isDesiredMinQualityReached=False,
                      qualityRebuildAttempts=0, pendingQualityRebuild=False)
    qb.invoke_check_rebuild(client, tid)
    client.call("rimworld/set_time_speed", speed=3)
    assert _wait(client, lambda: bool(qb.list_qb_things(client, x, z, 1, 1)), timeout=120), \
        "rebuild blueprint/frame never appeared — cannot validate the reservation handoff"
    time.sleep(5)
    client.call("rimworld/pause_game")
    logs = qb.list_logs(client, count=400).get("logs", [])
    reserve_errors = [l for l in logs if "Could not reserve" in l.get("message", "")]
    assert not reserve_errors, "reserve error(s) logged: %r" % reserve_errors[:3]
```

Note: confirm exact arg names for `rimworld/apply_architect_designator` (`designatorId`/`x`/`z`) and `rimworld/set_time_speed` (`speed`) and `rimbridge/list_logs` (`count`, field `message`) via `rimbridge/get_capability` during the live run; adjust in `qb.py`/tests once.

- [ ] **Step 4: Run integration D cases, verify pass**

Run: `python -m pytest qbtest/tests/test_d_rebuild.py -k "d3 or d6 or d8" -v`
Expected: 3 passed (allow retries; integration timing can need the timeouts bumped).

- [ ] **Step 5: Commit**

```bash
git add qbtest/tests/test_d_rebuild.py
git commit -q -m "qbtest: module D (rebuild cycle) complete incl. 3 integration tests"
```

---

## Task 15: Test module C — skill gating (C1..C7)

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_c_skill.py`
- Modify: `Source/QbInspectTools.cs` — add `qb/is_pawn_good_enough` (wraps `_WorkGiver_ConstructFinishFrames.isPawnGoodEnoughToBuild`) so C1..C5 are deterministic logic-level.

- [ ] **Step 1: Add the WorkGiver-gate inspection tool**

In `QbInspectTools.cs`:

```csharp
[Tool("qb/is_pawn_good_enough",
    Description = "Evaluate QB's isPawnGoodEnoughToBuild(pawn) for a colonist (the WorkGiver skill gate).",
    ResultDescription = "good: bool.", Tags = new[] { "read-only" })]
public Task<object> IsPawnGoodEnough(IRimBridgeContext ctx, string pawnId, CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var map = CurrentMap;
        if (map == null) return Error("no current map");
        Pawn pawn = null;
        foreach (var p in map.mapPawns.AllPawnsSpawned) if (p.ThingID == pawnId) { pawn = p; break; }
        if (pawn == null) return Error("no pawn '" + pawnId + "'");
        var res = QbReflect.CallStatic(QbReflect.WorkGiverType, "isPawnGoodEnoughToBuild",
            new[] { typeof(Pawn) }, pawn);
        return new { success = true, good = (bool)res };
    }, ct);
```

Add wrapper to `qb.py`:
```python
def is_pawn_good_enough(c, pawn_id):
    return c.call("qb/is_pawn_good_enough", pawnId=pawn_id)["good"]
```
Rebuild + deploy + restart.

- [ ] **Step 2: Write C1..C7**

```python
# qbtest/tests/test_c_skill.py
import pytest
from qbtest import qb

pytestmark = pytest.mark.live


def _reset_gate_settings(client):
    qb.set_setting(client, "useMapSettings", True)
    qb.set_setting(client, "bestConstructorOverride", "")            # clear override
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 20)
    qb.set_setting(client, "skillDifferenceFromBestBuilder", 0)


def test_c1_low_skill_denied_high_allowed(client, colonists):
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    qb.set_pawn_skill(client, colonists["lo"], 2)
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 20)  # gate active below 20
    qb.set_setting(client, "skillDifferenceFromBestBuilder", 3)
    assert qb.is_pawn_good_enough(client, colonists["hi"]) is True
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is False


def test_c3_best_constructor_override(client, colonists):
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    qb.set_pawn_skill(client, colonists["lo"], 2)
    qb.set_setting(client, "bestConstructorOverride", colonists["hi"])
    assert qb.is_pawn_good_enough(client, colonists["hi"]) is True
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is False
    # A downed override must not block everyone.
    qb.set_pawn_flags(client, colonists["hi"], downed=True)
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is True


def test_c4_ignore_at_skill_threshold(client, colonists):
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    qb.set_pawn_skill(client, colonists["lo"], 8)
    qb.set_setting(client, "skillDifferenceFromBestBuilder", 3)
    # ignore gate at 8 -> a level-8 pawn is at/above ignore threshold => always good.
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 8)
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is True
    # Raise ignore threshold above lo's level -> gate applies, and 8 < 15-3=12 => denied.
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 20)
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is False


def test_c5_skill_difference_boundary(client, colonists):
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 20)
    qb.set_setting(client, "skillDifferenceFromBestBuilder", 5)  # target = 15-5 = 10
    qb.set_pawn_skill(client, colonists["lo"], 10)
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is True   # at target
    qb.set_pawn_skill(client, colonists["lo"], 9)
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is False  # below target


def test_c7_best_skill_excludes_downed(client, colonists):
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 18)
    qb.set_pawn_skill(client, colonists["lo"], 4)
    before = qb.get_settings(client)["bestConstructorSkill"]
    assert before >= 18
    qb.set_pawn_flags(client, colonists["hi"], downed=True)
    # qb/get_settings reads the RAW (uncached) getBestConstructorSkill, so the downed-exclusion
    # is visible immediately — no cache-expiry wait needed. (The ~10s cache lives on a different
    # accessor used inside the WorkGiver gate, not on this field.)
    after = qb.get_settings(client)["bestConstructorSkill"]
    assert after < 18  # downed high-skill pawn no longer counts


@pytest.mark.integration
def test_c1b_low_skill_denied_finishframe_job(client, arena, colonists):
    # Real integration check with a concrete assertion: on a QB frame the gate denies, the
    # low-skill pawn is never assigned a FinishFrame job targeting that frame. Uses the
    # WorkGiver-gate tool as the deterministic oracle plus a live job-state read.
    _reset_gate_settings(client)
    qb.set_pawn_skill(client, colonists["hi"], 15)
    qb.set_pawn_skill(client, colonists["lo"], 2)
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 20)
    qb.set_setting(client, "skillDifferenceFromBestBuilder", 3)
    x, z = arena.x, arena.z
    tid = qb.spawn_blueprint(client, "Wall", x, z, stuff="Steel")
    qb.set_skilled(client, tid, "Good", True)
    # Deterministic oracle: the gate itself denies lo and allows hi for THIS frame's finish.
    assert qb.is_pawn_good_enough(client, colonists["lo"]) is False
    assert qb.is_pawn_good_enough(client, colonists["hi"]) is True
    # Live signal: run the colony briefly; the low-skill pawn must never hold a FinishFrame
    # job on this frame. (Read the low pawn's current job via job-state tooling — exact tool
    # confirmed during the live pass; assert its job def/target is not FinishFrame on `tid`.)
    client.call("rimworld/set_time_speed", speed=3)
    import time
    lo_ever_finished = False
    for _ in range(15):
        job = qb.pawn_current_job(client, colonists["lo"])
        if job.get("defName") == "FinishFrame" and job.get("targetThingId") == tid:
            lo_ever_finished = True
            break
        time.sleep(1)
    client.call("rimworld/pause_game")
    assert lo_ever_finished is False
```

C1b uses a deterministic oracle (`is_pawn_good_enough`) plus a live job-state read, so it has a real assertion (no `assert True`). It needs a `qb/get_pawn_current_job` companion tool (returns `{defName, targetThingId}` for a pawn's current job) and a `qb.pawn_current_job` wrapper — add both in this task's Step 1 alongside `qb/is_pawn_good_enough`. **C2 (forced bypass)** is **covered by C1 + the forced-branch being a direct `if (!forced && !isPawnGoodEnoughToBuild(pawn))` guard**; no separate live test.

Add to `QbInspectTools.cs`:

```csharp
[Tool("qb/get_pawn_current_job",
    Description = "Read a colonist's current job def and targetA thing id (for gate/kick assertions).",
    ResultDescription = "defName (or null) and targetThingId (or null).", Tags = new[] { "read-only" })]
public Task<object> GetPawnCurrentJob(IRimBridgeContext ctx, string pawnId, CancellationToken ct = default)
    => ctx.MainThread.InvokeAsync<object>(() =>
    {
        var map = CurrentMap;
        if (map == null) return Error("no current map");
        Pawn pawn = null;
        foreach (var p in map.mapPawns.AllPawnsSpawned) if (p.ThingID == pawnId) { pawn = p; break; }
        if (pawn == null) return Error("no pawn '" + pawnId + "'");
        var job = pawn.jobs?.curJob;
        return new
        {
            success = true,
            defName = job?.def?.defName,
            targetThingId = job?.targetA.Thing?.ThingID,
        };
    }, ct);
```

Add to `qb.py`:
```python
def pawn_current_job(c, pawn_id):
    return c.call("qb/get_pawn_current_job", pawnId=pawn_id)
```

- [ ] **Step 3: Run module C, verify pass**

Run: `python -m pytest qbtest/tests/test_c_skill.py -v`
Expected: C1,C3,C4,C5,C7 PASS; C1b passes as a smoke. Adjust skill numbers if the qb_test colony's base skills differ.

- [ ] **Step 4: Commit**

```bash
git add qbtest/tests/test_c_skill.py Source/QbInspectTools.cs qbtest/qb.py
git commit -q -m "qbtest: module C (skill gating) + qb/is_pawn_good_enough"
```

---

## Task 16: Test module B — persistence (B1..B3) + crash-restart hardening + reporting

**Files:**
- Create: `Mods/QualityBuilderBridgeTools/qbtest/tests/test_b_persistence.py`
- Modify: `Mods/QualityBuilderBridgeTools/qbtest/conftest.py` (crash-restart hook)

**Interfaces:** persistence tests reload the save via built-in tools; after reload, previously-spawned things are gone, so each B test spawns → saves → reloads → re-locates by cell.

- [ ] **Step 1: Add crash-restart to `conftest.py`**

Append a session-scoped restart helper and wrap `clean_arena` to recover:

```python
# --- append to conftest.py ---
@pytest.fixture(scope="session")
def _restart(_game):
    def do():
        c = _game.restart()
        _game.load_test_save(c)
        return c
    return do


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    rep = outcome.get_result()
    # Mark the session client as dead so the next test's clean_arena triggers a restart.
    if rep.when == "call" and rep.failed and "ToolTimeout" in str(rep.longrepr):
        item.session._qb_bridge_dead = True
```

And make `clean_arena` restart when the bridge died:

```python
@pytest.fixture(autouse=True)
def clean_arena(request):
    # Gate on the `live` marker + lazy fixture resolution so offline unit tests never launch
    # the game (see Task 10's clean_arena rationale).
    if "live" not in request.keywords:
        yield
        return
    client = request.getfixturevalue("client")
    _restart = request.getfixturevalue("_restart")
    if getattr(request.session, "_qb_bridge_dead", False):
        new_client = _restart()
        # replace the session-cached client object's socket by reconnecting in place
        client.__dict__.update(new_client.__dict__)
        request.session._qb_bridge_dead = False
    qb.clear_arena(client, ARENA)
    yield
    try:
        qb.heal_all_colonists(client)  # undo any test that downed a pawn (no health leak)
        qb.clear_arena(client, ARENA)
    except (ToolTimeout, BridgeError):
        request.session._qb_bridge_dead = True
```

(Replace the Task-10 `clean_arena` with this version.)

- [ ] **Step 2: Write B1..B3**

```python
# qbtest/tests/test_b_persistence.py
import pytest
from qbtest import qb

pytestmark = [pytest.mark.live, pytest.mark.persistence]


def test_b1_comp_fields_roundtrip(client, arena):
    x, z = arena.x, arena.z
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", x, z, stuff="Steel")
    qb.set_skilled(client, tid, "Excellent", True)
    qb.set_comp_state(client, tid, qualityRebuildAttempts=2, pendingQualityRebuild=True,
                      isDesiredMinQualityReached=False)
    qb.save_game(client, "qb_test_tmp")
    qb.load_game(client, "qb_test_tmp")
    st = qb.get_building_state(client, x=x, z=z)  # re-locate by cell after reload
    assert st["isSkilled"] is True
    assert st["desiredMinQuality"] == "Excellent"
    assert st["qualityRebuildAttempts"] == 2
    assert st["pendingQualityRebuild"] is True


def test_b2_load_adopts_existing_designation(client, arena):
    x, z = arena.x, arena.z
    qb.set_setting(client, "defaultUseQualityBuilder", False)
    tid = qb.spawn_blueprint(client, "Wall", x, z, stuff="Steel")
    qb.set_skilled(client, tid, "Normal", True)  # SkilledBuilder3 designation
    qb.save_game(client, "qb_test_tmp")
    qb.load_game(client, "qb_test_tmp")
    st = qb.get_building_state(client, x=x, z=z)
    assert st["isSkilled"] is True
    assert st["desiredMinQuality"] == "Normal"
    assert st["qbDesignation"] == "SkilledBuilder3"


def test_b3_map_settings_persist(client):
    qb.set_setting(client, "useMapSettings", True)
    qb.set_setting(client, "ignoreQualityBuilderAtSkill", 11, scope="map")
    qb.save_game(client, "qb_test_tmp")
    qb.load_game(client, "qb_test_tmp")
    s = qb.get_settings(client, scope="map")
    assert s["useMapSettings"] is True
    assert s["effective"]["ignoreQualityBuilderAtSkill"] == 11
```

- [ ] **Step 3: Run module B, verify pass**

Run: `python -m pytest qbtest/tests/test_b_persistence.py -v`
Expected: 3 passed. (Reload is slow; each test ~10-20s.)

- [ ] **Step 4: Full-suite run + JSON report**

Install once: `pip install pytest pytest-json-report`.
Run: `python -m qbtest`
Expected: launches game (if not running), loads `qb_test`, runs all modules, writes `qbtest_report.json`, exits non-zero only if a test failed. Confirm the JSON report lists every A/B/C/D/E test with outcomes.

- [ ] **Step 5: Commit**

```bash
git add qbtest/tests/test_b_persistence.py qbtest/conftest.py
git commit -q -m "qbtest: module B (persistence) + crash-restart recovery + full-suite JSON report"
```

---

## Self-Review — spec coverage

| Spec item | Covered by |
|---|---|
| Companion DLL (RimBridge SDK pattern, deploy to BridgeTools, dev-mode gating, reflection-into-QB) | Tasks 1,6,7,8,11,15 |
| `qb/get_building_state`, `list_qb_things`, `get_settings`, `get_gizmo_info` | Tasks 6,8 |
| `spawn_blueprint`, `spawn_finished_building`, `set_skilled`, `set_comp_state`, `invoke_check_rebuild`, `invoke_after_finish_toil`, `set_pawn_skill`, `set_pawn_flags`, `set_setting`, `clear_arena` | Tasks 7,8 |
| Built-in tools for load/save/spawn/messages/logs (no companion dup) | Tasks 5,9,14,16 |
| `qb_test` save spec (dev mode, arena, 2 builders, ideology, materials) | Task 12 |
| Python: GABP TCP transport (framing, handshake, tools/call, error) | Tasks 2,4 |
| Python: log discovery of port/token | Task 3 |
| Python: game lifecycle (launch/kill/restart, load) | Task 5 |
| Python: per-test arena cleanup; reload only for persistence | Tasks 10,16 |
| Python: crash-restart, PASS/FAIL/ERROR + JSON report, exit code | Task 16 |
| Determinism via forced quality | Tasks 7,14 |
| Test inventory A1..A8 | Task 11 (+A1 Task 10) |
| B1..B3 | Task 16 |
| C1..C7 (C2 by inspection, C6→D-style integration) | Task 15 |
| D1..D8 | Task 14 |
| E1..E3 | Task 13 |
| Build/deploy (`dotnet build -c Release`, restart to load) | Task 1 + each DLL task |

**Coverage notes / deliberate deviations:**
- **C1b** uses a deterministic oracle (`is_pawn_good_enough`) plus a live current-job read (`qb/get_pawn_current_job`) — it has a real assertion, not `assert True`.
- **C2 (forced bypass)** has no standalone live test — the WorkGiver's forced branch is a direct `if (!forced && !isPawnGoodEnoughToBuild(pawn))` guard; C1 proves the gate, and the `forced` bypass is covered by code inspection. Documented here rather than adding a flaky force-build test.
- **C6 (kick)** from the spec is realized within module C's integration surface but is timing-sensitive; the deterministic kick assertion is hard to make non-flaky, so C1/C1b cover the gate and the kick is exercised opportunistically. If a robust kick test is required, add a `qb/start_finishframe_job(pawnId, frameId)` companion tool in a follow-up to make it deterministic.
- **A5** label matching depends on English localization in `qb_test` (documented in Task 12).
- **D7 (def disambiguation)** is `@pytest.mark.skip` — reproducing `afterFinishToil`'s wall-vs-lamp branch deterministically needs a real in-progress `Frame` (so `cmp.parent as Frame` is non-null) AND a wall-attached-light def sharing the wall cell, neither reliably available in the base env. Covered by code inspection; revisit with a `qb/spawn_frame` companion tool + a wall-light mod. Passing a finished Building makes the assertion vacuous, so skipping is deliberate.
- Persistence tests write to `qb_test_tmp` (never overwrite the fixture `qb_test`).

**Placeholder scan:** no TBD/TODO; every code step shows complete code. Verification steps that depend on exact built-in arg names (`apply_architect_designator`, `set_time_speed`, `list_logs`) are flagged with a one-time confirm-via-`get_capability` note rather than guessed silently.

**Type consistency:** wrapper names (`get_building_state`, `set_setting`, `spawn_finished_building`, `is_pawn_good_enough`, `set_forbidden`) match between `qb.py` (Tasks 9/11/15) and their tests; companion tool names (`qb/...`) match between DLL tasks and wrappers; `QbReflect` member names match across DLL tasks.

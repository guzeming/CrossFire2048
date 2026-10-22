"""Real offline ASR + GGUF + Windows speech integration, including silence and interruption."""
import base64
import json
import pathlib
import queue
import subprocess
import sys
import threading
import time
import wave

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUTPUT = ROOT / ".build-check" / "lobby-assistant"
OUTPUT.mkdir(parents=True, exist_ok=True)
runtime = ROOT / "LocalAssistant"
events = queue.Queue()
diagnostics = (OUTPUT / "backend.log").open("w", encoding="utf-8")
process = subprocess.Popen([str(runtime / "host" / "Blacktide.LocalAssistant.exe"), str(runtime)], stdin=subprocess.PIPE,
                           stdout=subprocess.PIPE, stderr=diagnostics, text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)

def read_output():
    for line in process.stdout:
        events.put(json.loads(line))
    events.put({"type": "exit"})

threading.Thread(target=read_output, daemon=True).start()

def send(kind, turn=0, **fields):
    process.stdin.write(json.dumps(dict(type=kind, turn=turn, **fields), ensure_ascii=False) + "\n")
    process.stdin.flush()

def collect(until, turn=None, timeout=90):
    deadline = time.monotonic() + timeout
    result = []
    while time.monotonic() < deadline:
        item = events.get(timeout=max(.1, deadline - time.monotonic()))
        if item["type"] in ("fatal", "exit"):
            raise AssertionError(item)
        if turn is not None and item.get("turn") != turn:
            continue
        if item["type"] == "error":
            raise AssertionError(item)
        result.append(item)
        if item["type"] == until:
            return result
    raise AssertionError("Timed out waiting for " + until)

try:
    started = time.monotonic()
    collect("ready")
    startup = time.monotonic() - started
    # Use actual Chinese speech from the installed offline Windows voice as ASR input.
    wav_path = OUTPUT / "input.wav"
    ps = """Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$v = $s.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -eq 'zh-CN' } | Select-Object -First 1
$s.SelectVoice($v.VoiceInfo.Name)
$f = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$s.SetOutputToWaveFile($args[0], $f)
$s.Speak('你好请简单介绍一下你自己。')
$s.Dispose()
"""
    synth_script = OUTPUT / "synthesize-test.ps1"
    synth_script.write_text(ps, encoding="utf-8-sig")
    subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(synth_script), str(wav_path)], check=True, creationflags=subprocess.CREATE_NO_WINDOW)
    with wave.open(str(wav_path), "rb") as source:
        assert source.getnchannels() == 1 and source.getsampwidth() == 2
        pcm, rate = source.readframes(source.getnframes()), source.getframerate()
    send("begin", 1)
    for offset in range(0, len(pcm), 3200):
        send("audio", 1, pcm=base64.b64encode(pcm[offset:offset+3200]).decode(), sampleRate=rate)
        time.sleep(.1)
    speech_end = time.monotonic()
    send("end", 1)
    first = collect("audio", 1)
    first_audio = time.monotonic() - speech_end
    rest = collect("done", 1)
    turn = first + rest
    partials = [event["text"] for event in turn if event["type"] == "partial"]
    final = next(event["text"] for event in turn if event["type"] == "final")
    answer = next(event["text"] for event in turn if event["type"] == "done")
    assert len(set(partials)) >= 2, partials
    assert "你好" in final, final
    assert len(answer) > 3, answer
    audio = b"".join(base64.b64decode(event["pcm"]) for event in turn if event["type"] == "audio")
    assert len(audio) > 24000 and any(audio), "Missing synthesized audio"
    with wave.open(str(OUTPUT / "reply.wav"), "wb") as target:
        target.setnchannels(1); target.setsampwidth(2); target.setframerate(24000); target.writeframes(audio)
    send("begin", 2)
    send("audio", 2, pcm=base64.b64encode(b"\0" * 32000).decode(), sampleRate=16000)
    send("end", 2)
    collect("empty", 2)
    send("text", 3, text="请介绍训练场。")
    collect("reply", 3)
    send("cancel", 4)
    send("text", 5, text="请只回复：好的，已经停止。")
    interrupted = collect("done", 5)
    assert any(event["type"] == "audio" for event in interrupted)
    report = dict(startup_seconds=round(startup, 3), speech_end_to_first_audio_seconds=round(first_audio, 3),
                  recognized=final, partial_updates=len(partials), reply=answer,
                  tests=["real_streaming_asr", "real_local_gguf_reply", "real_chinese_tts", "silence_rejected", "interrupt_and_new_turn"])
    (OUTPUT / "integration.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("LOCAL_ASSISTANT_INTEGRATION_PASS " + json.dumps(report, ensure_ascii=False), flush=True)
finally:
    if process.poll() is None:
        send("shutdown")
        process.stdin.close()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
    diagnostics.close()

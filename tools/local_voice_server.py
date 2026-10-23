"""Optional local ASR bridge. Never started or installed automatically by NabyScape.

In an isolated Python environment:
  python -m pip install faster-whisper fastapi python-multipart uvicorn
  python local_voice_server.py --model small --device cpu --compute-type int8

For a CUDA setup with compatible CUDA/cuDNN libraries and enough free VRAM:
  python local_voice_server.py --model large-v3-turbo --device cuda --compute-type int8_float16

The model downloads on the first transcription. CPU tiny/base are lighter than small.
Speech recognition is local after model installation. Do not expose this server publicly.
Kokoro-FastAPI or a compatible TTS service runs separately; Windows TTS needs no server.
"""
import argparse
import io
import threading
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
import uvicorn
from faster_whisper import WhisperModel

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--model", default="small")
parser.add_argument("--device", choices=("cpu", "cuda"), default="cpu")
parser.add_argument("--compute-type", default="int8")
parser.add_argument("--port", type=int, default=8178)
options = parser.parse_args()
app = FastAPI(docs_url=None, redoc_url=None)
lock = threading.Lock()
engine = None


@app.get("/health")
def health():
    return {"status": "ready", "model": options.model, "loaded": engine is not None}


@app.get("/v1/models")
def models():
    return {"data": [{"id": options.model}]}


@app.post("/v1/audio/transcriptions")
def transcribe(file: UploadFile = File(...), model: str = Form(""), response_format: str = Form("json")):
    global engine
    if model and model != options.model:
        raise HTTPException(400, "Model differs from this server. Restart it with --model or update Naby's ASR model ID.")
    audio = file.file.read(2 * 1024 * 1024 + 1)
    if len(audio) > 2 * 1024 * 1024:
        raise HTTPException(413, "Audio upload exceeds 2 MiB.")
    if not lock.acquire(blocking=False):
        raise HTTPException(429, "A transcription is already running. Try again shortly.")
    try:
        if engine is None:
            engine = WhisperModel(options.model, device=options.device, compute_type=options.compute_type)
        segments, info = engine.transcribe(io.BytesIO(audio), beam_size=1, vad_filter=True)
        return {"text": " ".join(segment.text.strip() for segment in segments), "language": info.language}
    except Exception:
        raise HTTPException(500, "Local ASR failed. Check this server's model, device and CUDA/cuDNN installation.")
    finally:
        lock.release()


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=options.port, access_log=False)

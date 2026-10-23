# Naby voice · local speech setup

Naby voice is a separate optional widget. Turn it on in **Settings → Widgets & tools**, or choose **Voice mode** in the chat header. It shares conversation, Stop and selected screen context with Naby chat. Naby has no built-in action or computer-control tools. Opening Voice mode never starts the microphone.

## Choose your voice path

Choose **Voice → Voice settings** to jump directly to **Settings → Naby AI → Voice → Voice setup · choose ASR & TTS**. Use **Automatic setup · scan & install** for managed local speech, **Realtime provider** for your realtime API, or **External ASR + TTS · advanced** for an existing speech server.

## Automatic setup: scan, install and talk

1. Choose **Automatic setup · scan & install**. The scan reads CPU, thread count, available physical RAM and GPU names locally. NVIDIA driver and total/free VRAM are read using the installed NVIDIA utility when available. Hardware data is not uploaded.
2. Choose a reply language and a Light, Balanced or Quality priority. Choose **Use recommendation**, or select another listening model and speaking voice. Expand **Compare listening models** for sizes and hardware fit. The card shows the pack size, memory budget and installation status. Recommendations consider free memory and are conservative heuristics, not a benchmark or guarantee of speed.
3. Choose **Install & use**. Only this action downloads engines and weights. Downloads use fixed versions/revisions, bounded sizes, SHA-256 verification and staging before activation. A missing Microsoft Visual C++ x64 runtime is downloaded from Microsoft and installed; Windows may ask for administrator approval. If it requires a restart, the UI tells you to restart and retry. Cancellation does not interrupt an in-progress Windows Installer transaction.
4. After the pack is installed and selected, choose **Open voice mode**, then **Start conversation**. The shortcut becomes available when the saved language, performance preference and models match the selected pack and a chat model is configured. Naby loads the selected model and starts its local engine automatically; no Python or terminal is needed. Opening the widget or installing a pack does not start the microphone. **End conversation** stops capture, playback and native child processes, releasing model RAM/VRAM.

## Voice latency · local 2.18 candidate

Supertonic 3, Kokoro and Kitten load once into a private native TTS worker when Start conversation is used. They remain loaded while voice is active, including with the dock closed, and are released on End conversation, Stop, hide or quit. This increases active-session memory use. Whisper already stays loaded during the session; native Qwen/Moonshine ASR still load per utterance. The legacy bundled Piper path keeps a single full-reply synthesis; external servers and Windows voices keep their existing engines.

Compatible Chat Completions endpoints, including Hermes, can send streamed reply text for sentence-sized synthesis/playback. Speech preparation overlaps with generation and playback using bounded queues. Fenced code is skipped and spoken output remains limited to 6,000 characters / two minutes; the full completed answer stays in chat. Local tool-call turns wait for the final text-only response before speech. Other protocols, image-output requests and endpoints returning JSON retain complete-response compatibility. Disable streaming using **Voice settings → Stream local voice replies when supported → Save streaming preference** if an endpoint rejects it. Saving ends the current voice session.

Stage labels distinguish transcription, waiting for the provider, preparing voice and speaking. ASR, first-text and first-segment TTS durations appear below voice controls, with detailed timing in the tooltip. Measurements stay in memory and exclude the silence-detection pause and hardware output delay. No performance claim has been established by this candidate: only source review and compilation/package checks were performed, without microphones, inference, provider calls or runtime benchmarks.

## Hardware profiles and model catalog · 2.14

Setup labels, guidance and errors are in English. New configurations default to English; existing language choices are preserved. The language picker filters listening/speaking models. Supertonic 3 is available for the 15 listed conversation languages; Moonshine and Kitten packs are language-specific.

| Hardware profile | Listening model | Speaking model |
| --- | --- | --- |
| CPU, 8 GB RAM | Moonshine v2 base English (2026); tiny for Light | KittenTTS nano v0.8 INT8 for English; Supertonic 3 when language/budget permits |
| CPU, 16 GB RAM | Qwen3-ASR 0.6B INT8 (2026) | Supertonic 3 INT8 |
| CPU, 32 GB RAM | Qwen3-ASR 1.7B INT8 in Quality; 0.6B in Balanced | Supertonic 3 INT8 |
| NVIDIA, 2 GB VRAM | Whisper base Q5 | KittenTTS or Supertonic 3 according to language, RAM and preference |
| NVIDIA, 4 GB VRAM | Whisper small Q5 | KittenTTS or Supertonic 3 |
| NVIDIA, 6 GB VRAM | Whisper large-v3-turbo Q5 | Supertonic 3 |
| NVIDIA, 8+ GB VRAM | Whisper large-v3-turbo Q8 | Supertonic 3 |

These are selectable profiles, not guarantees based on capacity alone. CPU model suggestions also consider thread count. ASR and TTS must fit available RAM; GPU recognition must fit available VRAM. Busy GPUs can fall back to a smaller model or CPU. TTS stays on CPU for every profile, leaving VRAM for recognition and other apps. Quality changes Supertonic synthesis from 8 to 12 steps; Light uses 4. Higher steps can take longer and are not a measured quality guarantee.

The GPU profiles use supported quantizations of Whisper; quantization does not make the model family a new 2026 release. Moonshine v2, Qwen3-ASR and Supertonic 3 supply newer CPU options. Smaller Whisper, Kokoro v1.1 English, Piper and Windows voices remain available in the model library. Qwen3-ASR 1.7B uses an explicitly attributed community ONNX conversion, rather than an official Qwen Windows binary.

### Downloads and memory budgets

| Added model | Model download | Available RAM budget before TTS |
| --- | --- | --- |
| Moonshine v2 tiny English | 30 MB compressed | 0.6 GB |
| Moonshine v2 base English | 111 MB compressed | 1 GB |
| Moonshine v2 base Spanish | 51 MB compressed | 1 GB |
| Qwen3-ASR 0.6B INT8 | 879 MB compressed | 3.5 GB |
| Qwen3-ASR 1.7B INT8 | 2.40 GB | 7 GB |
| Whisper base Q5 | 60 MB | 1 GB |
| Whisper small Q5 | 190 MB | 2 GB |
| Whisper large-v3-turbo Q5 | 574 MB | 3 GB |
| Whisper large-v3-turbo Q8 | 874 MB | 4 GB |
| KittenTTS nano v0.8 INT8 | 31 MB compressed | 0.35 GB TTS budget |
| Supertonic 3 INT8 | 129 MB compressed | 1 GB TTS budget |

Full pack size shown in settings includes the selected weights, native runtime, optional CUDA runtime and any missing Microsoft C++ prerequisite. These budgets are conservative estimates, not benchmark results. The 1.7B CPU pack can be slow and is intended for the Quality profile on larger machines.

Automatic native packs require **an x64 app process on Windows x64 with AVX2/FMA**. Whisper GPU uses CUDA 12.4 and a compatible NVIDIA driver (551.61+), with CPU fallback included. AMD/Intel graphics and unconfirmed GPUs use CPU; The native ARM64 app package does not make these x64 speech packages compatible: ARM64 users should use a compatible external local ASR/TTS service or provider. Windows voice and wake-word recognition also depend on matching installed SAPI engines. No drivers are installed. The extra native speech runtime is approximately 25 MB; optional Whisper GPU runtime approximately 460 MB. Model weights stay outside the app installer under `%LOCALAPPDATA%\NabyScape\local-speech`.

All native engines are session-owned. Whisper stays loaded while talking; Moonshine/Qwen recognize one utterance and exit, and native TTS loads only while speaking. Downloading or choosing a pack does not start the microphone. **End conversation** releases the engines. New compatible versions arrive through Naby updates; this is a curated catalog rather than unrestricted live model discovery.

### Licenses and international use

Model terms differ from NabyScape's MIT license. In particular, Supertonic 3 weights use OpenRAIL-M. **Voice details & license** opens the relevant model page; runtime archives retain their bundled notices and the speech folder records source attribution in `SOURCES.txt`. Downloaded files have pinned revisions/sizes and SHA-256 checksums. Installation stages complete packages before activation; cancellation keeps verified downloads for retry.

The default conversation language is English. Non-ASCII text and Windows account/file paths are not runtime-validated in the native CLI adapters. Sherpa documents [Windows UTF-8 requirements](https://k2-fsa.github.io/sherpa/onnx/tts/faq.html); Naby does not change Windows locale settings. A separately configured external speech server remains available if the native command-line encoding is unsuitable for the selected language.

Installing or switching speech packs preserves your chat conversation and external-server settings. The selected chat provider/model is still needed for AI replies; the app shows a setup error before opening the microphone when it is missing.

Local speech recognizes an utterance after a pause, sends its text to your selected **chat provider**, and speaks the reply locally. The microphone waits during recognition and playback to avoid hearing its own reply. End conversation cancels recognition, requests and playback. Whisper/Piper WAV/PCM stays in memory. Moonshine/Qwen3-ASR and native TTS CLI adapters write temporary utterance/reply WAV files under `%TEMP%\NabyScape-speech-*`, deleting them on normal completion, errors and cancellation. A crash can leave temporary audio behind. Captured audio and generated transcripts are not intentionally logged.

**Local speech is not automatically local chat.** Select a local Ollama or LM Studio model too if the whole conversation should stay on this computer. Screen sharing is explicit and independent. Naby’s former Actions/Computer controls have been removed; a connected Hermes agent manages its own tools and permissions. See [Hermes connection](hermes.md).

## Use your own model files · 2.15

Choose **Voice → Voice settings → Speech connection → Use your own model files**. Pick the listening and speaking format, then browse to files/folders or paste full local drive paths. **Check files** checks the required file structure without inference. **Prepare engines & use models** downloads only missing pinned engines, saves the selection and enables it for **Start conversation**. When engines are already present, no network download is required. Model weights stay in their original location and are not copied, overwritten or deleted.

You can combine your own ASR with Windows voice or an installed speaking pack, or use an installed listening pack with your own TTS. A pack selected as Installed must already have its weights from Automatic setup. Missing stock weights are not downloaded through the custom-model form. Conversation language must match the selected models; default is English.

| Format | Select | Required support files |
| --- | --- | --- |
| Whisper GGML | A whisper.cpp `.bin` model | Valid GGML header; GGUF chat models and safetensors are not accepted |
| Qwen3-ASR INT8 | Extracted Sherpa-ONNX folder | `conv_frontend.onnx`, `encoder.int8.onnx`, `decoder.int8.onnx`, `tokenizer/vocab.json`, `tokenizer/merges.txt` |
| Moonshine v2 | Extracted Sherpa-ONNX folder | `encoder_model.ort` or `.onnx`, `decoder_model_merged.ort` or `.onnx`, `tokens.txt` |
| Piper | Voice `.onnx` | Matching `.onnx.json` beside it, including an audio sample rate from 8000 to 48000 Hz |
| Kokoro / KittenTTS | Extracted Sherpa-ONNX folder | ONNX weights, `voices.bin`, `tokens.txt`, complete `espeak-ng-data`; this adapter uses English voices |
| Supertonic INT8 | Extracted Sherpa-ONNX folder | Four `.int8.onnx` components, `tts.json`, `unicode_indexer.bin`, `voice.bin` in the documented layout |

Choose speaker ID 0 for the default voice, or an existing ID in the model. A complete file layout does not prove that an arbitrary model is compatible with the pinned engine, fits memory or will synthesize correctly. The app does not execute user-supplied scripts or executables. For a different export/runtime, use **External ASR + TTS** with your existing localhost service.

Full paths are saved in the local assistant settings. Moving/removing model files, disconnecting a drive or deleting supporting files requires choosing the location again. Imported models do not have a catalog memory estimate; available hardware is checked, but custom model capacity cannot be guaranteed. Windows x64/AVX2/FMA and the native CLI encoding limits below still apply. Naby does not change the model's license or language capabilities.

## External servers: manual setup

For **External ASR + TTS · advanced**, choose a hardware template, then select or paste a **Listening model**, **Text-to-speech engine**, **Speaking model**, and **Voice**. These suggestions are not an inventory of installed server models. Server URLs and pause/sensitivity controls are in **Server addresses & microphone tuning**.

Follow the displayed server instructions, choose **Check local speech services**, then **Save voice setup**. The ASR model ID must match the server's loaded model. Changing a model ID here does not download or switch server weights.

| Template | ASR starting point | TTS starting point | Notes |
| --- | --- | --- | --- |
| Light · CPU | whisper.cpp tiny or base, multilingual | Installed Windows SAPI voice | No dedicated GPU required; CPU latency varies. |
| Balanced · CPU / GPU | faster-whisper small, CPU int8 or supported GPU | Kokoro through its local API | Separate speech and chat processes use their own RAM. |
| Quality · about 8 GB VRAM | faster-whisper large-v3-turbo, compatible CUDA/cuDNN | Kokoro or Windows voice | A starting point, not a memory guarantee. Local chat models share the GPU budget. |

These external-server templates configure connections only. They do not install engines, CUDA, voices or weights. Piper / compatible TTS is supported when its server exposes the WAV speech endpoint. Language support depends on the model and installed voice.

## Lightweight whisper.cpp

Start a [whisper.cpp server](https://github.com/ggml-org/whisper.cpp/tree/master/examples/server) with an installed tiny/base model. Configure **WhisperCpp** and its localhost base URL, typically `http://127.0.0.1:8080/`. Naby posts multipart WAV to `/inference`. Select a multilingual model for languages other than English.

## Optional faster-whisper bridge

Use **Save local ASR server template…** to export the bundled script (default filename: `naby-local-asr.py`). The source is [tools/local_voice_server.py](../tools/local_voice_server.py).

In a separate Python environment, install its optional dependencies:

```powershell
python -m pip install faster-whisper fastapi python-multipart uvicorn
python naby-local-asr.py --model small --device cpu --compute-type int8
```

Use **OpenAI-compatible**, base `http://127.0.0.1:8178/v1/`, and model `small`. The first recognition downloads the model if absent. The script is user-run; Naby never starts or installs it.

For supported CUDA/cuDNN setups with enough free GPU memory:

```powershell
python naby-local-asr.py --model large-v3-turbo --device cuda --compute-type int8_float16
```

Set Naby's ASR model to the same ID. See [faster-whisper prerequisites](https://github.com/SYSTRAN/faster-whisper). Keep the bridge on loopback; it is not an authenticated public server.

## Local text to speech

- **Windows voice** needs no HTTP server. Naby generates 24 kHz PCM using installed Windows SAPI and meters playback for the character.
- **Kokoro / compatible** uses a separately running [Kokoro-FastAPI](https://github.com/remsky/Kokoro-FastAPI) service. Default base `http://127.0.0.1:8880/v1/`, model `kokoro`, voice `af_heart`. Naby requests WAV at `/audio/speech`.
- **Piper / compatible** requires an HTTP server providing the same WAV endpoint. A standalone Piper executable is not an HTTP service.

URLs must be localhost or loopback. No speech-server key is sent. **Check local speech services** checks reachability only; it does not activate a microphone, prove model accuracy, or synthesize audio.

## Conversation and visuals

Voice uses a normal conversation without a style preset. Switch between Chat and Voice with the shared mode control. AI/Speech setup and Hermes connection are grouped in the connection bar. The character follows listening, transcribing/thinking, speaking and error. Its halo follows measured audio amplitude. Local replies show fading sentence captions with approximate timing, not word-level alignment. Full messages remain in chat. Reduce motion preserves static state changes.

**Test connection** verifies a selected chat API/model with one short request, without conversation, microphone or screen data. Provider charges may apply. Model discovery is metadata only; you can always paste a model ID manually.

## Validation limits

Compiled and packaged locally. At the user's request, no automated test, microphone/speaker trial, chat-provider request, model-weight download or inference/VRAM benchmark was performed during development. Engine archive metadata, source interfaces, model checksums and voice configurations were inspected without running the engines. Automatic installation and real-device speech still need runtime validation; external-server mode requires separate service setup.

New native model references: [Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR), [Sherpa Qwen3-ASR integration](https://k2-fsa.github.io/sherpa/onnx/qwen3-asr/pretrained.html), [Kokoro v1.1 languages/voices](https://k2-fsa.github.io/sherpa/onnx/tts/all/Chinese-English/kokoro-multi-lang-v1_1.html), [Sherpa native runtime v1.13.8](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8).

Implementation sources: [Whisper.cpp releases](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.8.7), [Whisper models](https://huggingface.co/ggerganov/whisper.cpp), [Piper native release](https://github.com/rhasspy/piper/releases/tag/2023.11.14-2), [Piper voices and model cards](https://huggingface.co/rhasspy/piper-voices), [Microsoft C++ runtime](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist), [NVIDIA CUDA 12.4 compatibility](https://docs.nvidia.com/cuda/archive/12.4.0/cuda-toolkit-release-notes/index.html).

Additional package references: [Moonshine v2](https://k2-fsa.github.io/sherpa/onnx/moonshine/models-v2.html), [Qwen3-ASR 1.7B conversion](https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8), [KittenTTS native integration](https://k2-fsa.github.io/sherpa/onnx/tts/all/English/kitten-nano-en-v0_8-int8.html), [Supertonic 3 native integration](https://k2-fsa.github.io/sherpa/onnx/tts/supertonic.html), [Supertonic model terms](https://huggingface.co/Supertone/supertonic-3/blob/main/LICENSE).

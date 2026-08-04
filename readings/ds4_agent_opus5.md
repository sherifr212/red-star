# `ds4_agent.c` — Technical Documentation

Source: `ds4_agent.c` (11,185 lines, C11, POSIX). Builds an interactive coding-agent CLI ("ds4-agent") on top of the local DS4/DeepSeek inference engine (`ds4.h`), analogous in spirit to Claude Code but talking to a locally-hosted GGUF model instead of a hosted API. It gives the model a small tool set (read/write/edit/list/search files, run shell commands, search/browse the web) inside a single-process, two-thread terminal application with on-disk conversation persistence (KV-cache snapshots).

This document was produced by direct reading of the source (line numbers cited as `ds4_agent.c:N` throughout — re-verify against the file if it has since changed) with special attention to **concurrency**: this program is single-process but multi-threaded, and additionally forks child processes for shell tool calls, so several distinct kinds of "concurrent" access are in play — thread/thread, thread/child-process, and process/process (two `ds4_agent` instances touching the same on-disk session cache). Each section below calls out what is and isn't safe.

Companion files in this repo the agent depends on: `ds4.h` (engine/session API), `ds4_kvstore.h` (on-disk KV-cache file format), `ds4_web.h` (web search/browse), `ds4_help.h`, `ds4_gpu_args.h`, `ds4_distributed.h` (multi-node inference), and the vendored `linenoise.c`/`linenoise.h` line-editing library.

---

## 1. Process and thread model

The file's own header comment (`ds4_agent.c:41-49`) states the design directly:

> The agent is intentionally not a single process split into a UI and worker: it is one process with a **UI thread** and a **worker thread**. The UI thread owns terminal input/output; the worker thread owns the live DS4 session and KV state.

- **UI thread** — runs `main()` → `run_agent()` (interactive) or `run_agent_non_interactive()` (scripted). Owns the terminal: reads stdin, drives the linenoise-based line editor, and is the *only* thread that ever writes to the real terminal (`ds4_agent.c:1210-1211` — this single-writer rule is what keeps redraws from tearing).
- **Worker thread** — runs `worker_main()` (`ds4_agent.c:8965`). Owns the `ds4_engine`/`ds4_session`, the token transcript, all tool execution (file I/O, shell jobs, web calls), and all session-file persistence.

No other threads exist. There is exactly one `agent_worker` per process (a global-ish struct instantiated once and passed by pointer). Concurrency inside the process is therefore a classic two-thread producer/consumer: the worker produces streamed text/status/tool output, the UI consumes and renders it, and occasionally the UI hands a new user turn (or a save/compact/power-change request) to the worker.

Beyond threads, the worker thread itself **forks child processes** for shell (`bash`) tool calls (§8), and the on-disk session cache (§9) can be touched by **multiple separate `ds4_agent` processes** (e.g., two terminals open on the same `$HOME/.ds4/kvcache`). Each of these three concurrency axes is handled with a different mechanism, summarized in §11.

---

## 2. Core data structures

Defined at the top of the file (`ds4_agent.c:51-330`):

- **`agent_config`** — immutable-after-parse launch configuration: engine options (model path, backend, context size, sampling knobs, GPU config), generation options (prompt, system prompt, think mode), and mode flags (`non_interactive`, `chdir_path`).
- **`agent_worker_state`** enum — `IDLE, PREFILL, GENERATING, COMPACTING, DRAINING, SAVING, ERROR, STOPPED`. The single authoritative state of the worker at any instant.
- **`agent_status`** — a small POD snapshot (state, prefill/generation progress + tokens/sec, context used/total, GPU power percent, last error string) copied out to the UI thread wholesale.
- **`agent_worker`** — the big shared-state struct (`ds4_agent.c:105-157`). Holds the engine/session pointers, the token transcript, cache/session-identity fields (`cache_dir`, `session_sha[41]`, `session_title`, `session_created_at`), the thread handle plus **`pthread_mutex_t mu` / `pthread_cond_t cond` / `int wake_fd[2]`** (the cross-thread protocol, §5), request flags (`save_requested`, `compact_requested`, `power_requested`, `interrupt`, `stop`), the streamed-output buffer (`out`/`out_len`/`out_cap`), the web-tool approval handshake fields, the bash-job list (`bash_jobs`, `next_bash_job_id`), and single-slot "more" paging state (`more_path`, `more_next_line`) for the `read`/`more` tool pair.
- **Tool-call model** — `agent_tool_arg` (name/value/is_string) → `agent_tool_call` (name + arg array) → `agent_tool_calls` (a resizable vector of calls).
- **Streaming tool-call parser** — `agent_dsml_parser` (the state machine that recognizes tool-call syntax inside the token stream) and `agent_stream_renderer` (which layers terminal rendering, `<think>` tracking, and a live tool-call visualizer on top of the parser). Covered in depth in §7.
- **`agent_token_renderer`** — the markdown/syntax-highlighting text renderer for plain assistant prose (bold/italic/code fences/inline code, per-language keyword coloring).
- **`agent_bash_job`** — per-background-shell-job bookkeeping (pid, pipes, spool file, byte/line counters, exit status). Defined near its first use (~`ds4_agent.c:7416`); see §8.
- **`agent_editor`** — the terminal-multiplexer state (scroll region geometry, prompt/status text, CPR cursor tracking). See §10.

---

## 3. Startup: CLI parsing and `main()`

### 3.1 `parse_options()` (`ds4_agent.c:562-764`)

A manual `argv` loop (no getopt) producing an `agent_config`. Defaults: model `ds4flash.gguf`, backend auto-selected by `default_backend()` (`ds4_agent.c:534-543`: CPU if `DS4_NO_GPU` env is set, Metal on Apple, else CUDA), system prompt `"You are a helpful coding assistant running inside ds4-agent."`, `n_predict=50000`, `ctx_size=100000`, `think_mode=DS4_THINK_HIGH`.

Distributed/cluster flags are delegated first to `ds4_dist_parse_cli_arg()` (from `ds4_distributed.h`) before the local flag chain runs, so any multi-node flag is consumed there.

| Flag | Meaning |
|---|---|
| `-h/--help [topic]` | Prints usage via `ds4_help_print`, exits 0 |
| `-p/--prompt <text>` | Initial/one-shot prompt |
| `--non-interactive` | Selects `run_agent_non_interactive` |
| `--raw/--raw-prompt` | Raw completion mode (must be combined with `--non-interactive -p`, else exit 2) |
| `-sys/--system <text>` | System prompt override |
| `--trace <path>` | Debug trace file of worker activity |
| `-m/--model <path>` | Model file |
| `--mtp <path>`, `--mtp-draft <n>`, `--mtp-margin <0..1000>` | Speculative-decoding draft model + tuning |
| `--glm-mtp` / `--glm-mtp-timing` | GLM-specific speculative decoding |
| `--dspark`, `--dspark-confidence <0..1>`, `--dspark-strict` | DeepSeek "Dspark" decoding variant |
| `-c/--ctx <n>` | Context size |
| `-n/--tokens <n>` | Max tokens to generate |
| `--temp <0..100>`, `--top-p <0..1>`, `--min-p <0..1>` | Sampling params; each sets a `*_set` flag so `agent_apply_model_sampling_defaults` (`ds4_agent.c:766`) knows whether to apply GLM-DSA-specific defaults (temp 1.0/top-p 0.95/min-p 0.0) only when unset by the user |
| `--seed <u64>` | RNG seed |
| `--think` / `--think-max` / `--nothink` | Reasoning-effort mode (HIGH/MAX/NONE), mutually overriding |
| `--backend metal\|cuda\|cpu`, `--metal`, `--cuda`, `--cpu` | Backend selection |
| `--gpu-vram <arg>`, `--gpu-devices <arg>` | Raw strings, parsed later in `main()` via `ds4_gpu_args.h` |
| `--cuda-tensor-parallel` | Multi-GPU tensor parallelism |
| `-t/--threads <n>` | CPU threads |
| `--chdir <path>` | chdir before engine load |
| `--quality` | Higher-quality (presumably slower) decoding path |
| `--ssd-streaming[-cold]`, `--ssd-streaming-cache-experts <count\|NGB>`, `--ssd-streaming-full-layers <n>`, `--ssd-streaming-preload-experts <n>`, `--simulate-used-memory <NGB>`, `--prefill-chunk <n>` | SSD-offloaded weight streaming for large MoE models that don't fit in VRAM/RAM |
| `--power <1..100>` | GPU duty-cycle throttle at startup |
| `--warm-weights` | Pre-touch weights before serving |
| `--dir-steering-file <path>`, `--dir-steering-ffn/-attn <-100..100>` | Activation-steering vector injection |

Unrecognized flags print an error + usage and `exit(2)`. After the loop, `ds4_dist_prepare_engine_options()` finalizes distributed config; if the resolved role is `DS4_DISTRIBUTED_WORKER` the agent refuses to start (`ds4_agent.c:754`) — serving mode belongs to the separate `ds4` server binary, not the interactive agent.

### 3.2 `main()` (`ds4_agent.c:11121-11185`)

1. `parse_options`, optional `chdir`.
2. Copies `ctx_size` into the engine options.
3. Opens the engine: if `--gpu-vram`/`--gpu-devices` were given, resolves a GPU config via `ds4_gpu_args.h` and calls `ds4_engine_create_with_gpu_config` (printing the resolved GPU layout first); otherwise plain `ds4_engine_open`.
4. Applies GLM-DSA sampling defaults.
5. Installs `SIGINT → agent_sigint_handler` **only in interactive mode** — non-interactive mode leaves the default SIGINT disposition, so Ctrl-C kills a scripted run immediately rather than trying to interrupt gracefully.
6. Dispatches to `run_agent_non_interactive()` or `run_agent()`.
7. Restores the old SIGINT handler, closes the engine, returns the worker's exit code.

---

## 4. Non-interactive mode (`run_agent_non_interactive`, `ds4_agent.c:10544-10693`)

Headless mode is architecturally just another front-end driving the same worker (`ds4_agent.c:10539`). Two sub-modes:

- **One-shot** (`-p` given): submits the prompt once the worker reports initialized, loops until idle again, exits.
- **Stdin protocol** (`-p` absent): sets stdin non-blocking, announces `+DWARFSTAR_WAITING` on stderr once idle-and-empty, accumulates stdin into a growable buffer, and treats **200ms of silence or EOF** as "the prompt is complete" (`ds4_agent.c:10627`). If the worker is busy when a prompt completes, it's pushed onto the prompt queue and `+DWARFSTAR_QUEUED` is announced — the protocol supports pipelining multiple prompts while a turn is in flight.

Each loop iteration `poll()`s `wake_fd[0]` (and stdin, if in protocol mode), drains the wake pipe, reads available stdin, calls `worker_consume()` and writes any produced bytes straight to stdout, checks for `AGENT_WORKER_ERROR` (prints to stderr, sets rc=1, breaks), and answers any `worker_take_queued_user_drain_request` (the worker asking to fold queued input into context). Loop exit: one-shot done-and-idle, or stdin EOF with nothing left pending. A final `worker_consume()` drains trailing output before teardown.

---

## 5. The UI↔worker concurrency protocol

This is the mechanical core of the program's thread safety, and the part most worth reading carefully if you intend to modify the file.

### 5.1 Three synchronization primitives, three different jobs

All three live on `agent_worker`:

1. **`pthread_mutex_t mu`** — protects essentially all cross-thread state: `out`/`out_len`/`out_cap` (streamed output buffer), `status`, `cmd_text` (pending user turn), the `interrupt`/`stop`/`save_requested`/`compact_requested`/`power_requested` flags, `wake_pending`, and even the trace `FILE*` (trace writes take `mu` too — `ds4_agent.c:1291-1303` — reusing the handoff lock rather than adding a dedicated one, a minor design shortcut rather than a bug since trace writes are short).
2. **`pthread_cond_t cond`** — the worker thread's sleep/wake mechanism in its main loop (`pthread_cond_wait`, `ds4_agent.c:8988-9024`). The UI thread signals it whenever it sets `cmd_text`, or a save/compact/power/stop request (`worker_submit`, `worker_request_save`, `worker_request_compact`, `worker_request_power`, `worker_stop`).
3. **`int wake_fd[2]`** — a non-blocking self-pipe (`pipe()` + `O_NONBLOCK`, set up in `agent_worker_init`, `ds4_agent.c:10336-10339`). This exists for a *different* reason than `cond`: the UI thread's main loop must block on `poll()` over **two** sources at once — `STDIN_FILENO` and `wake_fd[0]` (`ds4_agent.c:10746-10752`) — and a condition variable cannot be waited on together with a file descriptor inside `poll()`. So the worker writes one byte to `wake_fd[1]` any time it changes something the UI needs to notice. `agent_wake_locked()` (`ds4_agent.c:1202-1208`) is idempotent per "batch" via the `wake_pending` flag, so a burst of token-by-token publishes coalesces into a single wake byte; `worker_consume()` clears `wake_pending` when it drains output (`ds4_agent.c:9139`). The UI thread reads (drains) the pipe with `drain_wake_fd()` (`ds4_agent.c:9056-9064`) after `poll()` reports readability. Because both the set and clear of `wake_pending` happen under `mu`, no wakeup is ever lost: a race either lands before the UI's consume (coalesced into the pending byte) or after (a fresh byte is written, caught on the next `poll()`).

### 5.2 State machine

`IDLE → PREFILL → GENERATING → (COMPACTING/DRAINING/SAVING) → IDLE/ERROR/STOPPED`. `worker_submit()` (UI thread, under `mu`) flips `IDLE → PREFILL` **the instant a turn is accepted**, before the worker thread has even woken up — deliberately, so a caller can never observe a false "still idle" during the handoff gap (`ds4_agent.c:9073-9077`). Every other transition (`GENERATING`, `COMPACTING`, `SAVING`, `ERROR`, `STOPPED`) is set exclusively by the worker thread via `agent_set_status`/`agent_set_error` (`ds4_agent.c:1255-1274`). `DRAINING` is the one exception set by the UI thread, inside `worker_interrupt()`, only in the distributed-coordinator case (unwinding remote nodes).

### 5.3 Publish / consume — how streamed text crosses threads safely

The worker appends generated text to `w->out` under `mu` via `agent_publish`/`agent_publishf` (`ds4_agent.c:1212-1251`). The UI thread's `worker_consume()` (`ds4_agent.c:9126-9141`) **swaps** `out`/`out_len`/`out_cap` to `NULL`/`0`/`0` under the same lock and returns the old pointer — a full ownership transfer, not a copy and not a read of live worker state. There is no torn-read hazard: the worker either hasn't started refilling the (now-fresh) buffer, or has, but the UI thread already owns the previous one outright. `status` is copied by value in the same critical section, so text and status observed together in one `worker_consume()` call are mutually consistent as of that instant. (Because `agent_publish` and `agent_set_status` are separately-locked calls, a status transition and its "final" associated text can occasionally land in different `worker_consume` batches — benign eventual consistency, never corruption, since relative ordering is preserved.)

### 5.4 Interrupt (Ctrl-C / ESC) and stop

SIGINT is installed only in interactive mode; its handler (`ds4_agent.c:376-379`) does the absolute minimum required of a signal handler: sets `volatile sig_atomic_t agent_sigint = 1`, a **single process-global flag**, not per-worker. The UI loop polls and clears it every iteration: if the worker is idle, it just cancels the in-progress input line; otherwise it calls `worker_interrupt()`, which sets `w->interrupt = true` under `mu` and signals. The worker never touches `agent_sigint` directly — it polls `w->interrupt`/`w->stop` via `worker_should_interrupt()` and `worker_cancel_session_cb()` (the latter passed into the engine as its own cancellation callback, checked at generation/tool-execution checkpoints). `worker_clear_interrupt()` is called once an interrupted operation reaches a safe boundary, specifically to avoid the UI observing an `IDLE` worker with a stale pending-interrupt flag.

Ctrl-C is caught **twice, redundantly, on purpose**: because linenoise runs the terminal in raw mode, byte `0x03` also arrives directly as ordinary stdin data, and the main loop special-cases it independently of the SIGINT path (`ds4_agent.c:10770-10776`) — this reacts faster than waiting for kernel signal delivery when a large output backlog is queued for draining.

`w->stop`/`worker_stop()` follows the identical request/flag/signal pattern and causes `worker_main`'s loop to exit and the thread to terminate; `agent_worker_free()` then joins it.

### 5.5 Deferred requests (save / compact / power)

Slash commands issued while the worker is busy don't touch engine/session state directly — they call `worker_request_save()` / `worker_request_compact()` / `worker_request_power()`, each of which just sets a flag, `cond_signal`s, and wakes. The worker's main loop executes `worker_run_deferred_save` / `worker_run_deferred_compact` / `worker_apply_pending_power` either at the top of its loop (no pending user turn) or right after finishing a turn — always on the worker thread itself, at a point where generation isn't in flight, so session/KV mutation is never concurrent with itself or with a live generation.

### 5.6 Hazards actually verified (not speculative)

- **Global `agent_sigint` vs. per-worker design**: fine today (exactly one worker per process) but a latent coupling if the binary were ever extended to host multiple workers.
- **No missed-wakeup race**: `wake_pending` is only ever set/cleared under `mu`, so the self-pipe coalescing is provably race-free as analyzed in §5.1.
- **`agent_trace` sharing `mu`**: couples trace-file I/O latency to the same lock used for output publishing (lock held across `fprintf`+`fflush`). A design wart, not a correctness bug, given all call sites are short.

---

## 6. Interactive mode: `run_agent()` (`ds4_agent.c:10698-11118`)

Setup: `agent_worker_init`; loads `~/.ds4_agent_history` into linenoise; enables multi-line editing; registers a session-switch tab-completion callback (`agent_switch_completion_callback`) via a global `agent_completion_worker` pointer; calls `editor_start()` (§10) to establish the scroll-region terminal layout — the code comment there explains this exists precisely so *"streaming tokens do not require repainting the bottom rows"* and every terminal write funnels through one function (`editor_write_async`) so linenoise/status/model-output/tool-output can never race each other on the real terminal. Prints a welcome banner; queues the initial `-p` prompt if given.

### 6.1 Main loop, per iteration

1. `worker_check_raw_mode_restore()` — if a bash child process left the tty in cooked mode, re-enables linenoise raw mode (see §10.7).
2. `poll()` on `{STDIN_FILENO, worker.wake_fd[0]}` — timeout 0 if the editor has queued input awaiting replay, else 100ms (the UI's redraw/animation polling floor).
3. Checks and clears the global `agent_sigint`; routes to cancel-input-line (idle) or `worker_interrupt()` (busy) as described in §5.4.
4. Reads any available stdin into the editor (also intercepting a raw Ctrl-C byte the same way).
5. Drains `wake_fd`.
6. `worker_consume()` → rebuilds prompt/footer text → `editor_write_async()` (force-shows the prompt on `IDLE`/`ERROR`/`STOPPED`, otherwise just updates status text). On `AGENT_WORKER_ERROR`, prints the error inline, then **directly locks `worker.mu`** to reset state to `IDLE` and clear the error field — the one deliberate exception in the whole file to "only the worker thread mutates its own state" (`ds4_agent.c:10803-10808`).
7. Services `worker_take_queued_user_drain_request` and `worker_take_web_approval_request` — the latter fully tears down and rebuilds the line editor around a blocking yes/no confirmation dialog (`agent_prompt_yes_no_ex`, 30s timeout defaulting to "no") before a web tool call is allowed to proceed (§8.3).
8. Submits the initial/queued prompt once the worker goes idle; Ctrl+X (byte 24) pops the front of the prompt queue back into the edit line; a bare ESC with a non-empty queue while busy triggers `worker_interrupt()`.
9. Feeds queued editor input through `linenoiseEditFeed`; on a completed line, dispatches slash commands or submits/queues plain text.

### 6.2 Slash commands (dispatch at `ds4_agent.c:10926-11075`; validity gate `agent_slash_command_known`, `ds4_agent.c:491-504`)

| Command | Effect |
|---|---|
| `/help` | Prints the static command list (`runtime_help`) |
| `/save` | Saves the session now if idle, else deferred |
| `/compact` | Requests context compaction (immediate if idle, deferred if busy) |
| `/list` | Lists saved sessions on disk |
| `/power <1..100>` | Sets GPU duty-cycle percentage (deferred request) |
| `/switch <sha-prefix>` | Saves current session if needed, loads another session by SHA prefix, replays recent history |
| `/del <sha-prefix>` | Deletes a saved session |
| `/strip <sha-prefix>` | Strips a session's heavy KV payload on disk, keeping metadata only (`/switch` transparently rebuilds via re-prefill) |
| `/history [N]` | Reprints the last N user turns |
| `/new` | Saves if needed, resets to a fresh session at just the system prompt |
| `/quit`, `/exit` | Tears down editor/terminal layout, offers to save, exits |
| Unknown `/xxx` | Terminal bell; text restored to the input line rather than submitted |
| Any `/xxx` (except the always-deferrable ones) while busy | Rejected: "command requires the model to be idle" |
| Plain text | Added to linenoise history, submitted immediately if idle, else queued |

The UI thread only ever touches worker internals through a small public API — `worker_submit`, `worker_consume`, `worker_get_status`, `worker_is_idle`, `worker_is_initialized`, `worker_interrupt`, `worker_request_save/compact/power`, and the request/answer pairs for queued-drain and web-approval — with the single locked-error-clear exception noted in step 6 above.

---

## 7. Tool-calling protocol: DSML and GLM streaming parsers

The model doesn't call tools through a structured API — it emits its tool calls as **literal text inside its token stream**, using one of two tag-based mini-languages, and the agent has to recognize and parse that syntax *while simultaneously rendering the surrounding prose to the terminal live, byte by byte, before the call is even known to be complete*. This is the most intricate state machine in the file.

### 7.1 Two wire formats

`agent_tool_syntax_for_engine()` (`ds4_agent.c:349-352`) picks the format from the loaded model: GLM-family models use **GLM-native** syntax, everything else (DeepSeek models) use **DSML**.

- **DSML** — uses the full-width vertical bar `｜` (U+FF5C) as a literal control-marker character, e.g.:
  ```
  <｜DSML｜tool_calls>
  <｜DSML｜invoke name="read">
  <｜DSML｜parameter name="path" string="true">ds4_agent.c</｜DSML｜parameter>
  </｜DSML｜invoke>
  </｜DSML｜tool_calls>
  ```
- **GLM** — an OpenAI-function-call-flavored XML dialect:
  ```
  <tool_call>bash<arg_key>command</arg_key><arg_value>printf hi</arg_value></tool_call>
  ```
  Tool name is bare text right after `<tool_call>`; args are `<arg_key>/<arg_value>` pairs. The advertised tool set in this mode (`agent_build_glm_tools_prompt`, `ds4_agent.c:1079-1090`, schemas at `~1041-1077`) is: `google_search`, `visit_page`, `bash`, `bash_status`, `bash_stop`, `read`, `more`, `write`, `edit`, `search`, `list`. The rules text explicitly forbids tool calls inside `<think>` blocks and documents the `[upto]` edit-anchor marker and bash-job polling via `refresh_sec`/`bash_status`.

`agent_append_system_prompt()` (`ds4_agent.c:1124-1146`) tokenizes the DSML tools-prompt through the engine's *rendered-chat-template* tokenizer (so the literal `｜DSML｜` bytes become the model's dedicated control token), but sends the GLM variant as an ordinary `system` chat message. A comment at `ds4_agent.c:1126-1130` explicitly warns that user-supplied `-sys` text must **never** go through the control-token path, precisely to avoid a prompt-injection vector where user text containing `<｜User｜>`/`<think>`/`｜DSML｜` gets misinterpreted as control markup.

### 7.2 Layer 1 — `agent_dsml_parser`: a pure state machine

States: `SEARCH → STRUCTURAL ⇄ PARAM_VALUE → DONE / ERROR`. Fed one byte at a time by `agent_dsml_feed()` (`ds4_agent.c:1858-1895`):

- In `SEARCH`, a 64-byte tail ring (`search_tail`) looks for the start marker; once matched, `agent_dsml_start()` seeds the internal `raw` buffer with the canonical start bytes and moves to `STRUCTURAL`.
- `agent_dsml_parse()` (`ds4_agent.c:1766-1848`) dispatches to `agent_glm_tool_parse()` for GLM, or does inline tag scanning for DSML: it looks for close tags via `agent_dsml_close_tag_at()` (tolerant of missing-bar/whitespace variants) or classifies a generic `<...>` open tag as `invoke` (captures `name=""`) or `parameter` (captures `name`/`string`, entering `PARAM_VALUE`).
- In `PARAM_VALUE`, it searches for the matching close tag — tolerant of some malformed close-tag variants while open tags stay strict, a deliberate asymmetry (comment `ds4_agent.c:1592-1594`) — and stores the raw byte span as an arg via `agent_tool_call_add_arg()`.
- The GLM path (`agent_glm_tool_parse()`, `ds4_agent.c:1615-1744`) is structurally identical but keyed on the literal `<tool_call>/<arg_key>/<arg_value>/</tool_call>` tags and loops (`glm_after_call`) to accept multiple adjacent tool-call blocks in one stream — confirmed by unit tests at `ds4_agent.c:6743` (multiple adjacent calls) and `6765` (a call chunked arbitrarily mid-tag across separate model-output chunks, e.g. split as `"...printf hi</arg"` + `"_value>..."`, and still parses correctly).
- Completed calls are appended to `parser.calls`. Malformed input (missing name, unterminated tag, unexpected tag) sets state `ERROR` via `agent_dsml_set_error()` — this is **retryable, not fatal**: the run doesn't abort; the malformed text is surfaced to both the user (in red) and, per the code's own comment, back to the model as a tool error it can retry.
- **Ambiguous-tail handling**: paired helpers `agent_bytes_starts_with`/`agent_bytes_partial_prefix_at` and syntax-specific tail matchers (`agent_dsml_parameter_close_tail`, `agent_glm_arg_value_close_tail`) let the parser distinguish "not enough bytes yet, wait" from "definitely not a close tag, flush as literal" from "complete, act now." While a close-tag prefix is ambiguous (`param_close_prefix`), `agent_stream_wants_greedy_sampling()` (tested `ds4_agent.c:6817-6862`) forces **deterministic (greedy) sampling** so the model's next token resolves the ambiguity toward the real close tag rather than sampling something that would corrupt the parse mid-tag — a small but important correctness trick given text is being generated token-by-token, not written all at once.

### 7.3 Layer 2 — `agent_stream_renderer` / `agent_stream_text()`: coexistence with live rendering

`agent_stream_text()` (`ds4_agent.c:3768-3872`) is the actual per-generated-chunk entry point, and this is what lets tool-call detection and prose rendering share one byte stream without either corrupting the other:

- Buffers a small pending tail (≤16 bytes) across calls so a start marker split across chunks/tokens is never missed.
- Tracks `<think>`/`</think>` transitions independently of DSML state, and suppresses the stray blank-line gap that would otherwise appear right after `</think>`.
- Outside an active DSML block, each byte goes through `agent_stream_normal_byte()`, which accumulates candidate start-marker bytes and classifies them (complete / partial / no-match) via `agent_stream_dsml_start_match()` — including tolerant variants like a missing bar or a bare `<...DSML｜invoke` treated as an implicit `tool_calls` wrapper. If a candidate tail turns out not to be a real marker, the buffered bytes are flushed as ordinary rendered prose — text that merely *starts* with `<` is never silently dropped.
- Independently, an `agent_dsml_marker_detector` (a 32-byte ring) watches for **fragment** markers appearing where they shouldn't: inside `<think>`, it flags "tool call attempted too early" (reported once thinking closes); outside think and outside an active DSML block, a stray DSML-looking fragment in normal prose immediately raises a malformed-DSML error even if it never becomes a complete tag.
- Once a start marker is confirmed, every subsequent byte routes through the parser (`agent_dsml_feed`), and parser progress is mirrored live into the **tool visualizer** (`agent_stream_tool_events()`): the tool name is announced the instant it's known (e.g. rendering `Read(` before the path argument has even finished streaming), and parameters are colorized by kind (`PATH`, `OFFSET`, `CONTENT`, `DIFF_OLD`, `DIFF_NEW`, `BASH_COMMAND` each render differently — diff-old/new get diff-style coloring). On parser `DONE`, the visualizer is finalized and `dsml_active` clears, leaving a fully-populated `agent_tool_call` ready for execution. On parser `ERROR`, the raw rejected bytes are dumped visibly (in red) before finalizing with the error status — visible and debuggable rather than silently swallowed.
- **Live preflight validation**: the instant an `edit` tool's `old` parameter closes — *before the model has even finished generating `new`* — `agent_stream_preflight_closed_param()` calls `agent_preflight_edit_old()` to check the anchor text is a unique match in the target file, so a doomed edit can be flagged early instead of only failing after a full (possibly large) generation completes.

### 7.4 `<think>` and tool calls

Per unit test `test_agent_glm_stream_ignores_tool_inside_think` (`ds4_agent.c:6794-6815`): a complete, well-formed tool call fully inside a `<think>...</think>` block parses structurally but is **discarded** — the stream renderer prints `[tool call ignored: tool calling is not allowed inside <think></think>]`, resets the parser, and `calls.len` stays 0. The model is allowed to "think about" calling a tool, but only a call emitted outside `<think>` is ever executed.

### 7.5 End-to-end path

`engine emits next token/chunk` → `agent_stream_text()` → per-byte dispatch through think-tracking and marker detection → `agent_dsml_feed()` advances the parser, appending to its internal buffer and re-parsing → on a complete well-formed call, it's pushed into `parser.calls` → parser reaches `DONE` → the visualizer finalizes and clears `dsml_active` → the populated `agent_tool_calls` vector is handed to `agent_execute_tool_calls()` (`ds4_agent.c:7972-7990`), which dispatches each call by name through `agent_execute_tool_call()` (`ds4_agent.c:7910-7968`) to its handler (§8).

---

## 8. Tool execution

All tool handlers run **on the worker thread**, synchronously with respect to the generation loop (the model waits for the tool result before continuing), with the single exception of long-running `bash` jobs which can be left running and polled across multiple turns (§8.2).

### 8.1 Filesystem tools

- **`read` / `more`** (`agent_tool_read`, `agent_tool_more`, `agent_read_range` — `ds4_agent.c:6081-6172`): `agent_read_range()` loads the whole file (`agent_read_file_bytes`, capped at `AGENT_FILE_MAX_BYTES`), splits it into line spans (CRLF/LF-aware), and slices `[start_line, start_line+max_lines)`. Two output modes: line-numbered ("decorated", default) or raw ("bare", for payloads where numbering would corrupt content). The default window size scales with the model's context size (`agent_read_default_lines`). If a read is truncated, `agent_worker_set_more()` stashes a **single-slot** continuation cursor (`more_path`/`more_next_line`/`more_bare`) directly on the worker struct — only one pending "more" position exists at a time; a subsequent `read` on a different file silently overwrites it. Result size is separately capped against remaining context budget (`agent_tool_result_fits_context`/`agent_tool_result_reserve_tokens`, reserving `max(ctx/8, 16)` tokens of headroom).
- **`write`** (`agent_tool_write`, `ds4_agent.c:6174-6201`): `fopen(path,"wb")` → `fwrite` → `fclose`. **Not atomic** (no temp-file+rename, no `O_EXCL`, no `fsync` — `fopen("wb")` truncates on open, so a crash or concurrent reader mid-write can observe a partial file). **No parent-directory creation.** **No conflict detection** — no mtime/hash check against a previously-read version, so a concurrent external edit to the same file is silently clobbered.
- **`edit`** (`agent_tool_edit`, `ds4_agent.c:7034-7075` + helpers `6333-6559`, `6970-7028`): an anchor-based old→new text splice, deliberately conservative rather than fuzzy:
  - *Plain mode*: `old` must occur **exactly once** in the file (`agent_find_unique`, a naive O(n·m) substring search). Zero matches → `"old text not found"`; more than one → `"old text is not unique"`. Both are hard errors with no best-effort fallback.
  - *`[upto]` mode*: `old` may contain one literal `[upto]` token splitting it into head/tail. The head must be globally unique; the tail must be uniquely findable **after** the head (`agent_find_unique_after`) — deliberately allowing the tail string to also appear earlier in the file. This lets the model express "replace from this known head down to this known tail" for large spans without retyping the unchanged middle. A whitespace-only tail, or more than one `[upto]` marker, is rejected.
  - **The "upto forcer"**: driven live from the streaming parser as `old` is generated token-by-token — once the emitted prefix is "mature" (long enough, ends on a full line, contains no `[upto]` already) **and** that prefix already uniquely identifies a spot in the file, the streaming layer injects a synthetic `[upto]` marker for the model instead of letting it keep reproducing a long unchanged block verbatim. This saves generation tokens and avoids transcription drift on large edits. `agent_preflight_edit_old()` validates the final `old` resolves to a real span before the edit is actually applied (tying back to §7.3's live preflight).
  - **Apply**: `agent_apply_file_splice()` builds the entire new file contents in memory and writes it via the same non-atomic `agent_write_file_bytes()` as the `write` tool — no locking, no fsync here either.
  - **Result reporting**: echoes back a few lines of context before/after the touched region with corrected line numbers, specifically so the model can visually verify shifted braces/semicolons without a follow-up full re-read.
- **`list`** (`agent_tool_list`, `ds4_agent.c:6203-6238`): non-recursive `opendir`/`readdir`, capped at 300 entries; uses `lstat` (symlinks reported as type `l`, not followed).
- **`search`** (`agent_tool_search`, `ds4_agent.c:7204-7251` + helpers): recursive walk, depth-limited to 24, skips `.git`, stops at `max_results` (1–500, default 50). Per file: optional glob filter (`fnmatch`, matched against basename and full path), binary-file skip via a null-byte heuristic, matching is literal substring or POSIX extended regex (`regcomp`/`regexec`), applied per line, with an optional 0–5 line context window.

**File-safety finding, stated plainly**: there is **no file locking** anywhere in this subsystem (no `flock`/`fcntl` byte-range locks — verified by direct inspection of every write path), **no atomic write** for `write`/`edit` (unlike the session-persistence code in §9, which *does* use temp-file+rename), and **no staleness check** (no mtime/hash comparison between when a file was read and when it's subsequently written). The `edit` tool's uniqueness guarantee is only as fresh as the read a few lines earlier in the same function call — nothing prevents another process (a human editor, a second agent instance, a build script) from changing the file in between. Two concurrent writers to the same path can interleave/clobber with no detection.

### 8.2 Bash tool: subprocess management

The `agent_bash_job` struct (near `ds4_agent.c:7416-7436`), one node in a singly-linked list rooted at `worker->bash_jobs`, tracks: `id`/`pid`, `pipe_fd` (read end of the child's merged stdout+stderr), `tmp_fd`/`path` (a `mkstemp` spool file mirroring all output to disk), the original `cmd` string, timing/timeout, running byte/newline counters, a **per-job observed-cursor** (`observed_bytes`, `observed_display_lines`) so repeated status polls report only new output, and the final `exit_status`.

**Launch** (`agent_bash_start`, `ds4_agent.c:7575-7645`): creates the spool file and a pipe, then `fork()`. Child: `setpgid(0,0)` (its own process group — important for clean group-kill later), stdin redirected to `/dev/null` (explicitly to stop the child from resetting the live raw-mode terminal behind the agent's back if it happens to open `/dev/tty`), stdout+stderr both `dup2`'d onto the pipe, `execl("/bin/sh","sh","-c",cmd,NULL)`, `_exit(127)` on exec failure. Parent: sets the pipe non-blocking, also calls `setpgid(pid,pid)` (belt-and-braces against the fork/exec race), and links the new job into the worker's list. This all happens on the **worker thread**.

**Output capture** (`agent_bash_drain`): non-blocking reads off the pipe until `EAGAIN`/EOF, updating counters and mirroring to the spool file — never blocks the thread.

**Completion detection — no SIGCHLD, no reaper thread.** There is exactly one `fork()` call site in the whole file. All reaping is explicit `waitpid(..., WNOHANG)` inside `agent_bash_poll()`, called *opportunistically* — from every `bash`/`bash_status`/`bash_stop` tool invocation and from the compaction path — rather than from a background reaper. The code's own comment states this is deliberate: keeping bash-job state single-owner (worker thread only), with no locking needed around `bash_jobs` itself since only that thread ever touches it. `agent_bash_poll` also enforces per-job timeouts by killing the whole process group (`-pid`) then the pid with `SIGKILL` and blocking-`waitpid`ing once the deadline passes.

**Head/tail truncation for the model's context**: first observation of a job returns up to ~100 lines / 8KiB from the *head* of the spool file (so early errors/headers are visible); subsequent progress/final observations return only the last few (running) or last 20 (done) lines from the *tail*, read through a bounded 32KiB ring buffer — keeping bash output from blowing the context window regardless of how much a command actually printed.

**Tool dispatch**: `bash` starts a job, then blocks (polling/sleeping) up to `refresh_sec` (default 60, 1–3600) for it to finish or produce a progress snapshot; `bash_status` looks up an existing job by `job=<id>` or `pid=<pid>` and refreshes without signaling it; `bash_stop` looks it up, sends `SIGTERM` to the group then the pid, waits up to 1s polling every 20ms, escalates to `SIGKILL` if still alive. A job is removed from the list only once it's finished *and* a call observed that (i.e., a job that hits its `refresh_sec` window while still running stays in the list for a later `bash_status`/`bash_stop`).

**Concurrency scope**: multiple bash jobs can genuinely run concurrently with each other (OS-scheduled, independent processes) and with the model still "thinking" between tool calls, but the agent's *observation* of them is cooperative and single-threaded — polled only at tool-call sites on the worker thread, never event-driven.

**Verified zombie/leak posture**: every path that marks a job non-running does so only after a successful `waitpid` reaps it, except a hard-`waitpid`-error branch (e.g. `ECHILD`) which is benign because `ECHILD` itself means the child is already gone. `agent_bash_job_free()` is the last line of defense — if a job is freed while still marked running (e.g. at process shutdown via `agent_bash_jobs_free`), it `SIGKILL`s the group/pid and does a **blocking** `waitpid` before freeing, so no code path frees a job struct while leaving a pid unreaped. One minor, real, non-crashing leak: the `mkstemp` spool files are unlinked only on the two *setup-failure* paths in `agent_bash_start` — completed jobs' `/tmp` spool files are not observed being removed on the completion/free paths, i.e. they accumulate on disk over a long session.

`google_search` / `visit_page` do **not** fork/exec — they call into `ds4_web_google_search`/`ds4_web_visit_page` (implemented outside this file, presumably libcurl-based) as ordinary synchronous worker-thread calls; `visit_page` spools its rendered Markdown to a temp file for later `read` access using the same head-truncation pattern as bash output.

### 8.3 Web tool approval handshake

Web tools (`google_search`, `visit_page`) require live user confirmation before running (presumably to avoid an unsupervised agent silently exfiltrating data or hitting arbitrary URLs). The worker thread sets `web_approval_pending` + a message under `mu` and blocks; the UI thread's main loop notices via `worker_take_web_approval_request()`, tears down the line editor, shows a blocking yes/no prompt (`agent_prompt_yes_no_ex`, 30s timeout defaulting to **deny**), and calls `worker_answer_web_approval()` to hand the boolean back — another instance of the request/flag/wake pattern from §5.5, just synchronous from the worker's point of view (it blocks on the answer rather than continuing and polling later).

---

## 9. Session persistence: on-disk KV-cache

### 9.1 Layout and format

Sessions live under `~/.ds4/kvcache` by default (`agent_default_cache_dir`, `ds4_agent.c:3996-4004`, falling back to `.` if `$HOME` is unset). Two kinds of files share the directory:

- **`sysprompt.kv`** — a fixed-name bootstrap checkpoint for the current system/tools prompt. Because the name is fixed, loading it always passes the freshly-rendered system-prompt text as `expected_text`; any mismatch (tool set changed, `-sys` changed, different model) is treated as a cache miss and silently rebuilt.
- **`<sha40>.kv`** — explicit conversation saves, named `SHA1(title || created_at_le64)` (`agent_session_identity_sha`, `ds4_agent.c:4020-4030`). Session identity is deliberately independent of the transcript content, so resaving a growing conversation keeps the same filename.

The physical file format is owned by `ds4_kvstore.h`: a fixed 48-byte header (model_id, quant_bits, reason code, ext_flags, token count, hit count, ctx_size, timestamps, payload size) followed by a length-prefixed rendered-text key (used for prefix-matching in the generic server-side KV cache) and then the opaque backend payload. `ds4_agent.c` layers one extra piece on top: an optional **title trailer** (4-byte length + UTF-8 title) appended after the payload, gated by a `DS4_KVSTORE_EXT_SESSION_TITLE` flag, read/written by `agent_kv_write_title_trailer`/`agent_kv_read_title_trailer` (which seek past the payload and restore the cursor afterward so they don't disturb the payload-loading code path).

Identity verification branches on that flag: modern agent sessions hash `title||created_at`; legacy/untitled files (including `sysprompt.kv`) hash the rendered text instead. This produces a `legacy_identity` migration path: saving a legacy session under its new title-based SHA leaves the old text-hashed file behind, and the next successful save unlinks it (`legacy_session_path_to_delete`, `ds4_agent.c:115`).

### 9.2 Save — atomic via temp-file + rename, no locking

`agent_kv_save_path()` (`ds4_agent.c:4260-4376`) is the single save primitive for both `sysprompt.kv` and session files:

1. Re-verifies the live KV tokens equal the caller's transcript (refuses to save a stale/mismatched session).
2. Rejects unsupported quantization, renders the text key, computes the identity SHA.
3. Stages the backend payload, then writes to **`path.tmp.XXXXXX`** created by `mkstemp`.
4. Writes header + text key + payload + title trailer, `fflush`, `fclose`.
5. **Only on full success**, `rename(tmp, path)` — the classic atomic-replace pattern.
6. On any failure, `unlink`s the scratch file and leaves the previous `path` completely untouched.

There is **no `fsync`** before the rename, so on a hard crash/power loss the rename could in principle be reordered ahead of the data hitting disk on some filesystems — a narrow durability gap, not a corruption gap under an ordinary process kill (the rename is still atomic; you just might get the *old* file back, never a torn one).

Because saves always go through temp-then-rename, **two writers targeting the same final path cannot corrupt each other or produce a torn file** — whichever `rename()` lands last simply wins, and every reader always sees one complete, self-consistent generation of the file. There is, however, **no locking anywhere in this subsystem** (verified: no `flock`/`lockf` usage in the file) and no read-before-overwrite conflict check — so two `ds4_agent` processes saving the same session concurrently is *safe from corruption* but *silently last-write-wins* (no merge, no error).

Sessions are only ever saved **explicitly** (`/save`, on `/switch` away, `/new`, `/quit`, or a dirty-check gate `agent_worker_needs_save` = `user_activity && session_dirty`) — never auto-saved mid-turn — and always via the deferred-request mechanism (§5.5) when the UI-thread caller is busy, so the actual disk write always happens on the worker thread at a point where it isn't concurrently generating.

### 9.3 Load — fail-closed verification

`agent_kv_load_path()` (`ds4_agent.c:4158-4256`) opens read-only, reads the header, text key, and optional title trailer, then validates in order: model ID match, quant-bits match, (if given) byte-exact text match, (if given) recomputed identity-SHA match. If `payload_bytes == 0` (a *stripped* session — see below) it re-tokenizes the rendered text and rebuilds the live KV via a full prefill; otherwise it restores the opaque backend payload directly. Either way, it double-checks the resulting live token count against the header's stored count, and calls the engine's session-invalidate path on **any** failure — a partially-loaded or corrupt file can never leave the session in a half-loaded, inconsistent state.

### 9.4 Strip / switch / delete / list

- **`/strip <sha>`** rewrites a session file in place (same mkstemp+rename pattern) with `payload_bytes=0`, dropping the heavy backend KV blob but keeping the rendered text and title — shrinking disk usage while remaining "resumable."
- **`/switch <sha-prefix>`** detects a stripped (`payload_bytes==0`) session and transparently rebuilds it via full re-prefill (printing a "rebuilding stripped session..." notice), otherwise loads the payload directly; it then replaces the worker's transcript and session-identity fields under `mu`.
- **`/del <sha-prefix>`** is a plain `unlink()`.
- **`/list`** enumerates the cache directory via `readdir`.

None of delete/list/strip/switch coordinate with each other beyond what atomic-rename-based writes already provide; a concurrent `/list` mid-delete can race an ordinary directory-scan TOCTOU (a listed entry disappearing before you open it), which is the normal, harmless kind given every read is still of a complete, atomically-written file.

### 9.5 Context compaction (`ds4_agent.c:8022-8292`)

**Trigger** (`agent_worker_should_compact`): the transcript reaches 85% of context capacity, or free space drops to ≤ `min(8192, ctx/8)` tokens. This is purely in-memory/live-KV, orthogonal to explicit disk saves (though it does mark `session_dirty` if it folds in a bash-job update, so a subsequent `/save` persists the compacted form).

**Algorithm**:
1. Builds a private prompt (tool-calls forbidden) instructing the model to emit a durable task-state summary.
2. Syncs that as a temporary continuation of the live KV.
3. Greedily samples up to 4096 tokens directly (bypassing the normal streaming/tool-call path) until a stop token or the DSML control-marker token appears.
4. Picks a tail-start index — roughly `ctx/10` tokens back (capped at 50000), snapped forward to the nearest user-turn boundary so the retained tail starts cleanly.
5. Rebuilds the transcript as `system tokens + summary-as-system-message + verbatim tail`.
6. Resyncs the live KV to the rebuilt transcript.

**Fail-closed invariant** (explicit in the code's own comment, `ds4_agent.c:8094-8097`): any failure *after* the private compaction prompt has already touched the live KV invalidates the session outright, rather than risking the real conversation silently continuing from a state that briefly saw the internal-only compaction instructions.

---

## 10. Terminal UI: the multiplexed editor

The `agent_editor` subsystem solves a genuinely hard terminal-UI problem: keep a persistent linenoise input line + status footer pinned at the bottom of the screen while an unbounded amount of model output scrolls above it, live, from a different thread's data — all without ANSI escape sequences from one stream corrupting the other, and without losing keystrokes the user typed while output was mid-flight.

### 10.1 Layout: DECSTBM scroll region

`editor_configure_scroll_region()` queries the real terminal size (`ioctl(TIOCGWINSZ)`), requires at least 8 rows / 20 columns and both stdin/stdout to be TTYs, then computes `reserved_rows` from linenoise's own rendered prompt height via a layout-callback hook that **linenoise itself invokes** whenever its prompt height changes (e.g. a multi-line queued-prompt footer growing) — this is how the reserved footer dynamically grows/shrinks. It writes `ESC[1;{bottom}r` to restrict scrolling to rows `1..output_bottom`, leaving the footer rows completely excluded from normal `\n` scrolling.

There is **no `SIGWINCH` handler** — resize is handled reactively rather than via a signal: the layout is recomputed (and `TIOCGWINSZ` re-read) every time the layout callback fires, i.e. polled-on-activity rather than signal-driven. When the footer grows into former output rows, old output is explicitly scrolled up to avoid being clobbered.

### 10.2 CPR (`ESC[6n`) — a fallback-mode cursor probe

Used only in the non-scroll-region fallback path (dumb terminals). Since a pty doesn't otherwise expose "what column is the cursor on," the agent queries it directly to decide whether the last line of streamed output needs a trailing `\r\n` before the prompt is redrawn below it. It writes `ESC[6n`, then polls stdin with short repeated timeouts; the reply-scanner (`find_cpr_reply`) looks for an embedded `ESC[row;colR` sequence anywhere in an arbitrary stdin chunk, and any real keystroke bytes surrounding it are re-queued — so typing during a CPR round-trip is delayed by at most a couple of polls, never lost or misinterpreted. A second, independent layer runs on *every* stdin read regardless of mode, speculatively buffering anything starting with ESC until it can be classified as a complete/invalid/partial CPR reply — this catches *late* replies that arrive after the original query already timed out, and silently discards them rather than letting `ESC[24;80R`-shaped garbage leak into the user's typed line.

### 10.3 `editor_write_async` — the single output funnel

Documented in its own comment as "the central terminal contract." In scroll-region mode, with the prompt currently visible, an update is wrapped in a terminal synchronized-update pair (`ESC[?2026h` / `ESC[?2026l`) so the redraw is atomic from the terminal's perspective (no visible tearing even over a slow link): it restores the saved output cursor, writes the new text (CRLF-normalized), re-saves the cursor, resets SGR, and moves back to linenoise's remembered prompt-cursor position. **The line the user is actively editing is never touched** — only the reserved footer rows are cleared/redrawn, and only when the prompt or status text actually changed, throttled to a minimum redraw interval unless forced. This is why streaming model tokens (many writes per second) don't cause visible prompt flicker or cursor jumps while typing.

### 10.4 Prompt queue

A plain FIFO array (`agent_prompt_queue`). Exists because the worker processes exactly one turn at a time; if the user submits while the worker is generating, the UI pushes onto this queue instead of calling `worker_submit`. Once the worker goes idle, all queued messages are concatenated with `"Queued user message N:\n"` headers into one combined turn. Ctrl+X is the one FIFO exception — it pops the *front* item back out into the live edit buffer, letting the user "unqueue" and correct the next-to-run message.

### 10.5 Bracketed paste and input filtering

The terminal's bracketed-paste envelope (`ESC[200~ ... ESC[201~`) is tracked independently of linenoise's own handling, because the outer loop reads stdin non-blockingly in arbitrary chunks — feeding partial pasted content into the line editor before the closing marker arrives would let embedded newlines in the pasted text be misread as pressing Enter. Bytes are buffered until a full envelope is recognized. The CPR-reply filter (§10.2) layers on top of the same input stream before anything reaches linenoise proper.

### 10.6 Status/footer content

The status line shows, per worker state: a Unicode block progress bar with a rotating (but per-operation-stable, to avoid visual churn) label during `PREFILL`; token count and tokens/sec during `GENERATING`/`COMPACTING` (with a small glyph appended when greedy sampling is being forced for tag-close disambiguation, tying back to §7.2); context used/total in all states; an optional GPU power-percent suffix. The footer adds an up-to-3-row preview of queued prompts above the status line when the queue is non-empty. All of this is rebuilt from scratch on essentially every poll iteration (cheap string formatting, no incremental diffing).

### 10.7 Startup/shutdown and interaction with bash jobs

`editor_start()` configures the scroll region, puts stdin in non-blocking mode, and optionally preloads initial text. `editor_stop()` explicitly hides/clears the live linenoise line first — linenoise here is treated as a transient input widget, not part of persistent scrollback — then restores the original stdin flags.

Tie-in with §8.2: when a bash child process is spawned or reaped, the bash-job code sets `worker->raw_mode_needs_restore = true` under `mu` (a child can open `/dev/tty` directly and alter terminal modes even with its stdin redirected to `/dev/null`). The UI main loop checks and clears this flag every iteration (`worker_check_raw_mode_restore`) and re-establishes linenoise raw mode when it fires — so a misbehaving shell command self-heals the terminal on the next loop tick rather than requiring the user to notice and fix broken input manually.

---

## 11. Cross-cutting concurrency summary

Three genuinely different concurrency domains exist in this program, each handled with a different mechanism and a different safety guarantee:

| Domain | Mechanism | Guarantee |
|---|---|---|
| UI thread ↔ worker thread (in-process) | `mu` mutex + `cond` condvar + `wake_fd` self-pipe; ownership-transfer buffer swap for streamed text; single-writer-to-terminal rule | Strong: no torn reads, no lost wakeups, no interleaved terminal writes — verified via direct code reading (§5) |
| Worker thread ↔ forked bash-job children | `fork`/`exec`, non-blocking pipes, explicit opportunistic `waitpid(WNOHANG)` polling, single-owner (worker-thread-only) job list | Strong for the agent's own bookkeeping (no races on `bash_jobs`, no unreaped pids on any exit path); weak for terminal state (a child can perturb tty modes, self-healed reactively, §10.7); minor spool-file leak on disk (§8.2) |
| Process ↔ process (two `ds4_agent` instances, or agent vs. external editor, sharing files/session cache) | **Session cache** (`~/.ds4/kvcache/*.kv`): atomic temp-file + `rename()`, fail-closed load validation, but **no locking** → safe from corruption, last-write-wins semantics only (§9.2). **Workspace files** (via `read`/`write`/`edit`/`search`/`list` tools): **no atomicity, no locking, no staleness detection at all** — direct `fopen("wb")` overwrites, races are entirely unmediated (§8.1) |

The practical takeaway for anyone extending this file: the in-process thread protocol is careful and well-reasoned; the on-disk *session* format is careful about atomicity but not about multi-writer coordination; and the on-disk *workspace files* the model edits have essentially no concurrency protection at all beyond what the filesystem gives you for free. If you were adding a feature like "run two agent instances against the same repo," the workspace-file gap (§8.1) is the one that would actually bite first.

---

## 12. Quick reference

### Tool names dispatched by `agent_execute_tool_call` (`ds4_agent.c:7910`)

| Tool | Handler | Notes |
|---|---|---|
| `read` | `agent_tool_read` | Line-range read, decorated or bare |
| `more` | `agent_tool_more` | Resumes the single-slot paging cursor |
| `write` | `agent_tool_write` | Full-file overwrite, non-atomic |
| `edit` | `agent_tool_edit` | Unique-anchor old→new splice, supports `[upto]` |
| `list` | `agent_tool_list` | Non-recursive directory listing |
| `search` | `agent_tool_search` | Recursive grep-like search (glob + literal/regex) |
| `bash` | via `agent_bash_start`/`agent_bash_job_tool_result` | Forks `/bin/sh -c`, blocks up to `refresh_sec` |
| `bash_status` | `agent_bash_job_tool_result` (no signal) | Polls an existing job by `job=`/`pid=` |
| `bash_stop` | `agent_bash_job_tool_result` (SIGTERM→SIGKILL) | Stops an existing job |
| `google_search` | `agent_tool_google_search` | Requires UI-thread approval (§8.3) |
| `visit_page` | `agent_tool_visit_page` | Requires UI-thread approval; spools rendered page to temp file |

### Key size/threshold constants encountered

- Compaction trigger: 85% of context, or ≤ `min(8192, ctx/8)` tokens free.
- Compaction summary cap: 4096 tokens.
- Bash head observation: ≤100 lines / 8KiB. Bash tail buffer: 32KiB (4 lines while running, 20 once done).
- Tool-result context reservation: `max(ctx/8, 16)` tokens.
- List tool cap: 300 entries. Search tool cap: 1–500 results (default 50), recursion depth 24.
- Web-approval dialog timeout: 30s, defaults to deny.

---

*Document scope: this covers `ds4_agent.c` only, treating `ds4.h`/`ds4_kvstore.h`/`ds4_web.h`/`linenoise.c` as external dependencies described only insofar as `ds4_agent.c` visibly relies on their behavior. Line numbers reflect the file as read on 2026-08-04 and should be re-checked if the file has since been edited.*

# Getting started with RedStar.WebApp

This is the hands-on runbook: commands, and troubleshooting, written assuming zero prior experience
with Node.js/pnpm/Vite. For the architectural *why* behind the choices mentioned here, see
[`CLAUDE.md`](CLAUDE.md) in this same folder — that file explains reasoning in depth; this one is
meant to be followed step by step.

## 1. When does my change actually take effect?

Read this before anything else — it's the single biggest source of "why isn't my change showing up"
confusion in a stack like this one.

| You edited... | What you need to do | Why |
|---|---|---|
| A `.ts`/`.css` file, or a Lit component | **Nothing — just save.** Browser updates instantly, no refresh. | Vite's dev server (HMR) pushes the change over its websocket the moment the file is saved. |
| A `.cshtml` view, while running `dotnet watch run` | **Nothing — save, then refresh the browser once.** | .NET Hot Reload (built into `dotnet watch`) recompiles the view and signals the browser. |
| A `.cs` file (Controller, `Program.cs`, ...), while running `dotnet watch run` | **Nothing — save.** Most edits apply in place within a couple of seconds; edits Hot Reload can't apply in place (e.g. adding a new method signature) trigger an automatic app restart — still no manual command. | .NET Hot Reload. |
| A `.cs` or `.cshtml` file, while running plain `dotnet run` (not `watch`) | **Stop the app (Ctrl+C) and run it again.** | Plain `dotnet run` does not watch for source changes at all — this is the #1 reason people think "nothing is working" when it's actually just the wrong command. |
| `ClientApp/package.json` / `pnpm-lock.yaml` (you added a library) | **Nothing manual** — the next `dotnet build`/`dotnet run`/`dotnet watch run` reinstalls automatically. | The `EnsureClientAppDependencies` MSBuild target. |
| Nothing, but you want to *see* the real, final, minified, hashed production bundle | Run `dotnet publish RedStar.WebApp -c Release -o <dir>` explicitly. | **This is the only thing that ever runs the frontend production build.** `dotnet build` and even Visual Studio's "Rebuild" never do. |

**The one rule to internalize**: `dotnet build` (and "Rebuild") only ever (re)compile C# and make
sure `ClientApp/node_modules` exists — they **never** run the frontend production build, on purpose
(doing so on every build would reintroduce the "rebuild everything" problem this whole pipeline
exists to avoid). If you've just run Build/Rebuild and don't see a `wwwroot/dist` folder with hashed
files in it, that's not broken — that's `Publish`'s job, and only `Publish`'s job.

**Known rough edge**, being upfront rather than promising it never happens: `dotnet watch` sometimes
has to fully restart the app (not just hot-reload in place) for structural C# changes. When that
happens, the Vite dev server (spawned automatically, see § Core concepts → "AutoRun") tries to spawn a
fresh copy — if the *previous* one hasn't finished releasing port 5173 yet, you can briefly see a
"port already in use" error. If this happens repeatedly and gets annoying: open
`appsettings.Development.json`, set `"Vite": { "Server": { "AutoRun": false } }`, and instead run
`corepack pnpm run dev` yourself in a second terminal (§ 4 below) — that Vite process then lives
independently of the ASP.NET Core process's restarts entirely, immune to this.

## 2. Core concepts (read once)

Every idea here is explained exactly once — later sections link back here instead of re-explaining.

- **Package manager / registry**: a package manager (pnpm here) downloads library code ("packages")
  from a public registry (npmjs.com) and manages a project's `node_modules` folder of installed code.
  pnpm specifically keeps one physical copy of each exact package version anywhere on your machine (a
  "content-addressable store") and **hard-links** it into each project that needs it, instead of
  copying the files per project — so installing something you already have anywhere on disk is a
  near-instant link operation, not a re-download or a re-copy.
- **Corepack**: a tool that ships built into Node.js itself. It reads a project's `package.json` for a
  `"packageManager"` field (e.g. `"pnpm@11.20.0"`) and transparently downloads/runs *that exact
  version* of pnpm the first time it's needed. This is why you never manually install pnpm — Corepack
  does it for you, per-project, automatically.
- **Dev server vs. bundler**: a *bundler* (Rollup, inside Vite) combines many source files into a few
  optimized output files for production. A *dev server* (Vite itself, in development) instead serves
  your source files directly, transforming each one (TypeScript → JavaScript, etc.) only when the
  browser actually requests it — no combining, no waiting for a full rebuild.
- **HMR (Hot Module Replacement)**: when you save a file while the dev server is running, it pushes
  just *that file's* updated code to the browser over a WebSocket connection, and the browser swaps it
  in live — no full page reload, no rebuild of anything else. This is module-granular, not
  whole-page: editing one component doesn't reset the state of unrelated ones.
- **Build manifest / content hashing**: when Vite produces a production build, it renames each output
  file to include a hash of its contents (e.g. `main-a1b2c3d4.js`) so browsers can cache it
  aggressively forever — a file only gets a new name if its content actually changed. The `manifest.
  json` file Vite also produces maps the *original* source path (e.g. `pages/home/main.ts`) to its
  *hashed* output filename, so the server-side `vite-src`/`vite-href` tag helpers can look up the
  right filename without you ever hardcoding a hash anywhere.
- **Type erasure vs. type-checking**: TypeScript code has to become plain JavaScript to run in a
  browser. Vite does this by *stripping* the type annotations as fast as possible (via esbuild) — it
  does **not** verify that your types are actually correct first. A real type error can "run" in the
  browser just fine (the incorrect type annotation is simply deleted). Separately, `tsc -b`
  (TypeScript's own compiler, in "build mode") *does* check that your types are correct, and fails
  loudly if not — that's the step wired into `pnpm run build`.
- **Why `vite.config.ts` needs a different `tsconfig` than your page code**: `vite.config.ts` itself
  runs under Node.js (it can use Node-only things like file-system paths), but everything under
  `ClientApp/components`/`pages`/`styles` runs in the browser (it can use browser-only things like
  `document`). One shared TypeScript config would force one environment's global types onto the
  other, incorrectly. That's why there are three `tsconfig*.json` files instead of one — see
  `CLAUDE.md` if you want the full detail.
- **Shadow DOM vs. light DOM**: Lit components normally render into an isolated "shadow DOM," which
  page-wide CSS (including Tailwind) cannot reach into, by design. This project's components instead
  render into "light DOM" (plain, ordinary DOM) specifically so Tailwind's classes work inside them —
  see `CLAUDE.md`'s "Why Tailwind + Lit" section for the full trade-off.
- **Runtime dependency vs. dev dependency**: a regular `"dependencies"` entry in `package.json` (like
  `lit`) ships to the browser as part of your app. A `"devDependencies"` entry (like `typescript` or
  `vite`) is a *tool* used only while building — it never ships to the browser. This distinction is
  why `pnpm add` and `pnpm add -D` are different commands (§ 4).

## 3. One-time setup (per machine)

1. **Install Node.js** (the current Active LTS version) from [nodejs.org](https://nodejs.org). Verify:
   ```
   > node -v
   v24.15.0
   ```
   (Your exact version number will differ slightly — any current LTS 24.x is fine.)
2. **Verify Corepack is present** (it ships with Node, nothing extra to install):
   ```
   > corepack -v
   0.34.6
   ```
3. **Enable Corepack's shims, once, globally** — this is the one manual step in this whole pipeline,
   and it's why it's called out on its own:
   ```
   > corepack enable
   ```
   This makes the bare `pnpm` command available on your machine's `PATH`, dispatching to whichever
   pnpm version each project you're in has pinned. Every MSBuild-driven step in this project
   (`dotnet build`/`dotnet publish`) explicitly runs `corepack pnpm ...` and works fine *without* this
   step — but `Vite.AspNetCore`'s automatic dev-server spawning (`AutoRun`, used by
   `dotnet watch run`) can only invoke `pnpm` by its bare name, so it needs this. If you skip this
   step, everything still works except `dotnet watch run` won't automatically start the frontend dev
   server for you (see the Troubleshooting entry below for the fallback).

   If this fails with a permissions error on Windows, re-run it from an elevated ("Run as
   Administrator") terminal.
4. **pnpm and TypeScript themselves need no separate installation** — both are pinned
   `devDependencies`/config inside this project (`ClientApp/package.json`'s `"packageManager"` field
   for pnpm, `"typescript"` for TypeScript) and are installed automatically the first time you build.
5. **Open the repo folder in VS Code.** It should prompt you to install a handful of recommended
   extensions (from the repo-root `.vscode/extensions.json`) — accept that prompt. The most important
   one is `lit-plugin`, which gives you autocomplete and type-checking *inside* Lit's
   `` html`...` `` template literals; without it, VS Code treats that content as an opaque string.
   The Tailwind CSS IntelliSense extension needs one workspace setting to work with this project
   (already committed in the repo-root `.vscode/settings.json`) — nothing for you to configure.

## 4. Everyday commands, in full detail

### `dotnet watch run --project RedStar.WebApp` — the command you'll use ~95% of the time

Run this from the `src/` folder. It starts the ASP.NET Core app **and** the Vite frontend dev server
together, with both halves live-reloading. Expected console output looks roughly like:

```
> dotnet watch run --project RedStar.WebApp
dotnet watch 🔥 Hot reload enabled. For a list of supported edits, see https://aka.ms/dotnet/hot-reload.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5280
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
dotnet watch 🚀 Application launched. Press Ctrl+C to shut down.
```

At this point only the ASP.NET Core app has started — the Vite dev server starts **lazily, on the
first HTTP request**, not immediately at boot. Open `http://localhost:5280` in your browser (not a
Vite URL — Vite's own port, 5173, is only used internally, proxied through by `Vite.AspNetCore`); that
first request triggers a few more lines in the same terminal:

```
info: Vite.AspNetCore.ViteDevServerLauncher[21]
      Starting the Vite development server...
info: Vite.AspNetCore.ViteDevServerLauncher[22]
      Vite development server started with process ID 6416.
info: Vite.AspNetCore.ViteDevServerLauncher[26]
      The Vite development server is running at http://localhost:5173
```

That first request typically takes a few seconds (spawning `corepack` → `pnpm` → `vite` and waiting
for it to report ready) — normal, and it only happens once per run, not per request. If instead you
see a warning that it *didn't* start, see Troubleshooting below. Leave this terminal running and just
edit files from here on — see § 1's table for what happens per file type.

Plain `dotnet run --project RedStar.WebApp` also works and starts the same two servers, but **does
not** watch for `.cs`/`.cshtml` changes — you'd have to stop (Ctrl+C) and rerun it yourself after
editing those. Use `dotnet watch run` unless you have a specific reason not to.

### `dotnet build RedStar.slnx`

Run from `src/`. Compiles every project in the solution, including `RedStar.WebApp`, and makes sure
`ClientApp/node_modules` is installed and current. Does **not** run the frontend production build
(§ 1). Useful for a quick "does everything still compile" check without starting the app.

### `dotnet publish RedStar.WebApp -c Release -o <output-directory>`

Run from `src/`. This is the only command that produces the real, final frontend bundle: it runs
`tsc -b && vite build` inside `ClientApp`, then copies the resulting hashed files into
`<output-directory>/wwwroot/dist/`. If there's a real TypeScript type error anywhere in `ClientApp`,
this command **fails** with output like:

```
> dotnet publish RedStar.WebApp -c Release -o ./publish-out
...
  $ tsc -b && vite build
components/hello-badge.ts(13,15): error TS2322: Type 'string' is not assignable to type 'number'.
RedStar.WebApp.csproj(54,5): error MSB3073: The command "corepack pnpm run build" exited with code 2.
```

Fix the reported error (read the file/line — it's a real problem in your code, not a broken pipeline)
and re-run. A successful run instead ends with the normal `dotnet publish` success summary, and
`<output-directory>/wwwroot/dist/` will contain files like `main-a1b2c3d4.js` plus a
`.vite/manifest.json`.

### Working on just the frontend, without the .NET host

Run these from `src/RedStar.WebApp/ClientApp`:

- `corepack pnpm install` — installs/updates dependencies to match `package.json`/`pnpm-lock.yaml`.
  You rarely need this manually; MSBuild runs it automatically. Useful if you want to double-check
  installation succeeds in isolation.
- `corepack pnpm run dev` — starts *just* the Vite dev server (no ASP.NET Core). Mostly useful as the
  fallback described in § 1's "known rough edge" note, or for quick component work.
- `corepack pnpm run build` — runs the real production build (`tsc -b && vite build`) without going
  through `dotnet publish` at all. Useful to check for type errors quickly.
- `corepack pnpm run preview` — after a `build`, serves the built `wwwroot/dist` output locally so you
  can sanity-check the production bundle in a browser without a full `dotnet publish`.

### Adding a new frontend library

Say you want to add a small date-formatting library. From `src/RedStar.WebApp/ClientApp`:

```
> corepack pnpm add date-fns
```

This adds `date-fns` to `package.json`'s `"dependencies"` (a **runtime** dependency — its code will
be bundled and shipped to the browser) and updates `pnpm-lock.yaml` to record the exact resolved
version. Expected output ends with something like:

```
dependencies:
+ date-fns 4.1.0

Done in 1.2s
```

If instead you're adding a *tool* that only runs during development/build and should never ship to
the browser (a linter, a testing library, etc.), use `-D`:

```
> corepack pnpm add -D some-dev-tool
```

which adds it to `"devDependencies"` instead. Either way, **`pnpm-lock.yaml` changes too — commit it
alongside `package.json`.** If you don't, a teammate (or CI) running `pnpm install` afterward could
resolve a *different* version of your new dependency than the one you actually tested against.

## 5. How do I add a new page

1. `Controllers/SettingsController.cs` — a new controller (or action), returning `View()`.
2. `Views/Settings/Index.cshtml` — container markup, plus:
   ```html
   <script type="module" vite-src="~/pages/settings/main.ts"></script>
   ```
3. `ClientApp/pages/settings/main.ts` — imports whatever `components/*` it needs.
4. A new *reusable* component (used by 2+ pages) goes in `ClientApp/components/<tag-name>.ts` and
   should override `createRenderRoot()` to render into light DOM, matching every other component in
   this project (see `CLAUDE.md` for why). A page-only component can just live inside that page's own
   folder instead.
5. Add the new entry to `ClientApp/vite.config.ts`'s `rollupOptions.input` map, e.g.:
   ```ts
   settings: resolvePath('./pages/settings/main.ts'),
   ```
6. Add `<a href="/Settings">Settings</a>` to `Views/Shared/_Layout.cshtml`'s navbar.
7. Nothing to start or restart — if `dotnet watch run` is already running, the new page works as soon
   as you save these files.

## 6. Troubleshooting

**`corepack: command not found`** — your Node.js install is too old (Corepack ships with modern
Node). Install a current LTS version from nodejs.org.

**`corepack pnpm install` fails, complaining the lockfile doesn't match `package.json`** — you (or
someone) edited `package.json` by hand without updating `pnpm-lock.yaml`. Run `corepack pnpm install`
*without* `--frozen-lockfile` once (from `ClientApp/`) to let it refresh the lockfile, review the
diff, then commit both files together.

**"Port 5173 already in use"**, especially right after `dotnet watch` restarts the app — see § 1's
"known rough edge" note. Usually resolves itself in a few seconds; if it doesn't, use the two-terminal
fallback described there.

**The page takes ~25 seconds to load the first time, then renders unstyled with no `hello-badge`
content, and your terminal shows `The Vite development server did not start within 5 seconds`** —
this is confirmed to have exactly two possible root causes; the preceding log line tells you which:
- `Failed to launch Vite development server. An error occurred trying to start process 'pnpm' ...
  The system cannot find the file specified.` — you haven't run `corepack enable` yet (§ 3). Fix:
  run it (if it fails with an `EPERM`/permissions error, you need an elevated "Run as Administrator"
  terminal for that one-time command specifically).
- `Vite development server started with process ID <N>.` immediately followed by the "did not start"
  warning, with **no** "file not found" error — `pnpm` was found and ran, but from the *wrong*
  directory (it needs to run from `ClientApp/`, where `package.json` actually lives) and exited
  almost instantly without binding to a port. Fix: confirm `appsettings.Development.json` has
  `"Vite": { "Server": { "PackageDirectory": "ClientApp" } }` — this repo already ships with it set
  correctly, so you'd only see this if it were accidentally removed.

Either way, until it's fixed you can use the two-terminal fallback from § 1's "known rough edge" note
(`AutoRun: false` + `corepack pnpm run dev` yourself) — that path needs neither `corepack enable` nor
a correct `PackageDirectory`, since you're running pnpm from the right folder yourself.

Once both are configured correctly, the *normal*, expected first-request cost is still a few seconds
(spawning `corepack` → `pnpm` → `vite` and waiting for it to report ready) — confirmed around 5-6
seconds on a typical machine, ending with `The Vite development server is running at
http://localhost:5173` and no warning. That one-time-per-run delay is normal; only the ~25 second
*failure* case above is a problem.

**Blank page or broken styling, and the browser console shows a 404 for something under
`/ClientApp/...`** — the `vite-src`/`vite-href` value has an extra `ClientApp/` in it. These paths
are relative to `ClientApp/` itself (Vite's own project root), not to the ASP.NET Core project root
— use `vite-src="~/pages/home/main.ts"`, never `vite-src="~/ClientApp/pages/home/main.ts"`. Every
view already shipped with this project uses the correct form; this only bites you when adding a new
page (§ 5) and mistyping the path.

**After `dotnet publish` and running the published output, the server logs `The manifest file was
not found` or `'<path>' was not found in Vite manifest file`, even though you can see
`wwwroot/dist/.vite/manifest.json` really exists** — `appsettings.json`'s `Vite:Base` setting tells
`Vite.AspNetCore` which subfolder under `wwwroot` to look in for the manifest; this project builds
into `wwwroot/dist/` (not directly into `wwwroot/`), so it needs `"Vite": { "Base": "dist" }`. This
repo already ships with it set correctly — you'd only see this error if it were removed, or if you
change `vite.config.ts`'s `outDir` without updating this to match.

**Edits aren't hot-reloading, but no errors either** — confirm the file you edited is actually under
`ClientApp/` (edits elsewhere don't trigger Vite's HMR, by design). If it is, a corporate VPN/proxy/
firewall blocking WebSocket connections is the next most common cause — HMR needs a live WebSocket
between the browser and the Vite dev server.

**`dotnet publish` fails with a `tsc -b`/TypeScript error** — read the reported file and line number;
it's describing a real type mismatch in your code, not a problem with the pipeline itself. Fix it and
re-run.

**Tailwind classes don't apply *inside* a component, but do apply outside it** — check that the
component overrides `createRenderRoot()` to return `this` (light DOM). If it doesn't, it's rendering
into shadow DOM, which Tailwind's global styles can't reach — see `CLAUDE.md`'s "Why Tailwind + Lit"
section.

**Tailwind classes don't apply *anywhere*, despite being spelled correctly** — check whether the class
name is being built dynamically, e.g. `` html`<div class="bg-${color}-600">` ``. Tailwind can't see
class names assembled at runtime; it only sees literal, complete strings in your source. Rewrite it to
reference complete class names directly (e.g. a small lookup object).

**Git shows a warning about line endings (LF/CRLF) when you add a new file on Windows** — informational
only, safe to ignore.

## 7. Where to look next

- [`CLAUDE.md`](CLAUDE.md) in this same folder — the architectural "why" behind every decision
  summarized here.
- The repo-root [`README.md`](../../README.md) and [`CLAUDE.md`](../../CLAUDE.md) — the whole-repo
  picture, including `RedStar.Base`/`RedStar.Cli`.

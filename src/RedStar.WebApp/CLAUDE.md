# CLAUDE.md (RedStar.WebApp)

This file is loaded automatically by Claude Code whenever work touches this directory, in addition
to the repo-root `CLAUDE.md`. It covers `RedStar.WebApp`'s architecture and the non-obvious *why*
behind its frontend pipeline. For hands-on commands, troubleshooting, and a plain-language runbook,
see [`GETTING_STARTED.md`](GETTING_STARTED.md) in this same folder instead — this file is the deeper
architectural walkthrough, that one is the "how do I actually run this" guide.

## What this is

`RedStar.WebApp` is an ASP.NET Core **MVC** project (Controllers + Views, not Razor Pages) that hosts
multiple pages, each rendered client-side by TypeScript using **Lit** web components. Navigation
between pages is a plain static `<a href>` navbar — there is deliberately no client-side router and
no single-page-app shell; each page is a normal full page load whose content happens to be built by a
small amount of client-side TypeScript rather than server-rendered Razor markup. It references
`RedStar.Base` via a normal `ProjectReference` (the same pattern `RedStar.Cli`/`RedStar.UnitTest`
already use), following the root `CLAUDE.md`'s framing of `RedStar.Base` as a library other consumers
build on — though as of this writing `RedStar.WebApp` doesn't yet call into it for anything; this
project currently exists to establish the frontend build pipeline itself, not a chat feature.

## The two problems this pipeline solves

1. **Fast iteration**: editing a `.ts`/`.css` file must update the browser live, without a `dotnet
   build`/full rebuild in the loop.
2. **Wired, not manual**: `dotnet build`/`dotnet publish` must handle the frontend pipeline on their
   own, not as a side-step a contributor has to remember to run separately.

Everything below explains the specific technical decisions made to satisfy both.

## Toolchain choices, and why

- **pnpm**, not npm/yarn, auto-provisioned via Node's **Corepack** — pnpm keeps one copy of each
  exact package version in a global content-addressable store and **hard-links** it into each
  project's `node_modules`, instead of copying files per project the way npm/yarn do. Concretely this
  means ~60–75% less disk used for an equivalent dependency tree and 3–5x faster cold installs
  (2026 benchmarks) — "installing" something you already have anywhere on the machine is a filesystem
  link, not a network download or a file copy. pnpm's `node_modules` is also **strict**, not flat: a
  package can only `import` what it explicitly declares as a dependency, not something merely hoisted
  nearby — this prevents "phantom dependency" bugs that plain npm's historical flat-hoisting allows.
  `ClientApp/package.json`'s `"packageManager": "pnpm@11.20.0"` field is what Corepack reads — the
  *first* `corepack pnpm install` anyone runs (you, a teammate, CI) transparently fetches that exact
  pnpm version and uses it; nobody ever runs `npm install -g pnpm`, and nobody can silently drift onto
  a different pnpm version than the one this repo was built against.

  (Yarn Berry/PnP was considered and rejected: it achieves comparable or better raw resolution speed
  by replacing Node's module resolution with its own resolver, which breaks tooling that assumes a
  real `node_modules` exists unless that tool has explicit PnP support. pnpm's "real `node_modules`,
  just hard-linked" model has far higher out-of-the-box compatibility with Vite/VS Code/whatever comes
  next.)

- **Vite**, not webpack/esbuild-alone/Parcel — Vite's dev server serves your own source as native,
  **unbundled** ES modules directly over HTTP (it only pre-bundles `node_modules` dependencies once,
  via esbuild). Webpack-style dev servers build a real bundle even in Development, so dev-reload
  latency grows with total app size; Vite's per-module HMR means editing one file only invalidates and
  re-fetches that module, so latency stays roughly flat as more pages/components are added — the
  literal mechanism behind "fast iteration" above, not just an optimization on top of a bundler-based
  approach. Multi-page support (`build.rollupOptions.input` as a named-entry map, see
  `ClientApp/vite.config.ts`) is native, with no plugin required. Production bundling runs on Rollup.
  Lit's own official scaffolder (`npm create vite@latest -- --template lit-ts`) is itself built on
  Vite, so this pairing is the framework author's own blessed path, not a novel combination.

- **`Vite.AspNetCore`** (NuGet, Eptagone) — `AddViteServices()`/`UseViteDevelopmentServer()` in
  `Program.cs` proxy asset requests to the Vite dev server in Development (enabling HMR) and resolve
  hashed, manifest-based asset paths in Production. The `vite-src`/`vite-href` Razor tag helpers
  (registered via `@addTagHelper *, Vite.AspNetCore` in `Views/_ViewImports.cshtml`) abstract that
  distinction away from every View — a View never has to know or care which environment it's running
  in.

  **`vite-src`/`vite-href` paths are relative to `ClientApp/` (Vite's own project root), never
  prefixed with `ClientApp/` itself** — e.g. `vite-src="~/pages/home/main.ts"`, not
  `vite-src="~/ClientApp/pages/home/main.ts"`. This matches both the Vite dev server's own URL
  scheme (confirmed directly: `http://localhost:5173/pages/home/main.ts` serves the file;
  `http://localhost:5173/ClientApp/pages/home/main.ts` 404s) and the keys Vite's build manifest uses
  (`"pages/home/main.ts"`, `"styles/app.css"` — no `ClientApp/` prefix there either). Getting this
  wrong doesn't fail loudly: Development quietly proxies to a 404 (dev tools shows it), Production
  logs `'<path>' was not found in Vite manifest file` and the tag helper renders nothing.

  **Two `Vite` config keys are required beyond the obvious `Server:Port`/`AutoRun`, confirmed by
  direct testing (both default to something that doesn't fit this project's layout):**
  - `Server:PackageDirectory` — must be set to `"ClientApp"` (in `appsettings.Development.json`,
    alongside `AutoRun`). Its default is "the .NET project's own working directory"
    (`RedStar.WebApp/`, not `RedStar.WebApp/ClientApp/`) — without this override, `AutoRun` spawns
    `pnpm run dev` from a directory with no `package.json`, which fails almost instantly (confirmed:
    the process gets a PID and is logged as "started," then exits within roughly a second without
    ever binding to its port — a red herring that looks like a process-spawning bug rather than a
    wrong-working-directory one).
  - `Vite:Base` — must be set to `"dist"` (in the base `appsettings.json`, since it governs
    Production manifest resolution, not a dev-only concern). Its default assumes the manifest and
    built assets sit directly under `wwwroot/`; this project's `vite.config.ts` outputs to
    `wwwroot/dist/` instead (§ "ClientApp/ conventions"), so without this the Production tag helpers
    fail with `The manifest file was not found` even though `dotnet publish` genuinely produced one.

  **`appsettings.Development.json`'s `Vite:Server:PackageManager` is explicitly set to `"pnpm"`.**
  `Vite.AspNetCore`'s `AutoRun` (which spawns the frontend dev server automatically when the ASP.NET
  Core app starts) defaults to invoking `npm`; without this override it would try to run a package
  manager this project doesn't use. Setting it to `"pnpm"` means `AutoRun` spawns the bare `pnpm`
  executable by name — which only exists on `PATH` once Corepack's shims are enabled (see
  `GETTING_STARTED.md`'s one-time setup: `corepack enable`, a one-time, machine-wide step). This is
  the *one* place in this pipeline that isn't fully "zero setup": every MSBuild-driven step (§ below)
  explicitly runs `corepack pnpm ...` and needs no prior `corepack enable`, but `AutoRun`'s
  configuration only accepts a bare executable name, so it depends on that one-time step having been
  run. If `AutoRun` can't find `pnpm`, the documented fallback (`GETTING_STARTED.md`) is to set
  `AutoRun` to `false` and run `corepack pnpm run dev` yourself in a second terminal instead — that
  path never depends on `corepack enable` at all.

- **Tailwind CSS v4**, via its official `@tailwindcss/vite` plugin, not Bootstrap — Tailwind is a
  utility-first CSS engine, not a component library, so it never competes with Lit for ownership of
  components the way a component-library CSS framework would. v4 removed the old
  `tailwind.config.js`/PostCSS setup for the standard flow entirely: `@import "tailwindcss";` in
  `ClientApp/styles/app.css`, the Vite plugin in `vite.config.ts`, nothing else.

- **TypeScript, pinned to major version 6** (`^6.0.x` in `ClientApp/package.json`), **deliberately
  not 7.0**. TypeScript 7.0 (a from-scratch Go-native compiler rewrite, ~10x faster) shipped the same
  month this project was scaffolded. The specific risk: `runem.lit-plugin`/`ts-lit-plugin` (§ Editor
  setup) plug directly into the TypeScript *language service* — about as deep an integration point as
  exists — and a rewrite that fundamental needs time for that ecosystem to verify compatibility. 6.0.x
  is the last mature release of the previous compiler line. Revisit this pin in a few months once the
  Lit tooling specifically has caught up to 7.0.

## Type-checking: the standard Vite mechanism, not a bespoke one

Vite's dev server and `vite build` both use esbuild purely to *strip* TypeScript types for speed —
neither validates them. `ClientApp/package.json`'s `"build"` script is exactly what Vite's own
official TypeScript templates ship (verified directly against `vitejs/vite`'s
`template-react-ts` on GitHub, adapted for Lit instead of React):

```json
"scripts": {
  "dev": "vite",
  "build": "tsc -b && vite build",
  "preview": "vite preview"
}
```

`tsc -b` (TypeScript **build mode**, using project references) type-checks the whole project and
exits non-zero on any error *before* `vite build` ever runs. `RedStar.WebApp.csproj`'s
`ViteProductionBuild` MSBuild target (§ below) calls `corepack pnpm run build` — that single command
— so a real type error fails `dotnet publish` with no extra MSBuild step, no second script, nothing
bespoke. (An earlier draft of this pipeline bolted on a separate hand-named `"typecheck"` script and
a second `<Exec>` — that reinvented something the ecosystem already standardizes; matching Vite's own
scaffold shape is simpler and more "official.")

Project-reference build mode requires the three-file `tsconfig` split under `ClientApp/`
(`tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json`) — also copied directly from Vite's
official template shape, not invented here. The reason it exists: **`vite.config.ts` runs under
Node.js, but everything under `ClientApp/components`/`pages`/`styles` runs in the browser** — two
different JS environments with different global APIs (Node's `process` vs. the browser's `window`).
One shared `tsconfig.json` would force a single, wrong-for-one-side set of global types onto both.
`tsconfig.app.json` also carries `experimentalDecorators: true` and `useDefineForClassFields: false`
— required for Lit's `@customElement`/`@property` decorators to work correctly; getting these wrong
produces silently-broken reactivity, not a compile error.

## `ClientApp/` conventions

```
src/RedStar.WebApp/
├── Controllers/HomeController.cs
├── Views/
│   ├── Home/Index.cshtml
│   ├── Shared/_Layout.cshtml       # static <nav>, one <a href> per page — no router;
│   │                                # <head> links the one shared Tailwind stylesheet
│   ├── _ViewImports.cshtml         # @addTagHelper *, Vite.AspNetCore
│   └── _ViewStart.cshtml
├── ClientApp/                      # pnpm project root; all frontend source
│   ├── package.json
│   ├── pnpm-lock.yaml              # committed
│   ├── tsconfig.json / tsconfig.app.json / tsconfig.node.json
│   ├── vite.config.ts              # multi-entry rollupOptions.input, outDir -> ../wwwroot/dist
│   ├── styles/app.css              # `@import "tailwindcss";` — one shared entry
│   ├── components/                 # shared Lit elements used by 2+ pages; light-DOM render
│   │   └── hello-badge.ts
│   └── pages/                      # one folder per MVC View = one Vite entry
│       └── home/main.ts
├── wwwroot/dist/                   # vite build output; generated at publish time only; git-ignored
└── appsettings.json / appsettings.Development.json / Program.cs / RedStar.WebApp.csproj
```

**Mapping rule**: one MVC action = one Vite entry = one
`ClientApp/pages/<controller>/<action>/main.ts` (lowercase). A Lit element used by exactly one page
lives colocated in that page's own folder; promote it to `components/` only once a second page needs
it — the same "don't build shared infrastructure before a second consumer exists" reasoning the
repo-root `CLAUDE.md` applies to `RedStar.Base`'s agent abstraction.

**Styling rule**: exactly one Tailwind entry point, `ClientApp/styles/app.css`, registered as its own
`rollupOptions.input` and linked once from `Views/Shared/_Layout.cshtml`'s `<head>` via `vite-href`.
Individual pages/components never `@import` or duplicate it.

**Steps to add a new page** (e.g. "Settings") — also copied into `GETTING_STARTED.md` so it's
discoverable from either file:
1. `Controllers/SettingsController.cs` — action returns `View()`.
2. `Views/Settings/Index.cshtml` — container markup + `<script type="module" vite-src="~/pages/settings/main.ts"></script>`.
3. `ClientApp/pages/settings/main.ts` — imports whatever `components/*` it needs.
4. New reusable component → `ClientApp/components/<tag-name>.ts` (light-DOM render, per convention);
   page-only → colocate in the page's own folder.
5. Add the entry to `vite.config.ts`'s `rollupOptions.input` map (key matching the page folder).
6. Add `<a href="/Settings">Settings</a>` to `_Layout.cshtml`'s navbar.
7. Nothing to start — the already-running dev server picks it up via HMR.

## Why Tailwind + Lit needs one specific decision: light DOM, not shadow DOM

Lit components render into **shadow DOM** by default, and shadow DOM deliberately blocks global page
CSS — including Tailwind's utility classes — from reaching inside a component. Left alone, Tailwind
classes used inside a Lit component's own template simply wouldn't apply. This project's convention:
**every Lit component overrides `createRenderRoot()` to return `this`** (light DOM), so Tailwind's
global utility classes apply identically inside and outside components, with no per-component CSS
pipeline. The trade-off — losing Lit's style/DOM isolation — is acceptable here because these are
page-level widgets for one application's own pages, not a component library meant for arbitrary
third-party embedding (the scenario shadow-DOM isolation actually protects against).

Three specific, easy-to-get-wrong consequences of this, worth knowing precisely:

1. **Tailwind sees classes inside `.ts` files automatically**, including inside Lit's
   `` html`...` `` template literals — its v4 content detection is a plain text scan across source
   files, with no idea (or need to know) whether a `class="..."` string sits in `.html`, JSX, or a
   `.ts` template literal. No Lit-specific Tailwind plugin or config is needed.
2. **Tailwind cannot see dynamically constructed class names** — a universal Tailwind constraint, not
   Lit-specific. `` html`<div class="bg-${color}-600">` `` never works: Tailwind never sees the
   literal string `bg-red-600` anywhere in source, only the un-generatable `bg-${color}-600`. Always
   spell out complete class names literally (e.g. a lookup object mapping a variant to a full class
   string).
3. **Lit's `static styles = css\`...\`` scoped-styling feature does not apply at all** to
   light-DOM-rendered components — Lit only applies `static styles` to elements rendering into a
   shadow root, by design. Tailwind utilities are the only styling mechanism these components need in
   practice; anything Tailwind genuinely can't express (e.g. a `@keyframes` animation) goes in the one
   shared `ClientApp/styles/app.css` as plain global CSS, scoped by a hand-chosen class-name
   convention (e.g. `.hello-badge__pulse`) to avoid collisions — never as component-local
   `static styles`, which would silently do nothing.

## MSBuild wiring — the Build/Publish split

Two targets in `RedStar.WebApp.csproj`. **Dependency install happens on `Build`** (cheap once cached
via `Inputs`/`Outputs` incrementality); **the actual frontend bundle build happens only on
`Publish`** — plain `dotnet build`/`dotnet run`/`dotnet watch run` never trigger `vite build`, so the
inner loop stays fast. Development-time serving comes entirely from the Vite dev server (`AutoRun`,
above), fully decoupled from MSBuild.

`EnsureClientAppDependencies` (`BeforeTargets="Build;Publish"`) runs `corepack pnpm install
--frozen-lockfile`, gated by `Inputs`/`Outputs` (a `.install-stamp` file) so it's skipped once
`node_modules` is already current with `package.json`/`pnpm-lock.yaml`. `Condition="'$(DesignTimeBuild)'
!= 'true'"` stops IDE background/IntelliSense design-time builds from spawning `pnpm` on every
keystroke — a known gotcha with `Exec`-based custom targets.

`ViteProductionBuild` (`AfterTargets="ComputeFilesToPublish"`, **deliberately not**
`BeforeTargets="Publish"`) runs `corepack pnpm run build` (i.e. `tsc -b && vite build`) then
re-globs `wwwroot/dist/**` into `ResolvedFileToPublish`. The reason for `ComputeFilesToPublish`
specifically: the SDK's implicit `wwwroot/**` content glob is evaluated once at MSBuild
*project-evaluation* time, before any target runs — on a clean checkout, `wwwroot/dist` doesn't exist
yet, so that implicit glob can never pick it up no matter when `vite build` runs relative to
`Publish`. Explicitly re-globbing `wwwroot/dist/**` *inside* a target (where item globs are evaluated
live, after `vite build` has actually run) is the only way the generated output reliably reaches the
publish payload — the same `AfterTargets="ComputeFilesToPublish"` pattern the old ASP.NET Core SPA
templates (`PublishRunWebpack`) used for this exact problem. This target is never wired to `Build`, so
it can't reintroduce the "rebuild everything" problem this whole pipeline exists to avoid.

## Config: `appsettings.Development.json` as the `appsettings.local.json` successor

`RedStar.Cli` layers `appsettings.json` → `appsettings.local.json` (hand-rolled via
`RedStarOptionsFactory`) → env vars → CLI flags. `RedStar.WebApp` doesn't need that hand-rolled
layer: `WebApplication.CreateBuilder` already loads `appsettings.json` then
`appsettings.{ASPNETCORE_ENVIRONMENT}.json` **natively**, with `Properties/launchSettings.json`'s
default profile setting `ASPNETCORE_ENVIRONMENT=Development` — so `appsettings.Development.json`
plays the same "dev-specific overrides, real values, git-tracked" role `appsettings.local.json` plays
for the CLI, using the framework's own mechanism instead of reinventing it. The dev-only `Vite`
section (`AutoRun`, `Port`, `PackageManager`) lives there, not in the shared `appsettings.json`. No
`appsettings.Production.json` exists yet — nothing WebApp-specific needs a production override.

## Why not Razor Runtime Compilation

The historical way to get `.cshtml` edits to apply without restarting was
`Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` + `.AddRazorRuntimeCompilation()`. As of .NET 10,
Microsoft has marked that package **obsolete** for development scenarios, and it explicitly
**disables .NET Hot Reload** while active — a bad trade (giving up live C# reload to get live
`.cshtml` reload). The current, Microsoft-recommended mechanism for both is the same one: **.NET Hot
Reload**, which `dotnet watch run` provides for free with zero `Program.cs`/package changes. This is
why `dotnet watch run` (not plain `dotnet run`) is this project's primary recommended command — see
`GETTING_STARTED.md`'s "when does a change take effect" table for the full breakdown per file type.

## Editor setup (VS Code)

`.vscode/extensions.json`/`.vscode/settings.json` live at the **repo root**, not here — the one
deliberate exception to "everything WebApp-specific lives in this folder," because VS Code's
single-folder-workspace mechanism only reads `.vscode/` from the repo root. `runem.lit-plugin` is not
optional polish: without it, VS Code treats the contents of every `` html`...` ``/`` css`...` ``
tagged template literal as an opaque string; with it, template-literal type-checking, autocomplete,
and CSS validation work. `bradlc.vscode-tailwindcss` needs the repo-root `.vscode/settings.json`'s
`tailwindCSS.experimental.configFile` setting to find `ClientApp/styles/app.css`, since Tailwind v4
has no `tailwind.config.js` for the extension to auto-detect.

## Explicit non-goals (current scope)

- No test project changes — `RedStar.UnitTest` doesn't reference this project; there's no business
  logic here yet to test.
- No shared/reusable "frontend build SDK" — one project, one set of targets, no abstraction built
  ahead of a second web project that would actually need it.
- No `appsettings.Production.json`.

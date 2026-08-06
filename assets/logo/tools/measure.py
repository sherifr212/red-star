import json, os, re, subprocess

D = os.path.dirname(os.path.abspath(__file__))
CHROME = os.environ.get("CHROME",
                        r"C:\Program Files\Google\Chrome\Application\chrome.exe")
src = os.path.join(D, "measure.html").replace("\\", "/")
r = subprocess.run([CHROME, "--headless", "--disable-gpu", "--dump-dom",
                    "--virtual-time-budget=2000", "file:///" + src],
                   capture_output=True, text=True)
m = re.search(r"MEASURES=(\{.*?\})", r.stdout)
if not m:
    raise SystemExit("no measures in dump:\n" + r.stdout[:800] + r.stderr[-500:])
data = json.loads(m.group(1))
json.dump(data, open(os.path.join(D, "measures.json"), "w"), indent=2)
print(json.dumps(data, indent=2))

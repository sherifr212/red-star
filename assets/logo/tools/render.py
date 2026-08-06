import json, os, subprocess, sys

D = os.path.dirname(os.path.abspath(__file__))
CHROME = os.environ.get("CHROME",
                        r"C:\Program Files\Google\Chrome\Application\chrome.exe")
SVG = os.path.join(D, "..", "svg")
PNG = os.path.join(D, "..")

man = json.load(open(os.path.join(D, "manifest.json")))
for e in man:
    src = os.path.abspath(os.path.join(SVG, e["name"])).replace("\\", "/")
    out = os.path.abspath(os.path.join(PNG, e["name"].replace(".svg", ".png")))
    if os.path.exists(out):
        os.remove(out)
    args = [CHROME, "--headless", "--disable-gpu", "--hide-scrollbars",
            "--force-device-scale-factor=2",
            "--screenshot=" + out,
            "--window-size={},{}".format(e["w"], e["h"])]
    if e["transparent"]:
        args.append("--default-background-color=00000000")
    args.append("file:///" + src)
    r = subprocess.run(args, capture_output=True, text=True)
    ok = os.path.exists(out)
    print(("OK  " if ok else "FAIL"), e["name"], os.path.getsize(out) if ok else r.stderr[-300:])

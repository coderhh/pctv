#!/usr/bin/env python3
"""Print the title and URL of every page target of the TV browser (the test page mirrors its state into its title)."""
import json, sys, urllib.request

port = sys.argv[1] if len(sys.argv) > 1 else "9222"
for p in json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json")):
    if p["type"] == "page":
        print(f'{p["title"]}  |  {p["url"][:80]}')

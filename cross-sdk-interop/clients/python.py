#!/usr/bin/env python3
# Copyright 2026 INNO LOTUS PTY LTD
# SPDX-License-Identifier: Apache-2.0
#
# Python interop client. Posts a QueryFrame to /query and prints the
# CapsFrame reduced to { count, anchor_ref, data } for cross-language diffing.
#
# Uses only urllib + json from the standard library so no pip install is
# needed. The Python SDK (nps-lib) adds frame framing / codec dispatch on
# top — that path is exercised by the SDK's own test suite.

import json
import sys
import urllib.request

URL = "http://127.0.0.1:17491/query"


def main() -> int:
    body = json.dumps({
        "anchor_ref": None,
        "filter":     {},
        "limit":      20,
    }).encode("utf-8")

    req = urllib.request.Request(
        URL, data=body, method="POST",
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=10) as resp:
        caps = json.loads(resp.read())

    projection = {
        "client":     "python",
        "count":      caps["count"],
        "anchor_ref": caps["anchor_ref"],
        "data":       caps["data"],
    }
    print(json.dumps(projection, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

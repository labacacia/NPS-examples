// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0
//
// Node.js interop client. Posts a QueryFrame to /query and prints the
// CapsFrame reduced to { count, anchor_ref, data } for cross-language diffing.
//
// Uses the built-in global fetch (Node >= 18). No npm install required.

const URL = "http://127.0.0.1:17491/query";

const resp = await fetch(URL, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    anchor_ref: null,
    filter:     {},
    limit:      20,
  }),
});

if (!resp.ok) {
  console.error(`http ${resp.status}: ${await resp.text()}`);
  process.exit(1);
}

const caps = await resp.json();

const projection = {
  client:     "nodejs",
  count:      caps.count,
  anchor_ref: caps.anchor_ref,
  data:       caps.data,
};

console.log(JSON.stringify(projection, null, 2));

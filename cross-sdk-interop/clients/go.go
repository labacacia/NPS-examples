// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0
//
// Go interop client. Posts a QueryFrame to /query and prints the
// CapsFrame reduced to { count, anchor_ref, data } for cross-language diffing.
//
// Stdlib only (net/http + encoding/json). No go modules needed when run via
// `go run`. The Go SDK adds framing + codec dispatch on top — exercised by
// the SDK's own test suite (impl/go/).

package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"time"
)

const url = "http://127.0.0.1:17491/query"

type queryFrame struct {
	AnchorRef *string        `json:"anchor_ref"`
	Filter    map[string]any `json:"filter"`
	Limit     uint           `json:"limit"`
}

type capsFrame struct {
	Count     int             `json:"count"`
	AnchorRef string          `json:"anchor_ref"`
	Data      json.RawMessage `json:"data"`
}

type projection struct {
	Client    string          `json:"client"`
	Count     int             `json:"count"`
	AnchorRef string          `json:"anchor_ref"`
	Data      json.RawMessage `json:"data"`
}

func main() {
	body, _ := json.Marshal(queryFrame{
		AnchorRef: nil,
		Filter:    map[string]any{},
		Limit:     20,
	})

	client := &http.Client{Timeout: 10 * time.Second}
	resp, err := client.Post(url, "application/json", bytes.NewReader(body))
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	defer resp.Body.Close()

	raw, err := io.ReadAll(resp.Body)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	if resp.StatusCode != 200 {
		fmt.Fprintf(os.Stderr, "http %d: %s\n", resp.StatusCode, raw)
		os.Exit(1)
	}

	var caps capsFrame
	if err := json.Unmarshal(raw, &caps); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	out, _ := json.MarshalIndent(projection{
		Client:    "go",
		Count:     caps.Count,
		AnchorRef: caps.AnchorRef,
		Data:      caps.Data,
	}, "", "  ")
	fmt.Println(string(out))
}

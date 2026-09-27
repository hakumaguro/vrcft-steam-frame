#!/bin/bash
# Run ON the Steam Frame (SteamOS, aarch64): installs Rust for the current user and builds frameeyeosc at a pinned,
# reviewed revision. Re-running moves an existing checkout to that revision, so updating this script updates the headset.
# Nothing outside $HOME is touched. Undo with:  ~/.cargo/bin/rustup self uninstall; rm -rf ~/frameeyeosc
set -euo pipefail

# frameeyeosc revision this project was tested with (review the upstream diff before changing it)
REV="${FRAMEEYEOSC_REV:-b9f0c017a0b5c08d2139891bdcdabe78746a5245}"
REPO="https://github.com/konsti219/frameeyeosc"
SRC="$HOME/frameeyeosc"

if [ -z "${FRAMEEYEOSC_NO_BUILD:-}" ] && ! command -v ~/.cargo/bin/cargo >/dev/null 2>&1; then
  curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain stable
fi

if [ ! -d "$SRC/.git" ]; then
  git init -q "$SRC"
  git -C "$SRC" remote add origin "$REPO"
fi
if [ "$(git -C "$SRC" rev-parse -q --verify HEAD 2>/dev/null || true)" != "$REV" ]; then
  git -C "$SRC" fetch -q --depth 1 origin "$REV"
  git -C "$SRC" checkout -q --detach FETCH_HEAD
fi
[ "$(git -C "$SRC" rev-parse HEAD)" = "$REV" ] || { echo "frameeyeosc checkout is not at $REV"; exit 1; }
echo "frameeyeosc commit $REV"

[ -n "${FRAMEEYEOSC_NO_BUILD:-}" ] && exit 0      # testing the checkout logic only
. ~/.cargo/env
cd "$SRC"
cargo build --release
ls -l target/release/frameeyeosc

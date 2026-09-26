#!/bin/bash
# Run ON the Steam Frame (SteamOS, aarch64): installs Rust for the current user and builds frameeyeosc.
# Nothing outside $HOME is touched. Undo with:  ~/.cargo/bin/rustup self uninstall; rm -rf ~/frameeyeosc
set -euo pipefail
if ! command -v ~/.cargo/bin/cargo >/dev/null 2>&1; then
  curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain stable
fi
. ~/.cargo/env
[ -d ~/frameeyeosc ] || git clone --depth 1 https://github.com/konsti219/frameeyeosc ~/frameeyeosc
cd ~/frameeyeosc
git log -1 --format='frameeyeosc commit %H'
cargo build --release
ls -l target/release/frameeyeosc

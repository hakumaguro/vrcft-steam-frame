#!/bin/bash
# Run ON the Steam Frame (SteamOS, aarch64): installs Rust for the current user and builds frameeyeosc at a pinned,
# reviewed revision, with the small patch below for the eye data format of SteamOS 0.4.3. Re-running moves an existing
# checkout to that revision, so updating this script updates the headset.
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
  git -C "$SRC" checkout -q --force --detach FETCH_HEAD     # --force: drops the patch below from the old revision
fi
[ "$(git -C "$SRC" rev-parse HEAD)" = "$REV" ] || { echo "frameeyeosc checkout is not at $REV"; exit 1; }
echo "frameeyeosc commit $REV"

# SteamOS 0.4.3 moved the eye record 5 bytes on (shared-memory version 5); the pinned revision only knows version 4.
# The patch picks the record offset by version and accepts both.
PATCH="$(mktemp)"
cat > "$PATCH" <<'PATCH_V5'
diff --git a/src/main.rs b/src/main.rs
index fcd685d..e79485a 100644
--- a/src/main.rs
+++ b/src/main.rs
@@ -1,4 +1,4 @@
-//! Steam Frame 0.5.0 eye bridge using the private version-4 shared-memory ABI.
+//! Steam Frame eye bridge using the private shared-memory ABI, versions 4 and 5.
 
 use clap::Parser;
 use memmap2::{MmapMut, MmapOptions};
@@ -11,8 +11,11 @@ use std::net::{SocketAddr, ToSocketAddrs, UdpSocket};
 use std::ptr;
 use std::time::Duration;
 
-const SHM_VERSION: u32 = 4;
+// Only the control fields and the eye record are touched, and both lie within the first SHM_SIZE bytes in every version.
 const SHM_SIZE: usize = 0x4f21a;
+// (version, offset of the eye record). Version 5 (SteamOS 0.4.3) inserted 5 bytes in front of the record (a flag and
+// a value for "Track Dominant Eye Only"); the record itself is unchanged.
+const SHM_LAYOUTS: [(u32, usize); 2] = [(4, 0x152), (5, 0x157)];
 const SOURCE: &str = "/dev/shm/eye-server.mmap";
 const TIMEOUT: Duration = Duration::from_secs(1);
 
@@ -61,6 +64,8 @@ const _: () = {
     assert!(offset_of!(EyeDataMmap, openness) == 0x79);
     assert!(size_of::<EyeDataMmap>() == 0xebc);
     assert!(size_of::<EyeServerMmap>() <= SHM_SIZE);
+    assert!(SHM_LAYOUTS[0].1 == offset_of!(EyeServerMmap, eye_data));
+    assert!(SHM_LAYOUTS[1].1 + size_of::<EyeDataMmap>() <= SHM_SIZE);
     assert!(size_of::<libc::pthread_mutex_t>() <= 0x30);
     assert!(8 % align_of::<libc::pthread_mutex_t>() == 0);
 };
@@ -76,6 +81,7 @@ struct Args {
 
 struct EyeSource {
     map: MmapMut,
+    eye_data: usize,
 }
 
 struct MutexGuard(*mut libc::pthread_mutex_t);
@@ -100,15 +106,17 @@ impl EyeSource {
             return Err(format!("{SOURCE}: shared memory is too small").into());
         }
         let map = unsafe { MmapOptions::new().len(SHM_SIZE).map_mut(&file)? };
-        let source = Self { map };
+        let mut source = Self { map, eye_data: 0 };
         let layout = source.layout();
         let version = u32::from_le(unsafe { ptr::read_volatile(&raw const (*layout).version) });
-        if version != SHM_VERSION {
-            return Err(format!(
-                "unsupported eye shared-memory version {version}; expected {SHM_VERSION} (Frame 0.5.0)"
-            )
-            .into());
-        }
+        source.eye_data = match SHM_LAYOUTS.iter().find(|(v, _)| *v == version) {
+            Some((_, offset)) => *offset,
+            None => {
+                return Err(
+                    format!("unsupported eye shared-memory version {version}; supported: 4 and 5").into(),
+                );
+            }
+        };
         if u32::from_le(unsafe { ptr::read_volatile(&raw const (*layout).initialized) }) != 1 {
             return Err("eye shared memory is not initialized".into());
         }
@@ -171,7 +179,7 @@ impl EyeSource {
 
         let guard = self.lock()?;
         let data = if unsafe { ptr::read_volatile(sequence_ptr) } != sequence {
-            let record_ptr = unsafe { &raw const (*self.layout()).eye_data };
+            let record_ptr = unsafe { self.map.as_ptr().add(self.eye_data).cast::<EyeDataMmap>() };
             let record = unsafe { ptr::read_unaligned(record_ptr) };
             if record.producer_state == 1 {
                 Some(EyeData {
PATCH_V5
if git -C "$SRC" apply --reverse --check "$PATCH" 2>/dev/null; then
  echo "shared-memory version 5 patch: already applied"
elif git -C "$SRC" apply --check "$PATCH" 2>/dev/null; then
  git -C "$SRC" apply "$PATCH"
  echo "shared-memory version 5 patch: applied"
else
  echo "note: the shared-memory version 5 patch does not fit this frameeyeosc revision; building it as it is"
fi
rm -f "$PATCH"

[ -n "${FRAMEEYEOSC_NO_BUILD:-}" ] && exit 0      # testing the checkout logic only
. ~/.cargo/env
cd "$SRC"
cargo build --release
ls -l target/release/frameeyeosc

cargo build --release --target wasm32-wasi
wasm-tools component new ./target/wasm32-wasi/release/bcp47.wasm     -o bcp47.wasm --adapt ./wasi_snapshot_preview1.reactor.wasm

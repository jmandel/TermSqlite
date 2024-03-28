RUSTFLAGS="--remap-path-prefix=$HOME/=home/" \
cargo build --no-default-features  --lib  --release  --target wasm32-unknown-unknown && 
wasm-opt -Oz --strip-debug -o bcp47.wasm  target/wasm32-unknown-unknown/release/bcp47.wasm